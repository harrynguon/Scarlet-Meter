using AionDpsMeter.Core.Data;
using AionDpsMeter.Services.PacketProcessing.Routing;
using AionDpsMeter.Services.Services.Entity;
using Microsoft.Extensions.Logging;
using ScarletMeter.Engine.Entries;
using ScarletMeter.Engine.Records;

namespace ScarletMeter.Engine.Upstream;

/// <summary>
/// The game's list of entry counts (Odyle energy, Nightmare, Ascension Trial and the rest) from the captured stream,
/// and whose they are. Upstream decodes no entry record, so <see cref="RecordTap"/> hands this the whole list
/// (<c>0b 61</c>, at login and after a server transfer), its updates (<c>0c 61</c>) and the player's own character
/// record (<c>33 36</c>), and it reads the first two with <see cref="EntryListRecords"/>. The character comes from
/// upstream, which reads that record itself (PlayerInfoProcessor) and marks the player as the user: the tap only takes
/// the record's entity id and asks upstream's tracker who it is, and adds the server id after the name, which upstream
/// keeps only as a short name that both sides share on some servers, and the class after it, which upstream learns only
/// from a character's skills in a fight. Upstream's pipeline is subscribed to the stream before
/// the tap (the engine resolves it first), so upstream has read the record, and the whole bundle around it, by then.
/// </summary>
/// <remarks>
/// <para>Whose the counts are follows A2Tools (data_storage.rs, <c>odyle_owner</c>; THIRD-PARTY-NOTICES.md): a
/// login's list comes some seconds before the record naming who logged in, so a whole list replaces the counts and they
/// wait for the next self record, the character known until then being possibly the one before a switch. An update
/// while a character is known is theirs; one before any self record (a meter started mid-session) waits with the counts.
/// A self record naming another character with no list between (a list lost to upstream's reassembly at a server
/// change) drops the counts, which were the last character's. A move to a new connection keeps the counts but leaves
/// the character unconfirmed: a switch moves the game to a new connection too, and its first records, the list and the
/// self record among them, can be lost (see <see cref="RecordTap"/>), so the updates after it are held until a self
/// record names the character again, and reported then if it is the same one (deep audit on #48). A self record without
/// a real name (a tutorial character's placeholder, <see cref="SelfRecords"/>) names no one.</para>
/// <para>The Kina the character holds comes from its inventory (<see cref="InventoryRecords"/>: the whole listing at world
/// entry, then each change), kept per item with the slot it is in (one slot per item: an entry in another slot is a move,
/// as in a sort, and a gone entry clears the item only in its own slot) and reported with the counts by the same rule.
/// Where the listing falls in a login is not documented, so a whole list keeps the Kina only when a listing came within
/// <see cref="LoginBurst"/> before it (the same login), never for a change alone (Codex and the deep audit on #50: a
/// change just before a switch would have gone to the next character); a listing that comes after a move to a new
/// connection, or while a character is named but with no self record within <see cref="LoginBurst"/> before it, is held,
/// not reported, until the next self record (it may be a switch's whose list was lost, or one on the same connection), which keeps it when it comes within <see cref="LoginBurst"/> after the
/// listing even if it names another character; another character named otherwise forgets it, as it does the counts; and
/// a move to a new connection before any character is named, while a listing is held, or after Kina was read with no
/// character confirmed (a change too), forgets what was read before.
/// A listing counts towards a login only on its own connection: one read before a move is no new login's (Codex on #50). Before any listing (a meter
/// started mid-session) an item is known only once seen changing.</para>
/// <para>Runs on the capture thread, inside upstream's PacketAccumulator callback (see <see cref="RecordTap"/>, which
/// contains anything that escapes), where upstream also writes its tracker, or on the demo feed's thread, whose records
/// come through <see cref="Observe"/>. Nothing logged names a character or a count: only which record a change came
/// from. A subscriber's failure is logged each time, so the rest of a bundle is still read.</para>
/// </remarks>
internal sealed class EntryListTap : IRecordReader
{
    /// <summary>Upstream's opcode for the player's own character record (bytes <c>33 36</c>).</summary>
    public const ushort SelfRecordOpcode = PacketOpcodes.PlayerInfo;

    /// <summary>How long before a login's list an inventory listing still counts as that login's (a login comes in a burst).</summary>
    public static readonly TimeSpan LoginBurst = TimeSpan.FromSeconds(10);

