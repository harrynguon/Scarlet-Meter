using Microsoft.Extensions.Logging;
using ScarletMeter.App.Settings;
using ScarletMeter.Engine;
using ScarletMeter.WorldEvents.FieldBosses;

namespace ScarletMeter.App.FieldBosses;

/// <summary>What the shared timers last gave: for which server, its observations, when they were asked for, and why none came (null when they did).</summary>
public sealed record FieldBossFeedState(int? ServerId, IReadOnlyList<FieldBossTimer> Timers, DateTimeOffset? ReadAt, string? Problem, bool Reading)
{
    public static FieldBossFeedState None { get; } = new(null, [], null, null, false);
}

/// <summary>
/// What the meter knows of a server's field bosses at an instant: the server, each boss's newest observation from the
/// player's own map and the shared timers (<see cref="FieldBossBoard.Merge"/>), the shared timers' state, and when the
/// player's map was last read on that server (null when not this run).
/// </summary>
public sealed record FieldBossView(int? ServerId, IReadOnlyDictionary<int, FieldBossTimer> Board, FieldBossFeedState Feed, DateTimeOffset? MapReadAt);

/// <summary>
/// The field-boss timers: the player's own world map as the game sends it when they open it
/// (<see cref="ICombatEngine.FieldBossMapObserved"/>) and NotMeter's shared timers (<see cref="FieldBossFeedClient"/>), for the
/// server in <see cref="FieldBossSettings.ServerId"/>. A map reading is the world's on the server it was read on, which the
/// record does not say, so it is put on the server of the character being played (<see cref="ICombatEngine.Character"/>);
/// while that is not sure (a meter started mid-session, or a server change until the game names the character again) it is
/// held, and put on the server of the character the game names next within <see cref="HoldFor"/> on the same connection
/// (a move drops it), as the roster holds the Abyss's artifact holders. The server of the character the game names is kept
/// in the settings whenever it changes, so the timers show with the game closed; a server the player chooses in the window
/// stays until the game names a character on another. The shared timers are read every <see cref="RefreshInterval"/> while
/// they are wanted: while the Field bosses window is open (<see cref="Watch"/>) or a boss has a notice, with
/// <see cref="FieldBossSettings.SharedTimers"/> on and a Global server known. Readings of the player's map are kept in memory
/// for the run.
/// </summary>
public sealed class FieldBossService : IDisposable
{
    /// <summary>How often the shared timers are read while wanted; NotMeter's own meter reads them every two minutes.</summary>
    public static TimeSpan RefreshInterval { get; } = TimeSpan.FromMinutes(3);

    /// <summary>How long a map read while the character is not sure waits for the game to name the character.</summary>
    public static TimeSpan HoldFor { get; } = TimeSpan.FromMinutes(10);

    private readonly ICombatEngine engine;
    private readonly SettingsService settings;
    private readonly FieldBossFeedClient feed;
    private readonly TimeProvider clock;
    private readonly ILogger<FieldBossService> logger;
    private readonly Lock gate = new();
    // Under the gate: each server's bosses as the player's map last showed them, and when the map was last read there.
    private readonly Dictionary<int, Dictionary<int, FieldBossTimer>> mapTimers = [];
    private readonly Dictionary<int, DateTimeOffset> mapReadAt = [];
    private (FieldBossMap Map, DateTimeOffset At)? held;
    private int characterServer; // the server of the character the game last named this run; 0 before any
    private FieldBossFeedState feedState = FieldBossFeedState.None;
    private int watchers;
    private int fetchGeneration; // a read whose generation is no longer current is not taken (the server or the demand changed)
    private CancellationTokenSource? fetching;
    private ITimer? timer;
    // The thread Start ran on (the UI thread in the app), where the server is saved, so the settings' subscribers run there
    // in the order the game named the characters; null in tests without one, which then save on the thread pool.
    private SynchronizationContext? settingsContext;
    private bool started;
    private bool disposed;

