using Microsoft.Extensions.Logging;
using ScarletMeter.App.Settings;

namespace ScarletMeter.App.Updates;

/// <summary>
/// Keeps the meter's "a newer RATmeter release is out" notice current. While the check is switched on
/// (<see cref="UpdateSettings.CheckUpstreamReleases"/>), it asks <see cref="IUpstreamReleaseChecker"/> a little
/// after start-up, so launching the meter is not slowed, and again whenever the checker's cached answer
/// expires: <see cref="UpstreamReleaseChecker.CheckInterval"/> after an answer,
/// <see cref="UpstreamReleaseChecker.RetryInterval"/> after GitHub could not be reached. The checker never
/// throws and bounds each request; this class only schedules, remembers the last result and hides a release
/// the player dismissed (a later release shows again).
/// </summary>
public sealed class UpdateNotice : IDisposable
{
    /// <summary>How long after <see cref="Start"/> the first check runs.</summary>
    public static TimeSpan StartupDelay { get; } = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan ExpirySlack = TimeSpan.FromSeconds(5);

    private readonly IUpstreamReleaseChecker checker;
    private readonly SettingsService settings;
    private readonly TimeProvider clock;
    private readonly ILogger<UpdateNotice> logger;
    private readonly Lock gate = new();
    private readonly CancellationTokenSource stopping = new();
    private ITimer? timer;
    private UpstreamReleaseCheckResult? result;
    private DateTimeOffset? checkedAt;
    private UpdateSettings? lastUpdates;
    private bool started;
    private bool disposed;

    public UpdateNotice(IUpstreamReleaseChecker checker, SettingsService settings, TimeProvider clock, ILogger<UpdateNotice> logger)
    {
        this.checker = checker;
        this.settings = settings;
        this.clock = clock;
        this.logger = logger;
    }

    /// <summary>The last check's result while checking is on, or null before the first check or while it is off.</summary>
    public UpstreamReleaseCheckResult? Result
    {
        get
        {
            lock (gate) return settings.Current.Updates.CheckUpstreamReleases ? result : null;
        }
    }

    /// <summary>When <see cref="Result"/> arrived, or null.</summary>
    public DateTimeOffset? CheckedAt
    {
        get
        {
            lock (gate) return settings.Current.Updates.CheckUpstreamReleases ? checkedAt : null;
        }
    }

    /// <summary>The newer release to show in the overlay, or null: none, checking off, or this one dismissed.</summary>
    public UpstreamReleaseCheckResult.NewerAvailable? Visible =>
        Result is UpstreamReleaseCheckResult.NewerAvailable newer
        && !string.Equals(newer.Tag, settings.Current.Updates.DismissedRelease, StringComparison.OrdinalIgnoreCase)
            ? newer
            : null;

    /// <summary>Raised after a check completes or the switch or dismissal changes, on a thread-pool thread or the settings caller's.</summary>
    public event EventHandler? Changed;

    /// <summary>Schedules the first check and follows settings. Later calls, and calls after disposal, do nothing.</summary>
    public void Start()
    {
        lock (gate)
        {
            if (started || disposed) return;
            started = true;
            lastUpdates = settings.Current.Updates;
            settings.Changed += OnSettingsChanged;
            if (settings.Current.Updates.CheckUpstreamReleases) Schedule(StartupDelay);
        }
    }

    /// <summary>Hides the notice for this release; the next newer one shows again.</summary>
    public void Dismiss(UpstreamReleaseCheckResult.NewerAvailable release) =>
        settings.Update(s => s with { Updates = s.Updates with { DismissedRelease = release.Tag } });

    // Only a change to the Updates section matters. Switching checks on checks at once (outside the lock; the
    // checker answers from its cache when it can); switching off stops the timer. Either way the page re-renders.
    private void OnSettingsChanged(object? sender, MeterSettings changed)
    {
        var checkNow = false;
        lock (gate)
        {
            if (disposed || changed.Updates == lastUpdates) return;
            lastUpdates = changed.Updates;
            if (!changed.Updates.CheckUpstreamReleases)
            {
                timer?.Dispose();
                timer = null;
            }
            else
            {
                checkNow = timer is null;
            }
        }
        if (checkNow) _ = CheckAsync();
        Raise();
    }

    // Caller holds the lock.
    private void Schedule(TimeSpan due)
    {
        timer?.Dispose();
        timer = clock.CreateTimer(_ => _ = CheckAsync(), null, due, Timeout.InfiniteTimeSpan);
    }

    private async Task CheckAsync()
    {
        try
        {
            // A timer callback already queued when the switch went off or the meter closed must not send a
            // request: Dispose on a timer does not stop a callback that is on its way.
            CancellationToken token;
            lock (gate)
            {
                if (disposed || !settings.Current.Updates.CheckUpstreamReleases) return;
                token = stopping.Token;
            }
            var answer = await checker.CheckAsync(token).ConfigureAwait(false);
            lock (gate)
            {
                if (disposed) return;
                result = answer;
                checkedAt = clock.GetUtcNow();
                // The checker answers from its cache until then, so asking sooner would change nothing.
                var next = answer is UpstreamReleaseCheckResult.Unknown { Failure: UpstreamReleaseCheckFailure.Network or UpstreamReleaseCheckFailure.Timeout }
                    ? UpstreamReleaseChecker.RetryInterval
                    : UpstreamReleaseChecker.CheckInterval;
                if (settings.Current.Updates.CheckUpstreamReleases) Schedule(next + ExpirySlack);
                else timer = null;
            }
            Raise(); // the checker logs the result itself
        }
        catch (Exception ex)
        {
            // The checker promises not to throw; this keeps a broken promise off the timer thread.
            logger.LogError(ex, "The upstream release check failed unexpectedly; trying again later.");
            lock (gate)
            {
                if (!disposed && settings.Current.Updates.CheckUpstreamReleases) Schedule(UpstreamReleaseChecker.RetryInterval);
            }
        }
    }

    private void Raise()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An update notice subscriber failed.");
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            settings.Changed -= OnSettingsChanged;
            timer?.Dispose();
            timer = null;
        }
        // Cancelled, not disposed: a timer callback already queued may still read its token. It holds no
        // timer or handle, so leaving it to the collector costs nothing.
        stopping.Cancel();
    }
}
