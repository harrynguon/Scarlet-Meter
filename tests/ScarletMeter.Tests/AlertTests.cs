using Microsoft.Extensions.Logging.Abstractions;
using ScarletMeter.App.Alerts;
using ScarletMeter.App.Settings;
using ScarletMeter.Engine;
using ScarletMeter.WorldEvents;

namespace ScarletMeter.Tests;

/// <summary>
/// The overlay's world-event notices over the Global preset at UTC+9 (server 10:00 is 01:00 UTC; Shugo is
/// hourly on the hour with a three-minute entry, the rift every three hours from server 02:00 with a
/// ten-minute portal), their settings, wording, and the monitor: its once-per-occurrence "event approaching"
/// signal, its wake-ups and its recovery from failures.
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

        var due = AlertMonitor.Compute(Only(EventIds.Shugo) with { ShowNextEvent = true }, At0056);
        Assert.Single(due.Alerts);
        Assert.Null(due.Next); // a due notice replaces the next-event line
    }

    [Fact]
    public void TheNextEventLineKeepsAnEventWhoseCurrentOccurrenceIsStillRunning()
    {
        // 01:04 UTC: Shugo's entry closed at 01:03 but the festival runs until 01:07, so no notice is due and
        // the line must already name the 02:00 start (Codex review on PR #6).
        var shugo = AlertMonitor.Compute(Only(EventIds.Shugo) with { ShowNextEvent = true }, new DateTimeOffset(2026, 10, 24, 1, 4, 0, TimeSpan.Zero));
        Assert.Empty(shugo.Alerts);
        var next = Assert.NotNull(shugo.Next);
        Assert.Equal(EventIds.Shugo, next.Rule.Id);
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 2, 0, 0, TimeSpan.Zero), next.Start);

        // 01:40 UTC: Invasion's entry closed at 01:33 and it runs until 01:43; next start 02:30.
        var invasion = AlertMonitor.Compute(Only(EventIds.Invasion) with { ShowNextEvent = true }, new DateTimeOffset(2026, 10, 24, 1, 40, 0, TimeSpan.Zero));
        Assert.Empty(invasion.Alerts);
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 2, 30, 0, TimeSpan.Zero), Assert.NotNull(invasion.Next).Start);
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
    public void HidingDuringBossFightsIsOnUnlessTurnedOff()
    {
        Assert.True(new AlertSettings().HideDuringBossFights);
        Assert.True(SettingsNormalizer.Normalize(new MeterSettings()).Settings.Alerts.HideDuringBossFights);
    }

    // When a boss fight is under way is BossFightHoldTests' subject; here, only that the setting decides.
    [Fact]
    public void TheStripStandsAsideOnlyWhileABossFightIsUnderWay()
    {
        Assert.True(AlertStripLayout.HiddenForFight(true, bossFightUnderWay: true));
        Assert.False(AlertStripLayout.HiddenForFight(true, bossFightUnderWay: false));
        Assert.False(AlertStripLayout.HiddenForFight(false, bossFightUnderWay: true)); // the setting off
    }

    // "Bring the meter back before an event" waits while the notices stand aside for a boss fight, and then brings
    // it back once, only if a notice is still due.
    [Fact]
    public void BringingTheMeterBackWaitsForTheBossFightToEnd()
    {
        var gate = new AlertRestoreGate();
        Assert.True(gate.Approaching());                  // no fight: at once
        Assert.False(gate.Hold(false, noticesDue: true)); // nothing waited

        Assert.False(gate.Hold(true, noticesDue: true));
        Assert.False(gate.Approaching());                 // mid-fight: it waits
        Assert.False(gate.Hold(true, noticesDue: true));  // still held
        Assert.True(gate.Hold(false, noticesDue: true));  // the fight ends: now
        Assert.False(gate.Hold(false, noticesDue: true)); // once

        Assert.False(gate.Hold(true, noticesDue: true));
        Assert.False(gate.Approaching());
        Assert.False(gate.Hold(false, noticesDue: false)); // the event passed during the fight: not at all
        Assert.True(gate.Approaching());
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
    public void TheStripShowsTwoNoticesUntilExpandedThenAll()
    {
        var alerts = FourDue();
        Assert.Equal(alerts.Take(2), AlertStripLayout.Shown(alerts, expanded: false));
        Assert.Equal(alerts, AlertStripLayout.Shown(alerts, expanded: true));
        Assert.Equal(2, AlertStripLayout.HiddenCount(alerts));
        Assert.Equal(1, AlertStripLayout.HiddenCount([.. alerts.Take(3)]));
        Assert.Equal(0, AlertStripLayout.HiddenCount([.. alerts.Take(2)]));
        Assert.Equal(0, AlertStripLayout.HiddenCount([]));
    }

    [Fact]
    public void AnExpandedStripCollapsesOnceEveryNoticeFits()
    {
        var alerts = FourDue();
        Assert.True(AlertStripLayout.StaysExpanded(true, alerts));
        Assert.True(AlertStripLayout.StaysExpanded(true, [.. alerts.Take(3)])); // one hidden: still worth expanding
        Assert.False(AlertStripLayout.StaysExpanded(false, alerts));
        Assert.False(AlertStripLayout.StaysExpanded(true, [.. alerts.Take(2)]));
        Assert.Equal("Show only the first 2", AlertStripLayout.CollapseHint);
    }

    [Fact]
    public void TheMoreTooltipListsEachHiddenNoticeWithItsClockTime()
    {
        var alerts = FourDue();
        string Line(EventAlert a, string verb) => $"{a.Rule.Name} · {a.Rule.Location} · {verb} {EventText.LocalTime(a.Deadline)}";
        Assert.Equal(Line(alerts[0], "closes"), AlertStripLayout.Describe(alerts[0]));
        Assert.Equal(Line(alerts[1], "starts"), AlertStripLayout.Describe(alerts[1]));
        Assert.Equal(Line(alerts[2], "starts") + "\n" + Line(alerts[3], "starts"), AlertStripLayout.HiddenSummary(alerts));
        Assert.Equal(string.Empty, AlertStripLayout.HiddenSummary([.. alerts.Take(2)]));
    }

    [Fact]
    public void TheSettingsListNamesEachPhaseInLocalTime()
    {
        var rules = EventCatalog.Default.ToDictionary(r => r.Id);
        var offset = EventCatalog.DefaultServerOffset;
        var utc = TimeZoneInfo.Utc;
        string Status(string id, DateTimeOffset now) => EventText.Status(EventSchedule.Occurrence(rules[id], now, offset), now, utc);

        var midnight = new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero); // a Saturday
        Assert.Equal("next 02:00", Status(EventIds.Rift, midnight));
        Assert.Equal("portal open now", Status(EventIds.Rift, midnight.AddHours(2).AddMinutes(5)));
        Assert.Equal("entry open now", Status(EventIds.Shugo, midnight.AddHours(1).AddMinutes(1)));
        Assert.Equal("running, entry closed", Status(EventIds.Shugo, midnight.AddHours(1).AddMinutes(5)));
        Assert.Equal("next Sun 12:00", Status(EventIds.Nahma, midnight)); // server Sun 21:00

        var boss = EventSchedule.Occurrence(rules[EventIds.Nahma], midnight, offset) with { Phase = EventPhase.ScheduledActive };
        Assert.Equal("started 12:00", EventText.Status(boss, midnight, utc));

        var auckland = TimeZoneInfo.CreateCustomTimeZone("UTC+13", TimeSpan.FromHours(13), "UTC+13", "UTC+13");
        Assert.Equal("next 15:00", EventText.Status(EventSchedule.Occurrence(rules[EventIds.Rift], midnight, offset), midnight, auckland));

        // Fri 23:30 UTC is Sat 12:30 at UTC+13 and the 02:00 UTC rift is Sat 15:00 there: the same local day,
        // though not the same UTC day, so no weekday.
        var lateFriday = new DateTimeOffset(2026, 10, 23, 23, 30, 0, TimeSpan.Zero);
        Assert.Equal("next 15:00", EventText.Status(EventSchedule.Occurrence(rules[EventIds.Rift], lateFriday, offset), lateFriday, auckland));
        Assert.Equal("next Sat 02:00", Status(EventIds.Rift, lateFriday));
    }

    [Fact]
    public void TheMonitorAnnouncesEachOccurrenceOnceAndWakesAtTheNextChange()
    {
        var clock = new FakeClock(At0056.AddMinutes(-1)); // 00:55, Shugo's notice opens now
        using var settings = NewSettings(Only(EventIds.Shugo));
        using var monitor = new AlertMonitor(settings, clock, NullLogger<AlertMonitor>.Instance);
        var approaching = new List<EventAlert>();
        monitor.EventApproaching += (_, alert) => approaching.Add(alert);

        monitor.Start();
        Assert.Equal(AlertKind.Soon, Assert.Single(approaching).Kind);
        Assert.Equal(TimeSpan.FromMinutes(1), clock.Due); // capped at a minute, though the next change is the 01:00 start

        clock.Fire(TimeSpan.FromMinutes(1));          // 00:56: same notice, no new signal
        Assert.Single(approaching);
        clock.Fire(TimeSpan.FromSeconds(210));        // 00:59:30: wake just after the 01:00 start
        Assert.Equal(TimeSpan.FromSeconds(30) + TimeSpan.FromMilliseconds(20), clock.Due);
        clock.Fire(clock.Due);                        // the entry opens: shown, but not announced again
        Assert.Equal(AlertKind.Open, Assert.Single(monitor.Current.Alerts).Kind);
        Assert.Single(approaching);

        clock.Fire(TimeSpan.FromMinutes(55));         // 01:55: the next occurrence is announced
        Assert.Equal(2, approaching.Count);
        Assert.Equal(approaching[0].Start.AddHours(1), approaching[1].Start);

        // Turning the event off and on, or changing the lead time, does not announce the same occurrence again.
        settings.Update(s => s with { Alerts = s.Alerts with { Events = [] } });
        Assert.Empty(monitor.Current.Alerts);
        settings.Update(s => s with { Alerts = s.Alerts with { Events = [EventIds.Shugo], LeadMinutes = 10 } });
        Assert.Single(monitor.Current.Alerts);
        Assert.Equal(2, approaching.Count);

        settings.Update(s => s with { Alerts = s.Alerts with { Enabled = false } });
        Assert.Empty(monitor.Current.Alerts);
    }

    [Fact]
    public void AFailingSubscriberOrComputationNeverEscapesTheTimer()
    {
        var clock = new FakeClock(At0056.AddMinutes(-1));
        using var settings = NewSettings(Only(EventIds.Shugo));
        using var monitor = new AlertMonitor(settings, clock, NullLogger<AlertMonitor>.Instance);
        var approaching = 0;
        monitor.Changed += (_, _) => throw new InvalidOperationException("subscriber");
        monitor.EventApproaching += (_, _) => approaching++;

        monitor.Start();                              // Changed throws; the signal is still raised
        Assert.Equal(1, approaching);
        Assert.Single(monitor.Current.Alerts);

        clock.FailNextRead = true;                    // the computation fails: no notices, retry in a minute
        clock.Fire(TimeSpan.FromMinutes(1));
        Assert.Empty(monitor.Current.Alerts);
        Assert.Equal(TimeSpan.FromMinutes(1), clock.Due);

        clock.Fire(TimeSpan.FromMinutes(1));          // 00:57: back, and the occurrence is not announced twice
        Assert.Single(monitor.Current.Alerts);
        Assert.Equal(1, approaching);
    }

    [Fact]
    public void StartingTwiceOrAfterDisposalDoesNothing()
    {
        var clock = new FakeClock(At0056);
        using var settings = NewSettings(Only(EventIds.Shugo));
        var monitor = new AlertMonitor(settings, clock, NullLogger<AlertMonitor>.Instance);
        var changes = 0;
        monitor.Changed += (_, _) => changes++;

        monitor.Start();
        monitor.Start();
        changes = 0;
        settings.Update(s => s with { Alerts = s.Alerts with { LeadMinutes = 7 } });
        Assert.Equal(1, changes); // subscribed to settings once

        monitor.Dispose();
        var timers = clock.TimersCreated;
        monitor.Start();
        settings.Update(s => s with { Alerts = s.Alerts with { LeadMinutes = 8 } });
        Assert.Equal(1, changes);
        Assert.Equal(timers, clock.TimersCreated);
    }

    private SettingsService NewSettings(AlertSettings alerts)
    {
        var store = new SettingsStore(directory.FullPath, NullLogger<SettingsStore>.Instance);
        var settings = new SettingsService(store, new EngineSettings(), NullLogger<SettingsService>.Instance);
        settings.Update(s => s with { Alerts = alerts });
        return settings;
    }

    public void Dispose() => directory.Dispose();

    // Four due notices at 01:01, most urgent first: Shugo's entry open (01:00 to 01:03), then the rift, Nahma and
    // Kaira about to start. Built directly, so the strip's rules are tested apart from the schedule.
    private static IReadOnlyList<EventAlert> FourDue()
    {
        var rules = EventCatalog.Default.ToDictionary(r => r.Id);
        var hour = new DateTimeOffset(2026, 10, 24, 1, 0, 0, TimeSpan.Zero);
        var now = hour.AddMinutes(1);
        EventAlert Soon(string id, int minute) =>
            new(rules[id], AlertKind.Soon, hour.AddMinutes(minute), hour.AddMinutes(minute), hour.AddMinutes(minute) - now);
        return
        [
            new EventAlert(rules[EventIds.Shugo], AlertKind.Open, hour, hour.AddMinutes(3), hour.AddMinutes(3) - now),
            Soon(EventIds.Rift, 4),
            Soon(EventIds.Nahma, 20),
            Soon(EventIds.Kaira, 35),
        ];
    }

    // A clock whose single timer fires only when the test says so, and whose next read can be made to fail.
    private sealed class FakeClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;
        private TimerCallback? callback;

        public TimeSpan Due { get; private set; }

        public int TimersCreated { get; private set; }

        public bool FailNextRead { get; set; }

        public override DateTimeOffset GetUtcNow()
        {
            if (!FailNextRead) return now;
            FailNextRead = false;
            throw new InvalidOperationException("clock");
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            this.callback = callback;
            Due = dueTime;
            TimersCreated++;
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
