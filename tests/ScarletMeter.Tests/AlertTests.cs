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
                Events = ["rift", "nonsense", "shugo", "rift"],
            },
        };
        var (settings, repairs) = SettingsNormalizer.Normalize(input);
        Assert.Equal(AlertSettings.MinLeadMinutes, settings.Alerts.LeadMinutes);
        Assert.Equal(9.0, settings.Alerts.ServerOffsetHours); // whole minutes, as the schedule requires
        Assert.Equal(new[] { "shugo", "rift" }, settings.Alerts.Events); // known ids, once, in catalog order
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
        // The defaults are already in catalog order, so normalizing them changes nothing.
        Assert.Equal(EventCatalog.Ids.Where(AlertSettings.DefaultEvents.Contains), AlertSettings.DefaultEvents);
    }

    [Fact]
    public void HidingDuringBossFightsIsOnUnlessTurnedOff()
    {
        Assert.True(new AlertSettings().HideDuringBossFights);
        Assert.True(SettingsNormalizer.Normalize(new MeterSettings()).Settings.Alerts.HideDuringBossFights);
    }

    // When a boss fight is under way is BossFightHoldTests' subject; here, only that the setting decides.
    [Fact]
    public void TheRowStandsAsideOnlyWhileABossFightIsUnderWay()
    {
        Assert.True(EventRowLayout.HiddenForFight(true, bossFightUnderWay: true));
        Assert.False(EventRowLayout.HiddenForFight(true, bossFightUnderWay: false));
        Assert.False(EventRowLayout.HiddenForFight(false, bossFightUnderWay: true)); // the setting off
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
        Assert.False(gate.Hold(false, noticesDue: true)); // a fight after the restore that waited: not again

        Assert.False(gate.Hold(true, noticesDue: true));
        Assert.False(gate.Approaching());
        Assert.False(gate.Hold(false, noticesDue: false)); // the event passed during the fight: not at all
        Assert.True(gate.Approaching());

        // A fight with nothing waiting brings nothing back, though a notice is due (the player hid the meter again
        // after its restore).
        var quiet = new AlertRestoreGate();
        Assert.False(quiet.Hold(true, noticesDue: true));
        Assert.False(quiet.Hold(false, noticesDue: true));
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

    // One icon per alerted event in catalog order, the order Harry chose for the row (2026-10-26): the festivals and the
    // rift, the siege, the bosses, then the resets.
    [Fact]
    public void TheRowShowsEveryAlertedEventInCatalogOrder()
    {
        var view = AlertMonitor.Compute(new AlertSettings(), At0056);
        var icons = EventRowLayout.Icons(view);
        Assert.Equal(new[] { EventIds.Shugo, EventIds.Invasion, EventIds.Rift, EventIds.Siege, EventIds.Kaira, EventIds.Nahma, EventIds.Executors },
            icons.Select(i => i.Rule.Id));
        // 00:56 UTC: Shugo and Kaira (server 10:00) start at 01:00, within the five-minute lead; the rest are quiet.
        var hour = new DateTimeOffset(2026, 10, 24, 1, 0, 0, TimeSpan.Zero);
        Assert.All(icons, icon => Assert.Equal(
            icon.Rule.Id is EventIds.Shugo or EventIds.Kaira ? EventIconState.Soon : EventIconState.Quiet, icon.State));
        Assert.All(icons.Where(i => i.State == EventIconState.Soon), i => Assert.Equal(hour, i.Until));
        Assert.All(icons.Where(i => i.State == EventIconState.Quiet), i => Assert.Null(i.Until));
        Assert.Equal(view.Alerts.Count, icons.Count(i => i.Alert is not null));

        var everything = AlertMonitor.Compute(new AlertSettings { Events = [.. EventCatalog.Ids] }, At0056);
        Assert.Equal(EventCatalog.Ids, EventRowLayout.Icons(everything).Select(i => i.Rule.Id));
        Assert.Empty(EventRowLayout.Icons(AlertView.None));
        Assert.Empty(EventRowLayout.Icons(AlertMonitor.Compute(new AlertSettings { Enabled = false }, At0056)));
    }

    [Fact]
    public void AnOpenEntryLightsUpUntilItCloses()
    {
        // 01:01 UTC: Shugo's entry is open until 01:03; the rift (02:00) is quiet.
        var icons = EventRowLayout.Icons(AlertMonitor.Compute(Only(EventIds.Shugo, EventIds.Rift), At0056.AddMinutes(5)));
        Assert.Equal((EventIds.Shugo, EventIconState.Open), (icons[0].Rule.Id, icons[0].State));
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 1, 3, 0, TimeSpan.Zero), icons[0].Until);
        Assert.Equal(AlertKind.Open, Assert.NotNull(icons[0].Alert).Kind);
        Assert.Equal((EventIds.Rift, EventIconState.Quiet), (icons[1].Rule.Id, icons[1].State));
    }

    // "Show the next event": with nothing due, the next event's icon counts down to its start, dimly; never while
    // something is due.
    [Fact]
    public void WithNothingDueTheNextEventsIconShowsItsTime()
    {
        var midnight = new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
        var asked = Only(EventIds.Rift, EventIds.Siege) with { ShowNextEvent = true };
        var icons = EventRowLayout.Icons(AlertMonitor.Compute(asked, midnight));
        Assert.Equal((EventIconState.Next, (DateTimeOffset?)new DateTimeOffset(2026, 10, 24, 2, 0, 0, TimeSpan.Zero)), (icons[0].State, icons[0].Until));
        Assert.Equal((EventIconState.Quiet, (DateTimeOffset?)null), (icons[1].State, icons[1].Until)); // the siege, at 12:00
        Assert.All(EventRowLayout.Icons(AlertMonitor.Compute(asked with { ShowNextEvent = false }, midnight)),
            icon => Assert.Equal(EventIconState.Quiet, icon.State));
        // Shugo due at 00:56: no icon shows the next event.
        Assert.DoesNotContain(EventRowLayout.Icons(AlertMonitor.Compute(Only(EventIds.Shugo, EventIds.Rift) with { ShowNextEvent = true }, At0056)),
            icon => icon.State == EventIconState.Next);
    }

    // Minutes and seconds under an hour, as the game's own timers read (Harry: "3m" with 2:01 left misled, since whole
    // minutes were rounded up); hours and minutes, then days and hours, for a next event further off.
    [Fact]
    public void AnIconsTimeIsMinutesAndSeconds()
    {
        Assert.Equal("2:19", EventText.IconTime(new TimeSpan(0, 2, 19)));
        Assert.Equal("2:01", EventText.IconTime(TimeSpan.FromSeconds(121.9))); // a part second is dropped
        Assert.Equal("54:53", EventText.IconTime(new TimeSpan(0, 54, 53)));
        Assert.Equal("0:01", EventText.IconTime(TimeSpan.FromSeconds(1)));
        Assert.Equal("0:00", EventText.IconTime(TimeSpan.Zero));
        Assert.Equal("0:00", EventText.IconTime(TimeSpan.FromSeconds(-3)));
        Assert.Equal("59:59", EventText.IconTime(new TimeSpan(0, 59, 59)));
        Assert.Equal("1h 00m", EventText.IconTime(TimeSpan.FromHours(1)));
        Assert.Equal("2h 04m", EventText.IconTime(new TimeSpan(2, 4, 50)));
        Assert.Equal("23h 59m", EventText.IconTime(new TimeSpan(23, 59, 59)));
        Assert.Equal("1d 00h", EventText.IconTime(TimeSpan.FromHours(24)));
        Assert.Equal("6d 05h", EventText.IconTime(new TimeSpan(6, 5, 10, 0)));
    }

    // The tip says what the event is doing in clock times; the icon itself shows the countdown.
    [Fact]
    public void AnIconsTipSaysWhatItsEventIsDoing()
    {
        string StatusAt(DateTimeOffset now, string id)
        {
            var icon = Assert.Single(EventRowLayout.Icons(AlertMonitor.Compute(Only(id), now)));
            return EventRowLayout.Status(icon, now, EventCatalog.DefaultServerOffset, TimeZoneInfo.Utc);
        }

        Assert.Equal("starts 01:00", StatusAt(At0056, EventIds.Shugo));
        Assert.Equal("entry open until 01:03", StatusAt(At0056.AddMinutes(5), EventIds.Shugo));
        Assert.Equal("portal open until 02:10", StatusAt(new DateTimeOffset(2026, 10, 24, 2, 5, 0, TimeSpan.Zero), EventIds.Rift));
        // Not due: the settings list's words, at the instant asked about.
        Assert.Equal("next 01:30", StatusAt(At0056, EventIds.Invasion));
        Assert.Equal("running, entry closed", StatusAt(At0056.AddMinutes(8), EventIds.Shugo)); // 01:04
        Assert.Equal("next 12:00", StatusAt(At0056, EventIds.Siege));                         // server Sat 21:00
        Assert.Equal("next Sun 12:00", StatusAt(At0056, EventIds.Nahma));                     // server Sun 21:00
    }

    // The row reads its tips and lines with the view's server offset, which must be the player's setting, not the
    // preset's: at UTC+8 the siege's server Saturday 21:00 is 13:00 UTC, an hour after the preset's.
    [Fact]
    public void TheRowsTimesFollowThePlayersServerOffset()
    {
        var view = AlertMonitor.Compute(Only(EventIds.Siege) with { ServerOffsetHours = 8 }, At0056);
        Assert.Equal(TimeSpan.FromHours(8), view.ServerOffset);
        var siege = Assert.Single(EventRowLayout.Icons(view));
        Assert.Equal("next 13:00", EventRowLayout.Status(siege, At0056, view.ServerOffset, TimeZoneInfo.Utc));
    }

    [Fact]
    public void AnIconsDetailLineCountsDown()
    {
        var offset = EventCatalog.DefaultServerOffset;
        var lead = TimeSpan.FromMinutes(5);
        EventIcon IconAt(DateTimeOffset now, string id) => Assert.Single(EventRowLayout.Icons(AlertMonitor.Compute(Only(id), now)));

        var soon = IconAt(At0056, EventIds.Shugo);
        Assert.Equal("in 4:00", EventRowLayout.When(soon, At0056, offset, TimeZoneInfo.Utc));
        Assert.Equal(20, Assert.NotNull(EventRowLayout.Progress(soon, At0056, lead)), 6); // a fifth of the lead time gone
        Assert.Equal(100, Assert.NotNull(EventRowLayout.Progress(soon, At0056.AddMinutes(6), lead))); // never past full

        var at0101 = At0056.AddMinutes(5);
        var open = IconAt(at0101, EventIds.Shugo);
        Assert.Equal("Entry open · 2:00 left", EventRowLayout.When(open, at0101, offset, TimeZoneInfo.Utc));
        Assert.Equal(200.0 / 3, Assert.NotNull(EventRowLayout.Progress(open, at0101, lead)), 6); // two thirds of the entry left
        Assert.Equal(0, Assert.NotNull(EventRowLayout.Progress(open, at0101.AddMinutes(3), lead))); // never below empty

        var idle = IconAt(At0056, EventIds.Invasion);
        Assert.Equal("next 01:30 · in 34:00", EventRowLayout.When(idle, At0056, offset, TimeZoneInfo.Utc));
        Assert.Null(EventRowLayout.Progress(idle, At0056, lead));
        var at0104 = At0056.AddMinutes(8); // Shugo's entry closed at 01:03; it runs until 01:07
        Assert.Equal("running, entry closed", EventRowLayout.When(IconAt(at0104, EventIds.Shugo), at0104, offset, TimeZoneInfo.Utc));
    }

    // Clicking an icon opens its detail line beside any already open (Harry asked for several at once); clicking it
    // again closes only its own.
    [Fact]
    public void AClickOpensAnEventsLineBesideTheOthersAndASecondClosesIt()
    {
        IReadOnlySet<string> none = new HashSet<string>();
        var shugo = EventRowLayout.Toggle(none, EventIds.Shugo);
        Assert.Equal(new[] { EventIds.Shugo }, shugo);
        Assert.Empty(none); // the set passed in is left as it was
        var both = EventRowLayout.Toggle(shugo, EventIds.Rift);
        Assert.Equal(new[] { EventIds.Rift, EventIds.Shugo }, both.Order(StringComparer.Ordinal));
        Assert.Equal(new[] { EventIds.Rift }, EventRowLayout.Toggle(both, EventIds.Shugo));
        Assert.Empty(EventRowLayout.Toggle(shugo, EventIds.Shugo));

        // In the row's order whatever the clicks' order; an event that left the row (switched off) has no line.
        var icons = EventRowLayout.Icons(AlertMonitor.Compute(Only(EventIds.Shugo, EventIds.Rift), At0056));
        var clicked = EventRowLayout.Toggle(EventRowLayout.Toggle(EventRowLayout.Toggle(none, EventIds.Rift), EventIds.Weekly), EventIds.Shugo);
        Assert.Equal(new[] { EventIds.Shugo, EventIds.Rift }, EventRowLayout.Opened(icons, clicked).Select(icon => icon.Rule.Id));
        Assert.Empty(EventRowLayout.Opened(icons, none));
        Assert.Equal(2, EventRowLayout.AnnouncedCount);
    }

    // Every event has a drawing of its own (Harry: the siege's castle read as the weekly calendar, Shugo's cat as a
    // monster, and the rift is the game's vortex), and each is path data only, since the markup binds it as an attribute.
    [Fact]
    public void EveryEventHasItsOwnDrawing()
    {
        Assert.All(EventCatalog.Ids, id => Assert.True(EventGlyphs.Has(id), id));
        var paths = EventCatalog.Ids.Select(EventGlyphs.For).ToList();
        Assert.Equal(paths.Count, paths.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(EventGlyphs.Fallback, paths);
        Assert.All(paths.Append(EventGlyphs.Fallback), path => Assert.Matches(@"^M[MmLlHhVvCcSsQqTtAaZz0-9 .\-]+$", path));
        Assert.Equal(EventGlyphs.Fallback, EventGlyphs.For("nonsense"));
        Assert.False(EventGlyphs.Has("nonsense"));
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