    private readonly Func<int, CharacterIdentity?> identify;
    private readonly Func<int, string> classNameOf;
    private readonly TimeProvider clock;
    private readonly ILogger<EntryListTap> logger;
    private readonly Lock gate = new();
    // The counts since the last whole list, each with when it was read; whose they are is `owner` unless awaitingOwner.
    private readonly Dictionary<uint, EntryCount> counts = [];
    // The Kina of each item as last read, with its slot (one slot per item); when it was last read; whether a listing came,
    // which makes a missing item none; when the last listing came (only a listing: what a whole list keeps it by); and
    // whether the Kina waits for the next self record (a listing with no self record just before it).
    private readonly Dictionary<uint, KinaSlot> kina = [];
    private DateTimeOffset kinaReadAt;
    private DateTimeOffset kinaListingAt;
    private bool kinaListed;
    private bool kinaHeld;
    // Kina read while no character was confirmed (a change or listing before the next self record): it may be another
    // character's, so it is forgotten if its connection is left before a self record confirms it.
    private bool kinaUnconfirmed;
    // When a self record last named the character: a listing within LoginBurst after it is that character's.
    private DateTimeOffset selfRecordAt;
    private OdyleEnergy? odyle;
    private CharacterIdentity? owner;
    private bool awaitingOwner;
    // The game moved to a new connection since the character was named: updates wait for a self record (see remarks).
    private bool ownerUnconfirmed;
    // A self record upstream had no name for is logged once until one is named. Touched only on the reading thread:
    // the capture thread, or the demo feed's, never both (a meter runs one or the other).
    private bool unnamedSelfLogged;

    public EntryListTap(EntityTracker entities, TimeProvider clock, ILogger<EntryListTap> logger)
        : this(entityId => UpstreamMapping.ToIdentity(entities.GetPlayerEntity(entityId)), clock, logger)
    {
    }

    /// <summary>
    /// With the character lookup of the caller's: the constructor above, and tests. Class names come from upstream's
    /// game data (<see cref="UpstreamMapping.ClassNameOf"/>) unless the caller gives its own.
    /// </summary>
    internal EntryListTap(Func<int, CharacterIdentity?> identify, TimeProvider clock, ILogger<EntryListTap> logger, Func<int, string>? classNameOf = null)
    {
        this.identify = identify;
        this.classNameOf = classNameOf ?? UpstreamMapping.ClassNameOf;
        this.clock = clock;
        this.logger = logger;
    }

    /// <summary>The latest Odyle energy the game sent, or null until it sends one (and after a login's list without it).</summary>
    public OdyleEnergy? Odyle
    {
        get { lock (gate) return odyle; }
    }

    /// <summary>
    /// The character being played, as the game last named it, or null while that is not sure: until a character is
    /// named, after a login's list until its self record, and after a move to a new connection until a self record names
    /// the character again (see remarks).
    /// </summary>
    public CharacterIdentity? Character
    {
        get { lock (gate) return awaitingOwner || ownerUnconfirmed ? null : owner; }
    }

    /// <summary>Raised when the Odyle energy changes to a value, on the capture (or demo feed) thread.</summary>
    public event EventHandler<OdyleEnergy>? OdyleUpdated;

    /// <summary>
    /// Raised with every count read since the character's list, once whose they are is known: when a self record names
    /// the character (with or without counts) and when an update changes a known character's. On the capture (or demo
    /// feed) thread.
    /// </summary>
    public event EventHandler<CharacterProgress>? ProgressObserved;

    /// <summary>The whole list, its updates and the self record.</summary>
    public bool Reads(ushort opcode) =>
        opcode is EntryListRecords.ListOpcode or EntryListRecords.UpdateOpcode or SelfRecordOpcode || InventoryRecords.IsInventory(opcode);

    /// <summary>Reads one record from the stream (<see cref="RecordTap"/>).</summary>
    public void Read(byte[] record, ushort opcode)
    {
        if (opcode == SelfRecordOpcode) TakeSelfRecord(record);
        else if (InventoryRecords.IsInventory(opcode)) TakeInventory(record);
        else TakeEntries(record);
    }