    public FieldBossService(ICombatEngine engine, SettingsService settings, FieldBossFeedClient feed, TimeProvider clock, ILogger<FieldBossService> logger)
    {
        this.engine = engine;
        this.settings = settings;
        this.feed = feed;
        this.clock = clock;
        this.logger = logger;
    }

    /// <summary>Raised when what the timers show may have changed, on any thread.</summary>
    public event EventHandler? Changed;

    /// <summary>Follows the engine and the settings from now on. Later calls, and calls after disposal, do nothing.</summary>
    public void Start()
    {
        lock (gate)
        {
            if (started || disposed) return;
            started = true;
            settingsContext = SynchronizationContext.Current;
        }
        engine.FieldBossMapObserved += OnMap;
        engine.ProgressObserved += OnProgress;
        engine.ConnectionStarted += OnConnectionStarted;
        settings.Changed += OnSettingsChanged;
        Reschedule();
    }

    /// <summary>
    /// The timers of the settings' server at <paramref name="now"/>: the player's map, and the shared timers' last read only
    /// while "Read other players' shared timers" is on (deep audit on #75: turned off, the last read was still shown).
    /// </summary>
    public FieldBossView View(DateTimeOffset now)
    {
        var options = settings.Current.FieldBosses;
        var server = options.ServerId;
        lock (gate)
        {
            IEnumerable<FieldBossTimer> observations = [];
            DateTimeOffset? mapAt = null;
            if (server is { } id)
            {
                if (mapTimers.TryGetValue(id, out var own)) observations = own.Values;
                if (options.SharedTimers && feedState.ServerId == id) observations = observations.Concat(feedState.Timers);
                if (mapReadAt.TryGetValue(id, out var at)) mapAt = at;
            }
            var shown = feedState.ServerId == server ? feedState : feedState with { ServerId = server, Timers = [], ReadAt = null, Problem = null };
            return new FieldBossView(server, FieldBossBoard.Merge(observations, now), shown, mapAt);
        }
    }

    /// <summary>The overlay's field-boss notices at <paramref name="now"/>: none while they are off or no boss is chosen.</summary>
    public FieldBossAlertPlan Plan(DateTimeOffset now)
    {
        var options = settings.Current.FieldBosses;
        if (!options.Enabled || options.Bosses.Count == 0) return FieldBossAlertPlan.None;
        return FieldBossAlertPlanner.Plan(View(now).Board, options.Bosses.ToHashSet(), now, TimeSpan.FromMinutes(options.LeadMinutes));
    }

    /// <summary>
    /// The Field bosses window is open: the shared timers are read now (unless read within <see cref="RefreshInterval"/>) and
    /// kept current until the returned handle is disposed.
    /// </summary>
    public IDisposable Watch()
    {
        lock (gate)
        {
            if (disposed) return NoWatch.Instance;
            watchers++;
        }
        Reschedule();
        return new Watcher(this);
    }

    /// <summary>Reads the shared timers now, whatever the interval (the window's Refresh), when they are wanted.</summary>
    public void RefreshNow()
    {
        lock (gate)
        {
            if (disposed) return;
            feedState = feedState with { ReadAt = null };
        }
        Reschedule();
    }

    /// <summary>
    /// Takes one reading of the player's world map: what the engine's event brings (tests call it directly). It is put on the
    /// server of the character being played, or held while that is not sure (see the class).
    /// </summary>
    public void TakeMap(FieldBossMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var who = engine.Character;
        string? note = null;
        lock (gate)
        {
            if (disposed) return;
            if (who is { ServerId: > 0 } named)
            {
                Apply(named.ServerId, map);
            }
            else if (who is not null)
            {
                note = "Field bosses were read from the world map, but the server of the character being played is not known; not kept.";
            }
            else
            {
                held = (map, clock.GetUtcNow());
                note = "Field bosses were read from the world map while the character being played is not sure; held until the game names them.";
            }
        }
        if (note is not null) logger.LogInformation("{Note}", note);
        if (who is { ServerId: > 0 } character) NoteCharacterServer(character.ServerId);
        RaiseChanged();
    }

