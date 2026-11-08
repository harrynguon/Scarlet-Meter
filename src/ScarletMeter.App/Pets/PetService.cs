using Microsoft.Extensions.Logging;
using ScarletMeter.App.Settings;
using ScarletMeter.Engine;

namespace ScarletMeter.App.Pets;

/// <summary>One pet as the window lists it: what the catalog knows, its count (default when none), and this run's souls.</summary>
/// <param name="Counted">Whether the meter has a count for it on this server (from the game's collection or a pickup).</param>
/// <param name="Session">Souls picked up for it this run while it was below level 3.</param>
public sealed record PetRow(PetInfo Info, PetProgress Progress, bool Counted, int Session);

/// <summary>
/// One server's pets at an instant: the server shown (null before any is known), the servers with counts, a row for every
/// pet the catalog knows and any other the game named, when the game last sent the whole collection there, and this run's
/// souls on it.
/// </summary>
public sealed record PetView(int? ServerId, IReadOnlyList<int> Servers, IReadOnlyList<PetRow> Pets, DateTimeOffset? SyncedAt, int SessionSouls)
{
    public static PetView None { get; } = new(null, [], [], null, 0);
}

/// <summary>
/// A soul notice for the overlay: a pet below level 3 given souls in the last <see cref="PetService.NoticeFor"/>, with the
/// souls since its notice appeared and its progress now.
/// </summary>
public sealed record PetNotice(PetInfo Pet, int Amount, PetProgress Progress, DateTimeOffset At);

/// <summary>
/// The player's pet counts while the meter runs: loads them once, takes each pet record the engine reads
/// (<see cref="ICombatEngine.PetRecordObserved"/>) and applies it to the collection of the server it was read on by
/// <see cref="PetRules"/>, and saves a moment after the last change, and on disposal. A collection is a server's, and the
/// records do not say which, so each is put on the server of the character being played (<see cref="ICombatEngine.Character"/>);
/// while that is not sure (a login, whose collection comes before the record naming the character; a meter started
/// mid-session; a server change until the game names the character again) records are held, in order, and put on the server
/// of the character the game names next (<see cref="ICombatEngine.ProgressObserved"/>) within <see cref="HoldFor"/>, on the
/// same connection: a move to a new one (<see cref="ICombatEngine.ConnectionStarted"/>) drops them, as the field-boss timers
/// drop a held map. Souls given to a pet below level 3 make the overlay's notice (<see cref="Notices"/>) and this run's
/// tally. Saving is off when the file must not be overwritten (<see cref="CanSave"/>); a failed save is logged once and tried
/// again with the next change. Nothing logged names a pet or a count. Every member may be called from any thread.
/// </summary>
public sealed class PetService : IDisposable
{
    /// <summary>How long after the last change the counts are saved: a farming run picks up souls every few seconds.</summary>
    public static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(2);

    /// <summary>How long a record read while the character is not sure waits for the game to name the character.</summary>
    public static readonly TimeSpan HoldFor = TimeSpan.FromMinutes(10);

    /// <summary>The most records held while the character is not sure; past it, the oldest go (the next login's collection corrects them).</summary>
    public const int MaxHeld = 256;

    /// <summary>How long a soul notice stays after the pet's last soul.</summary>
    public static readonly TimeSpan NoticeFor = TimeSpan.FromSeconds(10);

    /// <summary>The most soul notices the overlay shows at once, the latest first.</summary>
    public const int MaxNotices = 2;

    private readonly ICombatEngine engine;
    private readonly PetStore store;
    private readonly SettingsService settings;
    private readonly TimeProvider clock;
    private readonly ILogger<PetService> logger;
    private readonly Lock gate = new();
    // Orders the writes of the file (the save timer, disposal), apart from the gate the capture thread takes, so a slow disk
    // never holds up a record. saveFailing is touched only under it.
    private readonly Lock saveGate = new();
    // Under the gate: each server's counts, this run's souls by server and pet, the latest notice of each pet by server,
    // and the records held for the character to be named.
    private readonly Dictionary<int, ServerPets> servers = [];
    private readonly Dictionary<int, Dictionary<int, int>> session = [];
    private readonly Dictionary<(int Server, int Pet), PetNotice> notices = [];
    private readonly Queue<(PetRecord Record, DateTimeOffset At)> held = [];
    private int characterServer; // the server of the character the game last named this run; 0 before any
    private int? chosenServer; // a server the player chose in the window this run
    private bool unplacedLogged; // a record with no server to put it on was logged, until one is placed
    private ITimer? saveTimer;
    private bool dirty;
    private bool saveFailing;
    private bool started;
    private bool disposed;

