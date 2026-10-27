using Microsoft.Extensions.Logging;
using ScarletMeter.Engine;
using ScarletMeter.Engine.Maps;

namespace ScarletMeter.App.Roster;

/// <summary>
/// The character roster while the meter runs: loads it once, takes each character the engine reports with its entry
/// counts (<see cref="ICombatEngine.ProgressObserved"/>) and each entry into the Artifact Corridor
/// (<see cref="ICombatEngine.MapChanged"/>, put on <see cref="ICombatEngine.Character"/>), and saves a moment after the
/// last change, and on disposal. A
/// count replaces the saved one only when it was read later, so an older report can never undo a newer one. Saving is
/// off when the file must not be overwritten (<see cref="CanSave"/>); a failed save is logged once and tried again with
/// the next change. Nothing logged names a character or a count. Every member may be called from any thread.
/// </summary>
public sealed class RosterService : IDisposable
{
    /// <summary>How long after the last change the roster is saved: the game sends a login's counts in a burst.</summary>
    public static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How soon after a character's last corridor entry a load of the corridor straight after an unknown map is taken as
    /// the same entry again (a reconnection inside it), not a new one: a corridor lasts two minutes.
    /// </summary>
    public static readonly TimeSpan CorridorReentryWindow = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long a corridor entry seen while the character being played is not sure waits for the game to name the
    /// character again: the game sends the record naming them at a zone load and every few minutes after (A2Tools).
    /// </summary>
    public static readonly TimeSpan CorridorConfirmWindow = TimeSpan.FromMinutes(10);

    private readonly ICombatEngine engine;
    private readonly RosterStore store;
    private readonly TimeProvider clock;
    private readonly ILogger<RosterService> logger;
    private readonly Lock gate = new();
    // Orders the writes of the file (the save timer, disposal), apart from the gate the capture thread takes, so a slow
    // disk never holds up a report. saveFailing is touched only under it.
    private readonly Lock saveGate = new();
    private readonly Dictionary<RosterKey, RosterCharacter> characters = [];
    private RosterKey? current;
    // The map the engine named last (null: not known), to see the corridor being entered rather than loaded again, and the
    // last map it knew, to tell a reconnection inside the corridor (unknown, then the corridor again) from an entry.
    private int? lastMap;
    private int? lastKnownMap;
    // Corridor entries seen while the character being played was not sure, each waiting for the game to name the character
    // the roster knew then (deep audit on #49: a run seen just before the game named the character again was lost).
    private readonly List<HeldRun> heldRuns = [];

    // A held corridor entry: when, for whom it would count, and whether it came straight after an unknown map having
    // last known the corridor (a reconnection inside it, for the re-entry rule).
    private readonly record struct HeldRun(DateTimeOffset At, RosterKey Expected, bool Reconnection);
    private ITimer? saveTimer;
    private bool dirty;
    private bool saveFailing;
    private bool started;
    private bool disposed;

    public RosterService(ICombatEngine engine, RosterStore store, TimeProvider clock, ILogger<RosterService> logger)
    {
        this.engine = engine;
        this.store = store;
        this.clock = clock;
        this.logger = logger;
        var loaded = store.Load(clock.GetUtcNow());
        foreach (var character in loaded.File.Characters) characters[character.Key] = character;
        CanSave = loaded.CanSave;
        Notice = loaded.Notice;
    }

    /// <summary>Whether changes are saved; false when the file comes from a newer build or could not be opened or set aside.</summary>
    public bool CanSave { get; }

    /// <summary>A sentence for the page when the file was repaired or recovered, or cannot be saved; null otherwise.</summary>
    public string? Notice { get; }

    /// <summary>The character the game named last this run, or null until it names one.</summary>
    public RosterKey? Current
    {
        get { lock (gate) return current; }
    }

    /// <summary>Raised after the roster changed (a report, a removal, a cap), on the thread that changed it.</summary>
    public event EventHandler? Changed;

    /// <summary>Every character, in no particular order.</summary>
    public IReadOnlyList<RosterCharacter> Characters
    {
        get { lock (gate) return [.. characters.Values]; }
    }

    /// <summary>Follows the engine from now on. Later calls, and calls after disposal, do nothing.</summary>
    public void Start()
    {
        lock (gate)
        {
            if (started || disposed) return;
            started = true;
        }
        engine.ProgressObserved += OnProgress;
        engine.MapChanged += OnMapChanged;
    }