    /// <summary>
    /// Keeps the counts, which are the character's, but holds their reports until a self record names the character
    /// again: the move may be a switch whose list and self record were lost.
    /// </summary>
    public void ConnectionStarted()
    {
        lock (gate)
        {
            // Read before any character was named, held for a self record that never came, or read while the character was
            // not confirmed, on a connection now left: whose it was is not known (Codex on #50: a switch's listing or change
            // went to the character named on the next connection).
            if (owner is null || kinaHeld || kinaUnconfirmed) ForgetKina();
            // A listing read on the connection left is not the next login's: a whole list on the new one does not keep it
            // by its time (Codex on #50), while a self record naming the same character, with no list between, still does.
            kinaListingAt = default;
            if (owner is null || awaitingOwner || ownerUnconfirmed) return;
            ownerUnconfirmed = true;
        }
        logger.LogInformation("The game moved to a new connection; entry counts are held until the game names the character again.");
    }

    /// <summary>Reads one record when it is one of the tap's: the demo feed's way in (its records are never bundled), and tests'.</summary>
    internal void Observe(byte[] record)
    {
        if (GameRecords.OpcodeOf(record) is { } opcode && Reads(opcode)) Read(record, opcode);
    }

    // A whole list or an update: keeps its counts (an Odyle entry out of bounds is not kept), notes the Odyle energy, and
    // reports the counts when whose they are is known.
    private void TakeEntries(ReadOnlySpan<byte> record)
    {
        if (EntryListRecords.Read(record) is not { } read) return;
        var now = clock.GetUtcNow();
        OdyleEnergy? odyleNow = null;
        var odyleChanged = false;
        CharacterProgress? progress = null;
        lock (gate)
        {
            if (read.IsWholeList)
            {
                counts.Clear();
                if (now - kinaListingAt > LoginBurst) ForgetKina();
                awaitingOwner = true;
            }
            foreach (var count in read.Counts)
            {
                if (count.Id == EntryIds.OdyleEnergy && EntryListRecords.OdyleIn([count]) is null) continue;
                counts[count.Id] = new EntryCount(count.First, count.Second, now);
            }
            if (read.IsWholeList || read.Counts.Any(c => c.Id == EntryIds.OdyleEnergy))
            {
                odyleNow = counts.TryGetValue(EntryIds.OdyleEnergy, out var energy) ? new OdyleEnergy(energy.First, energy.Second) : null;
                odyleChanged = odyleNow != odyle;
                odyle = odyleNow;
            }
            if (!awaitingOwner && !ownerUnconfirmed && owner is not null && read.Counts.Count > 0) progress = Snapshot(owner);
        }
        if (read.IsWholeList) logger.LogInformation("Entry list read ({Count} counts); waiting for the game to name the character.", read.Counts.Count);
        if (odyleChanged)
        {
            logger.LogInformation("Odyle energy changed, read from {Record}.",
                read.IsWholeList ? "the list of entry counts (0b 61)" : "an update (0c 61)");
            if (odyleNow is { } value) Raise(() => OdyleUpdated?.Invoke(this, value), "An Odyle energy subscriber failed.");
        }
        if (progress is not null) Raise(() => ProgressObserved?.Invoke(this, progress), "An entry count subscriber failed.");
    }

    // The player's own character record: the character upstream read from it owns the counts from now on, and the counts
    // waiting for it are reported; another character with no list between drops the last one's.
    private void TakeSelfRecord(ReadOnlySpan<byte> record)
    {
        if (!SelfRecords.TryRead(record, out var entityId, out var unnamed, out var serverId, out var classCode)) return;
        if (unnamed || identify(entityId) is not { } named)
        {
            if (!unnamedSelfLogged) logger.LogInformation("The game's own character record came, but upstream has no name for it; entry counts wait for the next one.");
            unnamedSelfLogged = true;
            return;
        }
        unnamedSelfLogged = false;
        var character = WithClass(WithServerId(named, serverId), classCode);
        var now = clock.GetUtcNow();
        CharacterProgress progress;
        var dropped = false;
        var matched = false;
        lock (gate)
        {
            if (!awaitingOwner && owner is not null && !owner.IsSameCharacter(character))
            {
                counts.Clear();
                if (!kinaHeld || now - kinaListingAt > LoginBurst) ForgetKina();
                odyle = null;
                dropped = true;
            }
            else if (awaitingOwner && counts.Count > 0)
            {
                matched = true;
            }
            owner = character;
            awaitingOwner = false;
            ownerUnconfirmed = false;
            kinaHeld = false;
            kinaUnconfirmed = false;
            selfRecordAt = now;
            progress = Snapshot(character);
        }
        if (dropped) logger.LogInformation("The game named another character with no entry list between; the last character's counts, Odyle energy included, were dropped.");
        if (matched) logger.LogInformation("The entry counts were matched to the character the game named.");
        Raise(() => ProgressObserved?.Invoke(this, progress), "An entry count subscriber failed.");
    }