    // One server's counts, when the game last sent its whole collection, and when a record last changed them.
    private sealed class ServerPets
    {
        public Dictionary<int, PetProgress> Pets { get; } = [];
        public DateTimeOffset? SyncedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    public PetService(ICombatEngine engine, PetStore store, SettingsService settings, TimeProvider clock, ILogger<PetService> logger)
    {
        this.engine = engine;
        this.store = store;
        this.settings = settings;
        this.clock = clock;
        this.logger = logger;
        var loaded = store.Load(clock.GetUtcNow());
        foreach (var server in loaded.File.Servers)
        {
            var pets = new ServerPets { SyncedAt = server.SyncedAt, UpdatedAt = server.UpdatedAt };
            foreach (var pet in server.Pets) pets.Pets[pet.Id] = new PetProgress(pet.Level, pet.Souls);
            servers[server.ServerId] = pets;
        }
        CanSave = loaded.CanSave;
        Notice = loaded.Notice;
    }

    /// <summary>Whether changes are saved; false when the file comes from a newer build or could not be opened or set aside.</summary>
    public bool CanSave { get; }

    /// <summary>A sentence for the window when the file was repaired or recovered, or cannot be saved; null otherwise.</summary>
    public string? Notice { get; }

    /// <summary>Raised when what the counts or notices show may have changed, on any thread.</summary>
    public event EventHandler? Changed;

    /// <summary>Follows the engine from now on. Later calls, and calls after disposal, do nothing.</summary>
    public void Start()
    {
        lock (gate)
        {
            if (started || disposed) return;
            started = true;
        }
        engine.PetRecordObserved += OnRecord;
        engine.ProgressObserved += OnProgress;
        engine.ConnectionStarted += OnConnectionStarted;
    }

    /// <summary>
    /// The pets of the server the window shows: the one the player chose this run (until the game names a character on
    /// another), else the one of the character the game named last, else the one whose counts changed last; none before any
    /// is known.
    /// </summary>
    public PetView View()
    {
        lock (gate)
        {
            int? shown = chosenServer ?? (characterServer > 0 ? characterServer : (int?)null)
                ?? (servers.Count == 0 ? (int?)null : servers.MaxBy(pair => pair.Value.UpdatedAt).Key);
            List<int> known = [.. servers.Keys];
            if (shown is { } id && !servers.ContainsKey(id)) known.Add(id);
            known.Sort();
            if (shown is not { } server) return PetView.None with { Servers = known };
            var counts = servers.GetValueOrDefault(server);
            var tally = session.GetValueOrDefault(server);
            var ids = PetCatalog.All.Select(pet => pet.Id).Union(counts?.Pets.Keys ?? Enumerable.Empty<int>()).Order();
            List<PetRow> rows = [.. ids.Select(petId => new PetRow(
                PetCatalog.Describe(petId),
                counts?.Pets.GetValueOrDefault(petId) ?? default,
                counts?.Pets.ContainsKey(petId) ?? false,
                tally?.GetValueOrDefault(petId) ?? 0))];
            return new PetView(server, known, rows, counts?.SyncedAt, tally?.Values.Sum() ?? 0);
        }
    }

    /// <summary>
    /// The overlay's soul notices at <paramref name="now"/>: the pets below level 3 given souls within <see cref="NoticeFor"/>,
    /// the latest first, at most <see cref="MaxNotices"/>, one per pet (the row keys its buttons by pet: a pet given souls on
    /// two servers within the time, around a switch, shows its latest); none while notices are off. A notice dated more than
    /// <see cref="NoticeFor"/> ahead (the PC's clock set back since) goes, rather than stay until the clock catches up.
    /// </summary>
    public IReadOnlyList<PetNotice> Notices(DateTimeOffset now)
    {
        if (!settings.Current.Pets.Notices) return [];
        lock (gate)
        {
            foreach (var key in notices.Where(pair => (now - pair.Value.At).Duration() > NoticeFor).Select(pair => pair.Key).ToList()) notices.Remove(key);
            return [.. notices.Values.GroupBy(notice => notice.Pet.Id).Select(pet => pet.MaxBy(notice => notice.At)!)
                .OrderByDescending(notice => notice.At).Take(MaxNotices)];
        }
    }

    /// <summary>The player chose a server in the window (this run only); one with no counts and not the character's is ignored.</summary>
    public void ChooseServer(int serverId)
    {
        lock (gate)
        {
            if (disposed || (!servers.ContainsKey(serverId) && serverId != characterServer)) return;
            chosenServer = serverId;
        }
        RaiseChanged();
    }

    /// <summary>
    /// Takes one pet record: what the engine's event brings (tests call it directly). It is applied to the collection of the
    /// character's server, or held while that is not sure (see the class).
    /// </summary>
    public void TakeRecord(PetRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var who = engine.Character;
        string? note = null;
        var applied = false;
        lock (gate)
        {
            if (disposed) return;
            if (who is { ServerId: > 0 } named)
            {
                Apply(named.ServerId, record);
                applied = true;
                unplacedLogged = false;
            }
            else if (who is not null)
            {
                if (!unplacedLogged) note = "A pet record came, but the server of the character being played is not known; not kept.";
                unplacedLogged = true;
            }
            else
            {
                if (held.Count == 0) note = "A pet record came while the character being played is not sure; held until the game names them.";
                if (held.Count == MaxHeld) held.Dequeue();
                held.Enqueue((record, clock.GetUtcNow()));
            }
        }
        if (note is not null) logger.LogInformation("{Note}", note);
        if (applied) RaiseChanged();
    }