    /// <summary>Takes one report of a character and its counts: what the engine's event brings (tests call it directly).</summary>
    public void Take(CharacterProgress progress)
    {
        var now = clock.GetUtcNow();
        var held = (Counted: 0, Dropped: 0);
        lock (gate)
        {
            if (disposed) return;
            var who = progress.Character;
            var key = new RosterKey(who.Server, who.Name);
            var character = characters.TryGetValue(key, out var known) ? known : new RosterCharacter { Name = who.Name, Server = who.Server };
            character = character with
            {
                ClassId = who.ClassId > 0 ? who.ClassId : character.ClassId,
                ClassName = who.ClassName.Length > 0 ? who.ClassName : character.ClassName,
                CombatPower = who.CombatPower > 0 ? who.CombatPower : character.CombatPower,
                LastSeen = now,
                Odyle = Later(character.Odyle, progress.Entries, EntryIds.OdyleEnergy, now),
                Nightmare = Later(character.Nightmare, progress.Entries, EntryIds.Nightmare, now),
                Ascension = Later(character.Ascension, progress.Entries, EntryIds.AscensionTrial, now),
            };
            if (character.Odyle is { First: > RosterRules.StandardCap }) character = character with { MembershipSeen = true };
            characters[key] = character;
            current = key;
            MarkChanged();
            held = SettleHeldRuns(who, now);
        }
        if (held.Counted > 0) logger.LogInformation("{Count} held Artifact Corridor entries counted: the game named the same character again.", held.Counted);
        if (held.Dropped > 0) logger.LogInformation("{Count} held Artifact Corridor entries dropped: the game named another character, or none in time.", held.Dropped);
        RaiseChanged();
    }

    // Under the gate: the held corridor entries once the game has named `who`. Those for this character, still within
    // CorridorConfirmWindow, are counted; the rest (another character named, so a switch; or too late) are dropped.
    private (int Counted, int Dropped) SettleHeldRuns(CharacterIdentity who, DateTimeOffset now)
    {
        if (heldRuns.Count == 0) return (0, 0);
        var key = new RosterKey(who.Server, who.Name);
        var counted = 0;
        foreach (var run in heldRuns.Where(run => run.Expected == key && now - run.At <= CorridorConfirmWindow))
            if (AddRun(who, run.At, run.Reconnection, now)) counted++;
        var dropped = heldRuns.Count - counted;
        heldRuns.Clear();
        return (counted, dropped);
    }

    /// <summary>Removes a character (the player's choice); it comes back the next time the game names it.</summary>
    public void Remove(RosterKey key)
    {
        lock (gate)
        {
            if (disposed || !characters.Remove(key)) return;
            MarkChanged();
        }
        RaiseChanged();
    }

    /// <summary>Sets how a character's basic energy cap is chosen.</summary>
    public void SetOdyleCap(RosterKey key, OdyleCapChoice choice)
    {
        if (!Enum.IsDefined(choice)) throw new ArgumentOutOfRangeException(nameof(choice));
        lock (gate)
        {
            if (disposed || !characters.TryGetValue(key, out var character) || character.OdyleCap == choice) return;
            characters[key] = character with { OdyleCap = choice };
            MarkChanged();
        }
        RaiseChanged();
    }

    // The count from the report when it was read later than the one kept; the one kept otherwise. A kept count dated after
    // now (a clock set back since) does not count as later than anything, or no read would ever replace it (deep audit
    // on #48).
    private static RosterCount? Later(RosterCount? kept, IReadOnlyDictionary<uint, EntryCount> entries, uint id, DateTimeOffset now)
    {
        if (!entries.TryGetValue(id, out var read)) return kept;
        if (kept is not null && kept.SeenAt <= now && read.SeenAt <= kept.SeenAt) return kept;
        return new RosterCount(read.First, read.Second, read.SeenAt);
    }

