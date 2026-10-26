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
    // The map the engine named last (null: not known), to see the corridor being entered rather than loaded again.
    private int? lastMap;
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
        }
        RaiseChanged();
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
    /// another map counts a run for the character being played; nothing is counted while that is not sure (the engine's
    /// <see cref="ICombatEngine.Character"/> is null), and a load of the corridor straight after an unknown map within
    /// <see cref="CorridorReentryWindow"/> of the last run is the same run (a reconnection inside it).
    /// </summary>
    public void TakeMap(int? mapId)
    {
        var now = clock.GetUtcNow();
        var who = engine.Character;
        var counted = false;
        var unsure = false;
        lock (gate)
        {
            if (disposed) return;
            var previous = lastMap;
            lastMap = mapId;
            if (mapId != MapNames.ArtifactCorridor || previous == MapNames.ArtifactCorridor) return;
            if (who is null)
            {
                unsure = true;
            }
            else
            {
                var key = new RosterKey(who.Server, who.Name);
                var character = characters.TryGetValue(key, out var known) ? known : new RosterCharacter { Name = who.Name, Server = who.Server };
                var runs = character.CorridorRuns;
                if (previous is null && runs.Count > 0 && now - runs[^1] < CorridorReentryWindow) return;
                characters[key] = character with
                {
                    LastSeen = now,
                    CorridorRuns = [.. runs.TakeLast(RosterRules.CorridorRunsKept - 1), now],
                };
                MarkChanged();
                counted = true;
            }
        }
        if (unsure) logger.LogInformation("An Artifact Corridor entry was seen while the character being played is not sure; not counted.");
        if (counted) RaiseChanged();
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