    /// <summary>
    /// Takes the game naming the character (with its counts): what the engine's event brings (tests call it directly). The
    /// records held since a moment the character was not sure are applied, in order, to this character's server, those held
    /// within <see cref="HoldFor"/>.
    /// </summary>
    public void TakeProgress(CharacterProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var server = progress.Character.ServerId;
        if (server <= 0) return;
        var applied = 0;
        var changed = false;
        lock (gate)
        {
            if (disposed) return;
            var now = clock.GetUtcNow();
            while (held.TryDequeue(out var waiting))
            {
                if (now - waiting.At > HoldFor) continue;
                Apply(server, waiting.Record);
                applied++;
            }
            if (characterServer != server)
            {
                // A character on another server: the window follows it, whatever server was chosen before.
                characterServer = server;
                chosenServer = null;
                changed = true;
            }
        }
        if (applied > 0) logger.LogInformation("{Count} held pet records kept for the character the game named.", applied);
        if (applied > 0 || changed) RaiseChanged();
    }

    /// <summary>
    /// Takes a move to a new connection: what the engine's event brings (tests call it directly). Held records are dropped:
    /// they were read on the connection left, and a character named on another does not say whose server they were.
    /// </summary>
    public void TakeConnectionStarted()
    {
        int dropped;
        lock (gate)
        {
            if (disposed) return;
            dropped = held.Count;
            held.Clear();
        }
        if (dropped > 0) logger.LogInformation("{Count} held pet records dropped: the game moved to a new connection before naming the character.", dropped);
    }

    /// <summary>
    /// Saves the counts if they changed since the last save: a copy taken under the gate, written outside it. A failure is
    /// logged once until a save works, and leaves the counts to save again with the next change.
    /// </summary>
    public void SaveNow()
    {
        lock (saveGate)
        {
            PetFile file;
            lock (gate)
            {
                if (!dirty) return; // never set while saving is off (MarkChanged)
                file = new PetFile
                {
                    Servers = [.. servers.OrderBy(pair => pair.Key).Select(pair => new PetServer
                    {
                        ServerId = pair.Key,
                        Pets = [.. pair.Value.Pets.OrderBy(pet => pet.Key).Select(pet => new PetEntry(pet.Key, pet.Value.Level, pet.Value.Souls))],
                        SyncedAt = pair.Value.SyncedAt,
                        UpdatedAt = pair.Value.UpdatedAt,
                    })],
                };
                dirty = false;
            }
            try
            {
                store.Save(file);
                if (saveFailing) logger.LogInformation("Saving the pet souls works again.");
                saveFailing = false;
            }
            catch (Exception ex)
            {
                lock (gate) dirty = true;
                if (!saveFailing) logger.LogWarning(ex, "The pet souls could not be saved; trying again with the next change.");
                saveFailing = true;
            }
        }
    }

    // Under the gate: one record on a server's counts, its souls on this run's tally and the notices.
    private void Apply(int server, PetRecord record)
    {
        if (!servers.TryGetValue(server, out var counts)) servers[server] = counts = new ServerPets();
        var gains = PetRules.Apply(counts.Pets, record);
        if (record is PetCollectionRead) counts.SyncedAt = record.SeenAt;
        counts.UpdatedAt = record.SeenAt;
        if (gains.Count > 0)
        {
            if (!session.TryGetValue(server, out var tally)) session[server] = tally = [];
            foreach (var gain in gains)
            {
                tally[gain.PetId] = tally.GetValueOrDefault(gain.PetId) + gain.Amount;
                // A notice still showing gathers the souls since it appeared; a pet whose notice has gone starts a new one.
                var amount = notices.TryGetValue((server, gain.PetId), out var showing) && record.SeenAt - showing.At <= NoticeFor
                    ? showing.Amount + gain.Amount
                    : gain.Amount;
                notices[(server, gain.PetId)] = new PetNotice(PetCatalog.Describe(gain.PetId), amount, gain.After, record.SeenAt);
            }
        }
        MarkChanged();
    }

    // Under the gate: the counts differ from the file, so a save is due SaveDelay after the last change.
    private void MarkChanged()
    {
        if (!CanSave) return;
        dirty = true;
        saveTimer?.Dispose();
        saveTimer = clock.CreateTimer(_ => SaveNow(), null, SaveDelay, Timeout.InfiniteTimeSpan);
    }

    private void OnRecord(object? sender, PetRecord record) => TakeRecord(record);

    private void OnProgress(object? sender, CharacterProgress progress) => TakeProgress(progress);

    private void OnConnectionStarted(object? sender, EventArgs e) => TakeConnectionStarted();

    // A subscriber's failure is logged, never thrown into the capture thread or the page.
    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "A pet souls subscriber failed.");
        }
    }

    /// <summary>Stops taking records, stops following the engine and saves what changed. Safe to call twice.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            saveTimer?.Dispose();
            saveTimer = null;
        }
        if (started)
        {
            engine.PetRecordObserved -= OnRecord;
            engine.ProgressObserved -= OnProgress;
            engine.ConnectionStarted -= OnConnectionStarted;
        }
        SaveNow();
    }
}