    /// <summary>
    /// Takes one map change: what the engine's event brings (tests call it directly). Entering the Artifact Corridor from
    /// another map counts a run for the character being played. While that is not sure (the engine's
    /// <see cref="ICombatEngine.Character"/> is null, as after a server change until the game names the character again),
    /// the entry is held for the character the roster knew, and counted if the game names that same character within
    /// <see cref="CorridorConfirmWindow"/> (<see cref="Take"/>). A load of the corridor straight after an unknown map, the
    /// last known map being the corridor, within <see cref="CorridorReentryWindow"/> of the last run is that run again (a
    /// reconnection inside it).
    /// </summary>
    public void TakeMap(int? mapId)
    {
        var now = clock.GetUtcNow();
        var who = engine.Character;
        var counted = false;
        string? note = null;
        lock (gate)
        {
            if (disposed) return;
            var previous = lastMap;
            var lastKnown = lastKnownMap;
            lastMap = mapId;
            if (mapId is not null) lastKnownMap = mapId;
            if (mapId != MapNames.ArtifactCorridor || previous == MapNames.ArtifactCorridor) return;
            var reconnection = previous is null && lastKnown == MapNames.ArtifactCorridor;
            if (who is not null)
            {
                counted = AddRun(who, now, reconnection, now);
            }
            else if (current is { } expected)
            {
                heldRuns.RemoveAll(run => now - run.At > CorridorConfirmWindow);
                if (heldRuns.Count < RosterRules.CorridorsPerSiege) heldRuns.Add(new HeldRun(now, expected, reconnection));
                note = "An Artifact Corridor entry was seen while the character being played is not sure; it is held until the game names them again.";
            }
            else
            {
                note = "An Artifact Corridor entry was seen before the game named any character; not counted.";
            }
        }
        if (note is not null) logger.LogInformation("{Note}", note);
        if (counted) RaiseChanged();
    }

    // Under the gate: a run at `at` for `who`, unless it is the last run again (a reconnection inside the corridor within
    // CorridorReentryWindow). Runs dated after now (a clock set back since) are dropped first, so they neither swallow
    // new runs nor break the order (Codex on #49). A character removed from the roster comes back with what is known of it.
    private bool AddRun(CharacterIdentity who, DateTimeOffset at, bool reconnection, DateTimeOffset now)
    {
        var key = new RosterKey(who.Server, who.Name);
        var character = characters.TryGetValue(key, out var known) ? known : new RosterCharacter
        {
            Name = who.Name,
            Server = who.Server,
            ClassId = Math.Max(0, who.ClassId),
            ClassName = who.ClassName,
            CombatPower = Math.Max(0, who.CombatPower),
        };
        List<DateTimeOffset> runs = [.. character.CorridorRuns.Where(run => run <= now)];
        if (reconnection && runs.Count > 0 && at >= runs[^1] && at - runs[^1] < CorridorReentryWindow) return false;
        characters[key] = character with
        {
            LastSeen = now,
            CorridorRuns = [.. runs.Append(at).Order().TakeLast(RosterRules.CorridorRunsKept)],
        };
        MarkChanged();
        return true;
    }

    private void OnProgress(object? sender, CharacterProgress progress) => Take(progress);

    private void OnMapChanged(object? sender, int? mapId) => TakeMap(mapId);

    // Under the gate: the roster differs from the file, so a save is due SaveDelay after the last change.
    private void MarkChanged()
    {
        if (!CanSave) return;
        dirty = true;
        saveTimer?.Dispose();
        saveTimer = clock.CreateTimer(_ => SaveNow(), null, SaveDelay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Saves the roster if it changed since the last save: a copy taken under the gate, written outside it. A failure is
    /// logged once until a save works, and leaves the roster to save again with the next change.
    /// </summary>
    public void SaveNow()
    {
        lock (saveGate)
        {
            RosterFile roster;
            lock (gate)
            {
                if (!dirty) return; // never set while saving is off (MarkChanged)
                roster = new RosterFile { Characters = [.. characters.Values.OrderBy(c => c.Server, StringComparer.Ordinal).ThenBy(c => c.Name, StringComparer.Ordinal)] };
                dirty = false;
            }
            try
            {
                store.Save(roster);
                if (saveFailing) logger.LogInformation("Saving the character roster works again.");
                saveFailing = false;
            }
            catch (Exception ex)
            {
                lock (gate) dirty = true;
                if (!saveFailing) logger.LogWarning(ex, "The character roster could not be saved; trying again with the next change.");
                saveFailing = true;
            }
        }
    }

    // A subscriber's failure is logged, never thrown into the capture thread or the page.
    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "A character roster subscriber failed.");
        }
    }

    /// <summary>Stops taking changes, stops following the engine and saves what changed. Safe to call twice.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            saveTimer?.Dispose();
            saveTimer = null;
        }
        engine.ProgressObserved -= OnProgress;
        engine.MapChanged -= OnMapChanged;
        SaveNow();
    }
}
