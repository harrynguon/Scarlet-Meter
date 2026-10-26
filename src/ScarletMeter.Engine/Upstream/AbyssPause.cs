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
/// <item>the fight is cleared on entering and every <see cref="SweepInterval"/>, which drops the boss hits that filter
/// lets through (a world boss with hundreds of players on it), so nothing piles up; upstream's reset never completes a
/// fight, so nothing is saved to history either;</item>
/// <item>the engine shows nothing (an empty snapshot, no details), and the overlay says so.</item>
/// </list>
/// Capture keeps running: leaving the Abyss is a server change, at which the map is forgotten
/// (<see cref="MapLoadTap.ConnectionStarted"/>), and that ends the pause. Upstream still reads every packet meanwhile,
/// as RATmeter itself does; only stopping capture would save that, and then the meter could not tell when the player
/// leaves.
/// </summary>
/// <remarks>
/// The map changes on the capture thread, the setting on the UI thread and the sweep on a timer thread, so the pause's
/// state changes under a lock; upstream's reset takes upstream's own session lock. Nothing escapes: an exception from a
/// timer callback would end the process, and one thrown back into a map or settings change would reach the capture
/// thread or the settings window.
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
        }
        Clear();
    }

    private void OnMapChanged(object? sender, int? mapId) => Update();

    private void OnSettingsChanged(object? sender, EventArgs e) => Update();

    /// <summary>
    /// Pauses or resumes to match the setting and the last map read; on either change the fight is cleared, so the
    /// Abyss starts from nothing and so does the place after it. Both are read under the gate, so a map change and a
    /// settings change at once cannot leave the state matching an older read of either; after disposal (a map change on
    /// the capture thread can race it) it does nothing, so the disposed timer is never touched. Internal for tests.
    /// </summary>
    internal void Update()
    {
        int? map;
        bool pause;
        lock (gate)
        {
            map = maps.Current;
            pause = settings.PauseInAbyss && map is { } id && AbyssMaps.Contains(id);
            if (disposed || pause == state.Paused) return;
            state.Paused = pause;
            var period = pause ? sweepInterval : Timeout.InfiniteTimeSpan;
            sweep.Change(period, period);
        }
        if (pause) logger.LogInformation("Meter paused: in the Abyss (map {MapId}).", map);
        else logger.LogInformation("Meter tracking again.");
        Clear();
    }

    // Drops every target's fight (upstream's reset: nothing is completed or saved), letting nothing escape.
    private void Clear()
    {
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