    // The character with the record's server id, when upstream knows that id and gave the character its short name: upstream
    // read the same two bytes for the name, so a mismatch means the identity came from another record, and no id is taken.
    private static CharacterIdentity WithServerId(CharacterIdentity character, int serverId) =>
        serverId > 0 && ServerMap.IsValidId(serverId) && ServerMap.GetName(serverId) == character.Server
            ? character with { ServerId = serverId }
            : character;

    // The character with the record's class, while upstream knows none (it learns a class only from a character's skills,
    // in a fight): only from a record whose server id checked out, so the class is read from where the record's layout
    // puts it, and only a code that is a class whose name upstream's data has. Upstream's class, once known, is kept.
    private CharacterIdentity WithClass(CharacterIdentity character, uint classCode) =>
        character.ClassId == 0 && character.ServerId > 0 && SelfRecords.ClassIdOf(classCode) is > 0 and var classId
            && classNameOf(classId) is { Length: > 0 } className
            ? character with { ClassId = classId, ClassName = className }
            : character;

    // An inventory record: its Kina slots are kept (a listing replaces them all) and reported with the counts when whose
    // they are is known.
    private void TakeInventory(ReadOnlySpan<byte> record)
    {
        if (InventoryRecords.Read(record) is not { } read) return;
        var now = clock.GetUtcNow();
        CharacterProgress? progress = null;
        lock (gate)
        {
            var named = !awaitingOwner && !ownerUnconfirmed && owner is not null;
            if (read.IsListing)
            {
                kina.Clear();
                kinaListed = true;
                kinaListingAt = now;
                // Held for the next self record: after a move to a new connection (it may be a switch whose list was
                // lost: Codex on #50), or while a character is named with no self record just before it.
                kinaHeld = ownerUnconfirmed || (named && now - selfRecordAt > LoginBurst);
            }
            foreach (var slot in read.Slots)
            {
                // A gone entry clears the item only in the slot it is in; anything else is the item's (new) slot.
                if (slot.Quantity == 0 && kina.TryGetValue(slot.Item, out var kept) && kept.Slot != slot.Slot) continue;
                kina[slot.Item] = slot;
            }
            kinaReadAt = now;
            if (!named) kinaUnconfirmed = true;
            if (named && !kinaHeld) progress = Snapshot(owner!);
        }
        if (read.IsListing) logger.LogInformation("Inventory listing read ({Slots} Kina slots).", read.Slots.Count);
        if (progress is not null) Raise(() => ProgressObserved?.Invoke(this, progress), "An entry count subscriber failed.");
    }

    // Under the gate: the Kina belonged to another character, or to a login long before this one.
    private void ForgetKina()
    {
        kina.Clear();
        kinaListed = false;
        kinaHeld = false;
        kinaUnconfirmed = false;
        kinaReadAt = default;
        kinaListingAt = default;
    }

    // Under the gate: the Kina held, each item as last read; null for an item not seen, unless a listing came (then none).
    // Null altogether before any inventory record, and while it waits for a self record.
    private KinaBalance? Kina()
    {
        if (kinaHeld || (!kinaListed && kina.Count == 0)) return null;
        long? Of(uint item) => kina.TryGetValue(item, out var slot) ? slot.Quantity : kinaListed ? 0 : null;
        return new KinaBalance(Of(InventoryRecords.KinaItem), Of(InventoryRecords.BoundKinaItem), kinaReadAt);
    }

    // Every count kept, and the Kina, as a copy for subscribers. Under the gate.
    private CharacterProgress Snapshot(CharacterIdentity character) => new(character, new Dictionary<uint, EntryCount>(counts), Kina());

    // Raises an event; a subscriber that throws is logged, so the rest of a bundle is still read.
    private void Raise(Action raise, string failure)
    {
        try
        {
            raise();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, failure);
        }
    }
}
