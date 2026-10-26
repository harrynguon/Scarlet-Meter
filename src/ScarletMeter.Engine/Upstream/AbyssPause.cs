using AionDpsMeter.Services.Services.Session;
using Microsoft.Extensions.Logging;
using ScarletMeter.Engine.Maps;

namespace ScarletMeter.Engine.Upstream;

/// <summary>
/// Pauses the meter while the player is in the Abyss (<see cref="AbyssMaps"/>), where hundreds of players fight and
/// upstream would keep every hit of everyone it sees, so the meter costs more the longer the fight goes on. Paused
/// while <see cref="EngineSettings.PauseInAbyss"/> is on and the last map read is the Abyss's:
/// <list type="bullet">
/// <item>upstream's own boss-only filter is forced on (<see cref="UpstreamSettingsBridge"/> reads <see cref="PauseState"/>),
/// which drops every hit on players and mobs before upstream keeps it;</item>
/// <item>on entering, the fight from before (a boss just killed in a dungeon, say) is completed and saved first, as at
/// exit, since upstream saves a fight only at a later hit, a new try or exit and its reset saves nothing (deep audit on
/// PR #42: going straight to the Abyss after a kill lost it); then, and every <see cref="SweepInterval"/>, the fight is
/// reset, which drops the boss hits the filter lets through (a world boss with hundreds of players on it) without
/// saving them, so nothing piles up and no Abyss fight reaches the history; leaving resets it once more, and the
/// engine drops it before its exit save when the meter closes while paused;</item>
/// <item>the engine shows nothing (an empty snapshot, no details), and the overlay says so.</item>
/// </list>
/// Capture keeps running: leaving the Abyss is a server change, at which the map is forgotten
/// (<see cref="MapLoadTap.ConnectionStarted"/>), and that ends the pause. Upstream still reads every packet meanwhile,
/// as RATmeter itself does; only stopping capture would save that, and then the meter could not tell when the player
/// leaves.
/// </summary>
/// <remarks>
/// The map changes on the capture thread, the setting on the UI thread and the sweep on a timer thread, so the pause's
/// state changes, and the fight is saved or reset, under one lock (the gate), so a sweep under way cannot reset the next
/// place's first moments after the pause ends (deep audit on PR #42). Upstream's save and reset take upstream's own
/// session lock inside the gate; nothing takes them the other way round, since upstream's boss-only filter reads the
/// pause through <see cref="PauseState"/>'s volatile flag, never the gate. Nothing escapes: an exception from a timer
/// callback would end the process, and one thrown back into a map or settings change would reach the capture thread or
/// the settings window.
/// </remarks>
internal sealed class AbyssPause : IDisposable
{
    /// <summary>How often the fight is cleared while paused.</summary>
    internal static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(2);

    private readonly MapLoadTap maps;
    private readonly EngineSettings settings;
    private readonly CombatSessionManager sessions;
    private readonly PauseState state;
    private readonly ILogger<AbyssPause> logger;
    private readonly Lock gate = new();
    private readonly Timer sweep;
    private readonly TimeSpan sweepInterval;
    private bool disposed;

    public AbyssPause(MapLoadTap maps, EngineSettings settings, CombatSessionManager sessions, PauseState state, ILogger<AbyssPause> logger)
        : this(maps, settings, sessions, state, logger, SweepInterval)
    {
    }

    /// <summary>With a sweep interval of the caller's: the constructor above, and tests.</summary>
    internal AbyssPause(
        MapLoadTap maps, EngineSettings settings, CombatSessionManager sessions, PauseState state, ILogger<AbyssPause> logger, TimeSpan sweepInterval)
    {
        this.maps = maps;
        this.settings = settings;
        this.sessions = sessions;
        this.state = state;
        this.logger = logger;
        this.sweepInterval = sweepInterval;
        sweep = new Timer(_ => Sweep(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        maps.Changed += OnMapChanged;
        settings.Changed += OnSettingsChanged;
        Update();
    }

    /// <summary>True while the meter is paused for the Abyss.</summary>
    public bool Paused => state.Paused;

    /// <summary>Clears the fight while paused (the timer's callback); nothing otherwise.</summary>
    internal void Sweep()
    {
        lock (gate)
        {
            if (disposed || !state.Paused) return;
            Clear(saveFirst: false);
        }
    }

    private void OnMapChanged(object? sender, int? mapId) => Update();

    private void OnSettingsChanged(object? sender, EventArgs e) => Update();

    /// <summary>
    /// Pauses or resumes to match the setting and the last map read; on either change the fight is cleared, so the
    /// Abyss starts from nothing and so does the place after it, and on pausing the fight from before is saved first.
    /// Both are read under the gate, so a map change and a settings change at once cannot leave the state matching an
    /// older read of either; after disposal (a map change on the capture thread can race it) it does nothing, so the
    /// disposed timer is never touched. Internal for tests.
    /// </summary>
    internal void Update()
    {
        lock (gate)
        {
            var map = maps.Current;
            var pause = settings.PauseInAbyss && map is { } id && AbyssMaps.Contains(id);
            if (disposed || pause == state.Paused) return;
            state.Paused = pause;
            var period = pause ? sweepInterval : Timeout.InfiniteTimeSpan;
            sweep.Change(period, period);
            if (pause) logger.LogInformation("Meter paused: in the Abyss (map {MapId}).", map);
            else logger.LogInformation("Meter tracking again.");
            Clear(saveFirst: pause);
        }
    }

    // Drops every target's fight (upstream's reset: nothing is completed or saved); with saveFirst, the open fights are
    // completed and saved before, as at exit (upstream saves only boss fights). Each step lets nothing escape, and a
    // failed save still resets.
    private void Clear(bool saveFirst)
    {
        if (saveFirst)
        {
            try
            {
                sessions.CompleteAndPersistActiveSessions();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Saving the fight before the Abyss pause failed.");
            }
        }
        try
        {
            sessions.Reset();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Clearing the fight for the Abyss pause failed.");
        }
    }

    /// <summary>Stops following the map and the setting and stops the sweep. Safe to call twice.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
        }
        maps.Changed -= OnMapChanged;
        settings.Changed -= OnSettingsChanged;
        sweep.Dispose();
    }
}
