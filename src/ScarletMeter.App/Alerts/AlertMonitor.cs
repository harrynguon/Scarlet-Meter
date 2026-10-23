using Microsoft.Extensions.Logging;
using ScarletMeter.App.Settings;
using ScarletMeter.WorldEvents;

namespace ScarletMeter.App.Alerts;

/// <summary>What the overlay shows about world events right now.</summary>
/// <param name="Alerts">Due notices, most urgent first (Soon before a start, Open while entry is possible).</param>
/// <param name="Next">The next alerted event when "show the next event" is on and nothing is due, else null.</param>
/// <param name="RefreshAt">When this view next changes, or null when nothing is alerted.</param>
public sealed record AlertView(IReadOnlyList<EventAlert> Alerts, EventOccurrence? Next, DateTimeOffset? RefreshAt, TimeSpan Lead)
{
    public static AlertView None { get; } = new([], null, null, AlertPlanner.DefaultLeadTime);
}

/// <summary>
/// Keeps the overlay's world-event notices current. The plan comes from ScarletMeter.WorldEvents and says
/// when it next changes, so the monitor sleeps until then (at most a minute, to survive clock changes and
/// sleep) instead of polling; countdown text is the page's job. <see cref="AlertAppeared"/> fires once per
/// notice, which the window layer uses to bring a minimized meter back.
/// </summary>
public sealed class AlertMonitor : IDisposable
{
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan BoundarySlack = TimeSpan.FromMilliseconds(20);

    private readonly SettingsService settings;
    private readonly TimeProvider clock;
    private readonly ILogger<AlertMonitor> logger;
    private readonly Lock gate = new();
    private readonly HashSet<(string Id, AlertKind Kind, DateTimeOffset Start)> shown = [];
    private ITimer? timer;
    private AlertView view = AlertView.None;
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

    /// <summary>Raised after <see cref="Current"/> changes, on a thread-pool thread or the settings caller's.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised once for each notice when it first appears.</summary>
    public event EventHandler<EventAlert>? AlertAppeared;

    /// <summary>Computes the first view and follows settings from now on.</summary>
    public void Start()
    {
        settings.Changed += OnSettingsChanged;
        Recompute();
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

    private void OnSettingsChanged(object? sender, MeterSettings changed) => Recompute();

    private void Recompute()
    {
        List<EventAlert> appeared = [];
        lock (gate)
        {
            if (disposed) return;
            var now = clock.GetUtcNow();
            AlertView next;
            try
            {
                next = Compute(settings.Current.Alerts, now);
            }
            catch (ArgumentException ex)
            {
                // The built-in schedule is valid; this guards the overlay against a future catalog mistake.
                logger.LogError(ex, "World event alerts could not be computed.");
                next = AlertView.None;
            }

            foreach (var alert in next.Alerts)
                if (shown.Add((alert.Rule.Id, alert.Kind, alert.Start))) appeared.Add(alert);
            shown.RemoveWhere(key => !next.Alerts.Any(a => a.Rule.Id == key.Id && a.Kind == key.Kind && a.Start == key.Start));
            view = next;

            var wait = next.RefreshAt is { } at ? at - now + BoundarySlack : MaxWait;
            wait = wait < TimeSpan.Zero ? TimeSpan.Zero : wait > MaxWait ? MaxWait : wait;
            timer?.Dispose();
            timer = clock.CreateTimer(_ => Recompute(), null, wait, Timeout.InfiniteTimeSpan);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        foreach (var alert in appeared) AlertAppeared?.Invoke(this, alert);
    }

    public void Dispose()
    {
        settings.Changed -= OnSettingsChanged;
        lock (gate)
        {
            disposed = true;
            timer?.Dispose();
            timer = null;
        }
    }
}
