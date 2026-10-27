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
/// the record's entity id and asks upstream's tracker who it is. Upstream's pipeline is subscribed to the stream before
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
/// entry, then each change), kept per slot and reported with the counts by the same rule. Where the listing falls in a
/// login is not documented, so a whole list keeps a listing read within <see cref="LoginBurst"/> before it (the same
/// login) and forgets an older one; another character named with no list between forgets it, as it does the counts.
/// Before any listing (a meter started mid-session) an item's Kina is the sum of the slots seen changing.</para>
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
    private readonly TimeProvider clock;
    private readonly ILogger<EntryListTap> logger;
    private readonly Lock gate = new();
    // The counts since the last whole list, each with when it was read; whose they are is `owner` unless awaitingOwner.
    private readonly Dictionary<uint, EntryCount> counts = [];
    // The Kina slots seen since the last listing (or ever, before one), when they were last read, and whether a listing
    // came, which makes a missing slot none.
    private readonly Dictionary<byte, KinaSlot> kinaSlots = [];
    private DateTimeOffset kinaReadAt;
    private bool kinaListed;
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

    /// <summary>With the character lookup of the caller's: the constructor above, and tests.</summary>
    internal EntryListTap(Func<int, CharacterIdentity?> identify, TimeProvider clock, ILogger<EntryListTap> logger)
    {
        this.identify = identify;
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
                if (now - kinaReadAt > LoginBurst) ForgetKina();
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
        if (!SelfRecords.TryRead(record, out var entityId, out var unnamed)) return;
        if (unnamed || identify(entityId) is not { } character)
        {
            if (!unnamedSelfLogged) logger.LogInformation("The game's own character record came, but upstream has no name for it; entry counts wait for the next one.");
            unnamedSelfLogged = true;
            return;
        }
        unnamedSelfLogged = false;
        CharacterProgress progress;
        var dropped = false;
        var matched = false;
        lock (gate)
        {
            if (!awaitingOwner && owner is not null && !owner.IsSameCharacter(character))
            {
                counts.Clear();
                ForgetKina();
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
            progress = Snapshot(character);
        }
        if (dropped) logger.LogInformation("The game named another character with no entry list between; the last character's counts, Odyle energy included, were dropped.");
        if (matched) logger.LogInformation("The entry counts were matched to the character the game named.");
        Raise(() => ProgressObserved?.Invoke(this, progress), "An entry count subscriber failed.");
    }

    // An inventory record: its Kina slots are kept (a listing replaces them all) and reported with the counts when whose
    // they are is known.
    private void TakeInventory(ReadOnlySpan<byte> record)
    {
        if (InventoryRecords.Read(record) is not { } read) return;
        var now = clock.GetUtcNow();
        CharacterProgress? progress = null;
        lock (gate)
        {
            if (read.IsListing)
            {
                kinaSlots.Clear();
                kinaListed = true;
            }
            foreach (var slot in read.Slots) kinaSlots[slot.Slot] = slot;
            kinaReadAt = now;
            if (!awaitingOwner && !ownerUnconfirmed && owner is not null) progress = Snapshot(owner);
        }
        if (read.IsListing) logger.LogInformation("Inventory listing read ({Slots} Kina slots).", read.Slots.Count);
        if (progress is not null) Raise(() => ProgressObserved?.Invoke(this, progress), "An entry count subscriber failed.");
    }

    // Under the gate: the Kina slots belonged to another character, or to a login long before this one.
    private void ForgetKina()
    {
        kinaSlots.Clear();
        kinaListed = false;
        kinaReadAt = default;
    }

    // Under the gate: the Kina held, each item the sum of its slots; null for an item no slot of which was seen, unless a
    // listing came (then none). Null altogether before any inventory record.
    private KinaBalance? Kina()
    {
        if (!kinaListed && kinaSlots.Count == 0) return null;
        long? Of(uint item)
        {
            var slots = kinaSlots.Values.Where(slot => slot.Item == item).ToList();
            return slots.Count > 0 ? slots.Sum(slot => slot.Quantity) : kinaListed ? 0 : null;
        }
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
