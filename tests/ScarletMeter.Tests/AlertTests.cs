using Microsoft.Extensions.Logging.Abstractions;
using ScarletMeter.App.Alerts;
using ScarletMeter.App.Settings;
using ScarletMeter.Engine;
using ScarletMeter.WorldEvents;

namespace ScarletMeter.Tests;

/// <summary>
/// The overlay's world-event notices over the Global preset at UTC+9 (server 10:00 is 01:00 UTC; Shugo is
/// hourly on the hour with a three-minute entry, the rift every three hours from server 02:00 with a
/// ten-minute portal), their settings, wording, and the monitor's "a notice appeared" signal.
/// </summary>
public sealed class AlertTests : IDisposable
{
    private static readonly DateTimeOffset At0056 = new(2026, 10, 24, 0, 56, 0, TimeSpan.Zero);

    private readonly TestDataDirectory directory = new();

    private static AlertSettings Only(params string[] events) => new() { Events = events };

    [Fact]
    public void ASoonNoticeAppearsWithinTheLeadTime()
    {
        var view = AlertMonitor.Compute(Only(EventIds.Shugo), At0056);
        var alert = Assert.Single(view.Alerts);
        Assert.Equal(AlertKind.Soon, alert.Kind);
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 1, 0, 0, TimeSpan.Zero), alert.Deadline);
        Assert.Equal(TimeSpan.FromMinutes(4), alert.Remaining);
        Assert.Equal(TimeSpan.FromMinutes(5), view.Lead);
        Assert.NotNull(view.RefreshAt);
    }

    [Fact]
    public void AnOpenNoticeLastsUntilTheEntryCloses()
    {
        var view = AlertMonitor.Compute(Only(EventIds.Shugo), At0056.AddMinutes(5)); // 01:01
        var alert = Assert.Single(view.Alerts);
        Assert.Equal(AlertKind.Open, alert.Kind);
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 1, 3, 0, TimeSpan.Zero), alert.Deadline);
        Assert.Empty(AlertMonitor.Compute(Only(EventIds.Shugo), At0056.AddMinutes(7)).Alerts); // 01:03
    }

    [Fact]
    public void LeadTimeComesFromSettings()
    {
        Assert.Empty(AlertMonitor.Compute(Only(EventIds.Shugo), At0056.AddMinutes(-6)).Alerts); // 00:50, lead 5
        var tenMinutes = Only(EventIds.Shugo) with { LeadMinutes = 10 };
        Assert.Single(AlertMonitor.Compute(tenMinutes, At0056.AddMinutes(-6)).Alerts);
    }

    [Fact]
    public void NothingShowsWhenAlertsAreOffOrNoEventIsChosen()
    {
        Assert.Same(AlertView.None, AlertMonitor.Compute(Only(EventIds.Shugo) with { Enabled = false }, At0056));
        Assert.Same(AlertView.None, AlertMonitor.Compute(Only(), At0056));
    }

    [Fact]
    public void TheNextEventShowsOnlyWhenAskedAndNothingIsDue()
    {
        var riftOnly = Only(EventIds.Rift) with { ShowNextEvent = true };
        var view = AlertMonitor.Compute(riftOnly, new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero));
        Assert.Empty(view.Alerts);
        var next = Assert.NotNull(view.Next);
        Assert.Equal(EventIds.Rift, next.Rule.Id);
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 2, 0, 0, TimeSpan.Zero), next.Start); // server 11:00

        Assert.Null(AlertMonitor.Compute(riftOnly with { ShowNextEvent = false }, new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero)).Next);
    }

    [Fact]
    public void TheServerOffsetMovesServerTimedEvents()
    {
        // At UTC+8 the rift's server 02:00 start is 18:00 UTC the day before.
        var view = AlertMonitor.Compute(Only(EventIds.Rift) with { ServerOffsetHours = 8 }, new DateTimeOffset(2026, 10, 24, 17, 56, 0, TimeSpan.Zero));
        Assert.Equal(AlertKind.Soon, Assert.Single(view.Alerts).Kind);
    }

    [Fact]
    public void AlertSettingsAreNormalized()
    {
        var input = new MeterSettings
        {
            Alerts = new AlertSettings
            {
                LeadMinutes = 0,
                ServerOffsetHours = 9.004,
                Events = ["shugo", "nonsense", "rift", "shugo"],
            },
        };
        var (settings, repairs) = SettingsNormalizer.Normalize(input);
        Assert.Equal(AlertSettings.MinLeadMinutes, settings.Alerts.LeadMinutes);
        Assert.Equal(9.0, settings.Alerts.ServerOffsetHours); // whole minutes, as the schedule requires
        Assert.Equal(new[] { "rift", "shugo" }, settings.Alerts.Events); // known ids, once, in catalog order
        Assert.Contains(repairs, r => r.Contains("lead time"));
        Assert.Contains(repairs, r => r.Contains("Unknown world events"));

        var offsets = SettingsNormalizer.Normalize(new MeterSettings { Alerts = new AlertSettings { ServerOffsetHours = 99 } }).Settings;
        Assert.Equal(AlertSettings.MaxServerOffsetHours, offsets.Alerts.ServerOffsetHours);
    }

    [Fact]
    public void AlertSettingsCompareByValue()
    {
        Assert.Equal(new AlertSettings { Events = ["rift"] }, new AlertSettings { Events = ["rift"] });
        Assert.NotEqual(new AlertSettings { Events = ["rift"] }, new AlertSettings { Events = ["shugo"] });
        Assert.Equal(new AlertSettings(), SettingsNormalizer.Normalize(null).Settings.Alerts);
    }

    [Fact]
    public void WordingForNoticesAndSchedules()
    {
        Assert.Equal("4:59", EventText.Countdown(new TimeSpan(0, 4, 59)));
        Assert.Equal("1 h 05 m", EventText.Countdown(new TimeSpan(1, 5, 30)));
        Assert.Equal("0:00", EventText.Countdown(TimeSpan.FromSeconds(-3)));

        var rules = EventCatalog.Default.ToDictionary(r => r.Id);
        Assert.Equal("Portal open", EventText.OpenLabel(rules[EventIds.Rift]));
        Assert.Equal("Entry open", EventText.OpenLabel(rules[EventIds.Shugo]));
        Assert.Equal("Every 3 h from 02:00 server time · portal 10 min", EventText.Schedule(rules[EventIds.Rift]));
        Assert.Equal("Every hour at :00 server time · entry 3 min", EventText.Schedule(rules[EventIds.Shugo]));
        Assert.Equal("Sun, Fri 21:00 server time", EventText.Schedule(rules[EventIds.Nahma]));
        Assert.Equal("07:00 UTC", EventText.Schedule(rules[EventIds.Daily]));
    }

    [Fact]
    public void TheMonitorSignalsEachNoticeOnceAndWakesAtTheNextChange()
    {
        var clock = new FakeClock(At0056.AddMinutes(-1)); // 00:55, Shugo's notice opens now
        var store = new SettingsStore(directory.FullPath, NullLogger<SettingsStore>.Instance);
        using var settings = new SettingsService(store, new EngineSettings(), NullLogger<SettingsService>.Instance);
        settings.Update(s => s with { Alerts = Only(EventIds.Shugo) });
        using var monitor = new AlertMonitor(settings, clock, NullLogger<AlertMonitor>.Instance);
        var appeared = new List<EventAlert>();
        monitor.AlertAppeared += (_, alert) => appeared.Add(alert);

        monitor.Start();
        Assert.Equal(AlertKind.Soon, Assert.Single(appeared).Kind);
        Assert.Equal(TimeSpan.FromMinutes(1), clock.Due); // capped at a minute, though the next change is the 01:00 start

        clock.Fire(TimeSpan.FromMinutes(1));          // 00:56: same notice, no new signal
        Assert.Single(appeared);
        clock.Fire(TimeSpan.FromMinutes(4));          // 01:00: the entry opens
        Assert.Equal(2, appeared.Count);
        Assert.Equal(AlertKind.Open, appeared[1].Kind);
        Assert.Equal(AlertKind.Open, Assert.Single(monitor.Current.Alerts).Kind);

        settings.Update(s => s with { Alerts = s.Alerts with { Enabled = false } });
        Assert.Empty(monitor.Current.Alerts);
    }

    public void Dispose() => directory.Dispose();

    // A clock whose single timer fires only when the test says so.
    private sealed class FakeClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;
        private TimerCallback? callback;

        public TimeSpan Due { get; private set; }

        public override DateTimeOffset GetUtcNow() => now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            this.callback = callback;
            Due = dueTime;
            return new NoTimer();
        }

        public void Fire(TimeSpan advance)
        {
            now += advance;
            callback?.Invoke(null);
        }

        private sealed class NoTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