    /// <summary>
    /// Takes the game naming the character (with its counts): what the engine's event brings (tests call it directly). A map
    /// held since a moment the character was not sure is put on this character's server, when within <see cref="HoldFor"/>.
    /// </summary>
    public void TakeProgress(CharacterProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var server = progress.Character.ServerId;
        if (server <= 0) return;
        var applied = false;
        lock (gate)
        {
            if (disposed) return;
            if (held is { } waiting)
            {
                held = null;
                if (clock.GetUtcNow() - waiting.At <= HoldFor)
                {
                    Apply(server, waiting.Map);
                    applied = true;
                }
            }
        }
        if (applied) logger.LogInformation("Held field bosses kept for the character the game named.");
        NoteCharacterServer(server);
        if (applied) RaiseChanged();
    }

    /// <summary>
    /// Takes a move to a new connection: what the engine's event brings (tests call it directly). A held map reading is
    /// dropped: it was seen on the connection left, and a character named on another does not say whose server it was.
    /// </summary>
    public void TakeConnectionStarted()
    {
        bool dropped;
        lock (gate)
        {
            if (disposed) return;
            dropped = held is not null;
            held = null;
        }
        if (dropped) logger.LogInformation("Held field bosses dropped: the game moved to a new connection before naming the character.");
    }

    /// <summary>The player chose a server in the window; one that cannot be a server is ignored.</summary>
    public void ChooseServer(int serverId)
    {
        if (!FieldBossSettings.IsServerId(serverId)) return;
        settings.Update(s => s with { FieldBosses = s.FieldBosses with { ServerId = serverId } });
    }

    // Under the gate: a map's bosses as observations on a server, each replacing that boss's older one. Bosses the catalog does
    // not place on this map are left out.
    private void Apply(int serverId, FieldBossMap map)
    {
        if (!mapTimers.TryGetValue(serverId, out var bosses)) mapTimers[serverId] = bosses = [];
        foreach (var boss in map.Bosses)
        {
            if (FieldBossCatalog.Find(boss.Id) is not { } known || known.MapId != map.MapId) continue;
            if (bosses.TryGetValue(boss.Id, out var older) && older.ObservedAt > map.SeenAt) continue;
            bosses[boss.Id] = new FieldBossTimer(boss.Id, boss.Up, boss.Time, map.SeenAt, FieldBossSource.Map);
        }
        if (!mapReadAt.TryGetValue(serverId, out var last) || map.SeenAt > last) mapReadAt[serverId] = map.SeenAt;
    }

    // The game named a character on a server: when that is another server than the last named this run, it becomes the timers'
    // server (a server the player chose stays until then). The settings change is made off the capture thread, on the thread
    // Start ran on (the UI thread), as every other settings change is: the settings' subscribers (the hotkey's among them)
    // expect it there, and posts keep the order of quick switches (deep audit on #75: a thread-pool update could race one
    // made on the UI thread and deliver the older settings last). Every change of character is posted, with nothing compared
    // before its turn: B then A named while saves wait must end on A, and a check against the settings now (still A) would
    // have dropped A's save and let B's land last (Codex on #75). A save of the server already set changes nothing.
    private void NoteCharacterServer(int serverId)
    {
        SynchronizationContext? context;
        lock (gate)
        {
            if (disposed || serverId == characterServer || !FieldBossSettings.IsServerId(serverId)) return;
            characterServer = serverId;
            context = settingsContext;
        }
        if (context is not null) context.Post(_ => SaveServer(serverId), null);
        else _ = Task.Run(() => SaveServer(serverId));
    }

    private void SaveServer(int serverId)
    {
        try
        {
            lock (gate)
            {
                if (disposed) return;
            }
            settings.Update(s => s with { FieldBosses = s.FieldBosses with { ServerId = serverId } });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The field boss timers' server could not be saved.");
        }
    }

