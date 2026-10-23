using Microsoft.Extensions.Logging;
using ScarletMeter.App.Settings;
using ScarletMeter.WorldEvents;

namespace ScarletMeter.App.Alerts;

/// <summary>What the overlay shows about world events right now.</summary>
/// <param name="Alerts">Due notices, most urgent first (Soon before a start, Open while entry is possible).</param>
/// <param name="Next">The next alerted event when "show the next event" is on and nothing is due, else null.</param>
/// <param name="RefreshAt">When this view next changes, or null when no event is alerted at all.</param>
public sealed record AlertView(IReadOnlyList<EventAlert> Alerts, EventOccurrence? Next, DateTimeOffset? RefreshAt, TimeSpan Lead)
{
    public static AlertView None { get; } = new([], null, null, AlertPlanner.DefaultLeadTime);
}

/// <summary>
/// Keeps the overlay's world-event notices current. The plan comes from ScarletMeter.WorldEvents and says
/// when it next changes, so the monitor sleeps until then (at most a minute, to survive clock changes and
/// sleep) instead of polling; countdown text is the page's job. <see cref="EventApproaching"/> fires once
/// per event occurrence, when its Soon notice first appears; the window layer uses it to bring a minimized
/// meter back. A failure never escapes the timer thread: it is logged and the monitor tries again a minute later.
/// </summary>
public sealed class AlertMonitor : IDisposable
{
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan BoundarySlack = TimeSpan.FromMilliseconds(20);

    private readonly SettingsService settings;
    private readonly TimeProvider clock;
    private readonly ILogger<AlertMonitor> logger;
    private readonly Lock gate = new();

    // Occurrences already announced, kept until they start, so re-enabling an event or changing the lead
    // time does not announce the same occurrence twice. Holds at most one entry per enabled event.
    private readonly HashSet<(string Id, DateTimeOffset Start)> announced = [];
    private ITimer? timer;
    private AlertView view = AlertView.None;
    private bool started;
    private bool disposed;

    public AlertMonitor(SettingsService settings, TimeProvider clock, ILogger<AlertMonitor> logger)
    {
        this.settings = settings;
        this.clock = clock;
        this.logger = logger;
    }

    public AlertView Current
    {
        get { lock (gate) return view; }
    }

    /// <summary>
    /// Raised after each refresh (a settings change, a planned change, or the once-a-minute wake-up), whether or
    /// not <see cref="Current"/> differs, on a thread-pool thread or the settings caller's.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>Raised once per event occurrence, when its Soon notice first appears.</summary>
    public event EventHandler<EventAlert>? EventApproaching;

    /// <summary>Computes the first view and follows settings from now on. Later calls, and calls after disposal, do nothing.</summary>
    public void Start()
    {
        lock (gate)
        {
            if (started || disposed) return;
            started = true;
            settings.Changed += OnSettingsChanged;
        }
        Refresh();
    }

    /// <summary>The view at <paramref name="now"/> for the given settings. Pure; the catalog's Global preset is the schedule.</summary>
    public static AlertView Compute(AlertSettings alerts, DateTimeOffset now)
    {
        if (!alerts.Enabled || alerts.Events.Count == 0) return AlertView.None;
        var rules = EventCatalog.Default;
        var enabled = new HashSet<string>(alerts.Events, StringComparer.Ordinal);
        var offset = TimeSpan.FromMinutes(Math.Round(alerts.ServerOffsetHours * 60));
        var lead = TimeSpan.FromMinutes(alerts.LeadMinutes);
        var plan = AlertPlanner.Plan(rules, enabled, now, offset, lead);

        EventOccurrence? next = null;
        if (alerts.ShowNextEvent && plan.Alerts.Count == 0)
        {
            next = EventSchedule.Timeline(rules.Where(r => enabled.Contains(r.Id)).ToList(), now, offset)
                .Where(o => o.Phase == EventPhase.Upcoming)
                .OrderBy(o => o.Start)
                .Cast<EventOccurrence?>()
                .FirstOrDefault();
        }
        return new AlertView(plan.Alerts, next, plan.NextChange, lead);
    }

    private void OnSettingsChanged(object? sender, MeterSettings changed) => Refresh();

    // Recomputes the view, sets the timer for its next change and raises the events outside the lock.
    private void Refresh()
    {
        List<EventAlert> approaching = [];
        lock (gate)
        {
            if (disposed) return;
            var wait = MaxWait;
            try
            {
                var now = clock.GetUtcNow();
                var next = Compute(settings.Current.Alerts, now);
                announced.RemoveWhere(key => key.Start <= now);
                foreach (var alert in next.Alerts)
                    if (alert.Kind == AlertKind.Soon && announced.Add((alert.Rule.Id, alert.Start))) approaching.Add(alert);
                if (next.RefreshAt is { } at) wait = Clamp(at - now + BoundarySlack);
                view = next;
            }
            catch (Exception ex)
            {
                // The built-in schedule is valid; this keeps a future mistake from ending the meter or the notices.
                logger.LogError(ex, "World event alerts could not be computed; trying again in a minute.");
                approaching.Clear();
                view = AlertView.None;
            }
            timer?.Dispose();
            timer = clock.CreateTimer(_ => Refresh(), null, wait, Timeout.InfiniteTimeSpan);
        }

        Raise(() => Changed?.Invoke(this, EventArgs.Empty));
        foreach (var alert in approaching) Raise(() => EventApproaching?.Invoke(this, alert));
    }

    private static TimeSpan Clamp(TimeSpan wait) => wait < TimeSpan.Zero ? TimeSpan.Zero : wait > MaxWait ? MaxWait : wait;

    // A failing subscriber is logged, never thrown into the timer thread or the settings caller.
    private void Raise(Action raise)
    {
        try
        {
            raise();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "A world event alert subscriber failed.");
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
    }
}