    // Starts a read of the shared timers when they are wanted and due, or sets the timer for the next one; cancels a read in
    // flight that is no longer wanted.
    private void Reschedule()
    {
        var options = settings.Current.FieldBosses;
        int server = 0, generation = 0;
        CancellationToken token = default;
        lock (gate)
        {
            if (disposed) return;
            timer?.Dispose();
            timer = null;
            var wanted = options.SharedTimers && options.ServerId is { } id && NotMeterFieldBossFeed.Region(id) is not null
                && (watchers > 0 || (options.Enabled && options.Bosses.Count > 0));
            if (!wanted)
            {
                if (fetching is not null)
                {
                    // The generation moves first, so a read that a cancellation completes at once is already stale.
                    fetchGeneration++;
                    fetching.Cancel();
                    fetching = null;
                    feedState = feedState with { Reading = false };
                }
                return;
            }
            server = options.ServerId!.Value;
            if (fetching is not null && feedState.ServerId == server) return; // a read for this server is under way
            var now = clock.GetUtcNow();
            if (feedState.ServerId == server && feedState.ReadAt is { } readAt && now - readAt < RefreshInterval && now >= readAt)
            {
                timer = clock.CreateTimer(_ => Reschedule(), null, readAt + RefreshInterval - now, Timeout.InfiniteTimeSpan);
                return;
            }
            generation = ++fetchGeneration; // first, as above
            fetching?.Cancel(); // not disposed: its read may still be linking to its token (a source with no timer holds nothing)
            fetching = new CancellationTokenSource();
            token = fetching.Token;
            var keep = feedState.ServerId == server ? feedState.Timers : [];
            feedState = new FieldBossFeedState(server, keep, feedState.ServerId == server ? feedState.ReadAt : null, feedState.ServerId == server ? feedState.Problem : null, true);
        }
        RaiseChanged();
        _ = FetchAsync(server, generation, token);
    }

    // One read of the shared timers; its result is taken only while it is still the current read. A failed read keeps the
    // timers the last good one gave, with the reason shown.
    private async Task FetchAsync(int server, int generation, CancellationToken token)
    {
        FieldBossFeedResult result;
        try
        {
            result = await feed.FetchAsync(server, token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result = FieldBossFeedResult.Failed("The shared timers could not be read.");
            logger.LogWarning(ex, "Reading the shared field boss timers failed.");
        }
        lock (gate)
        {
            if (disposed || generation != fetchGeneration) return;
            fetching = null;
            var timers = result.Problem is null ? result.Timers : feedState.Timers;
            feedState = new FieldBossFeedState(server, timers, clock.GetUtcNow(), result.Problem, false);
        }
        RaiseChanged();
        Reschedule();
    }

    private void OnMap(object? sender, FieldBossMap map) => TakeMap(map);

    private void OnProgress(object? sender, CharacterProgress progress) => TakeProgress(progress);

    private void OnConnectionStarted(object? sender, EventArgs e) => TakeConnectionStarted();

    private void OnSettingsChanged(object? sender, MeterSettings changed)
    {
        Reschedule();
        RaiseChanged();
    }

    // A failing subscriber is logged, never thrown into the capture thread, a timer or the settings caller.
    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "A field boss timers subscriber failed.");
        }
    }

    private void Unwatch()
    {
        lock (gate)
        {
            if (disposed || watchers == 0) return;
            watchers--;
        }
        Reschedule();
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            timer?.Dispose();
            timer = null;
            fetching?.Cancel();
            fetching = null;
        }
        if (started)
        {
            engine.FieldBossMapObserved -= OnMap;
            engine.ProgressObserved -= OnProgress;
            engine.ConnectionStarted -= OnConnectionStarted;
            settings.Changed -= OnSettingsChanged;
        }
    }

    // The handle Watch returns: disposing it once ends that watch.
    private sealed class Watcher(FieldBossService service) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) service.Unwatch();
        }
    }

    private sealed class NoWatch : IDisposable
    {
        public static NoWatch Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
