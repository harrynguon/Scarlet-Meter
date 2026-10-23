using ScarletMeter.WorldEvents;
using static ScarletMeter.Tests.WorldEventsTestData;

namespace ScarletMeter.Tests;

/// <summary>
/// The overlay's alert planner on the Global preset at UTC+9: Soon and Open boundaries, what never alerts,
/// disabled rules, argument checks, urgency order and the exactness of the next-change instant.
/// </summary>
public sealed class AlertPlannerTests
{
    private static readonly TimeSpan OneTick = TimeSpan.FromTicks(1);

    // Global Shugo: every hour on the hour (server and UTC agree at UTC+9), entry 3 minutes, window 7.
    private static AlertPlan Plan(DateTimeOffset now, params string[] enabled) =>
        AlertPlanner.Plan(EventCatalog.Global, enabled.ToHashSet(StringComparer.Ordinal), now, Asia);

    private static AlertPlan Plan(DateTimeOffset now, TimeSpan leadTime, params string[] enabled) =>
        AlertPlanner.Plan(EventCatalog.Global, enabled.ToHashSet(StringComparer.Ordinal), now, Asia, leadTime);

    // What the overlay would show, without the countdowns, which move continuously.
    private static List<(string Id, AlertKind Kind, DateTimeOffset Start, DateTimeOffset Deadline)> Shown(AlertPlan plan) =>
        [.. plan.Alerts.Select(alert => (alert.Rule.Id, alert.Kind, alert.Start, alert.Deadline))];

    [Fact]
    public void SoonAppearsExactlyAtTheLeadTime()
    {
        var before = Plan(At("2026-10-23T00:55:00Z") - OneTick, EventIds.Shugo);
        Assert.Empty(before.Alerts);
        Assert.Equal(At("2026-10-23T00:55:00Z"), before.NextChange);

        var atLead = Plan(At("2026-10-23T00:55:00Z"), EventIds.Shugo);
        var soon = Assert.Single(atLead.Alerts);
        Assert.Equal((AlertKind.Soon, At("2026-10-23T01:00:00Z"), At("2026-10-23T01:00:00Z"), TimeSpan.FromMinutes(5)), (soon.Kind, soon.Start, soon.Deadline, soon.Remaining));
        Assert.Equal(At("2026-10-23T01:00:00Z"), atLead.NextChange);

        var lastTick = Assert.Single(Plan(At("2026-10-23T01:00:00Z") - OneTick, EventIds.Shugo).Alerts);
        Assert.Equal((AlertKind.Soon, OneTick), (lastTick.Kind, lastTick.Remaining));
    }

    [Fact]
    public void AtTheStartSoonGivesWayToOpenUntilTheEntryCloses()
    {
        var atStart = Plan(At("2026-10-23T01:00:00Z"), EventIds.Shugo);
        var open = Assert.Single(atStart.Alerts);
        Assert.Equal((AlertKind.Open, At("2026-10-23T01:00:00Z"), At("2026-10-23T01:03:00Z"), TimeSpan.FromMinutes(3)), (open.Kind, open.Start, open.Deadline, open.Remaining));
        Assert.Equal(At("2026-10-23T01:03:00Z"), atStart.NextChange);

        Assert.Equal(OneTick, Assert.Single(Plan(At("2026-10-23T01:03:00Z") - OneTick, EventIds.Shugo).Alerts).Remaining);

        // Entry closed: still active until 01:07, but nothing to join, so no alert until the next lead time.
        var atClose = Plan(At("2026-10-23T01:03:00Z"), EventIds.Shugo);
        Assert.Empty(atClose.Alerts);
        Assert.Equal(At("2026-10-23T01:55:00Z"), atClose.NextChange);
    }

    [Fact]
    public void TheRiftPortalIsAnOpenAlert()
    {
        // Rift at 11:00 server is 02:00 UTC; the portal closes ten minutes later.
        Assert.Equal(At("2026-10-23T01:55:00Z"), Plan(At("2026-10-23T01:00:00Z"), EventIds.Rift).NextChange);
        var open = Assert.Single(Plan(At("2026-10-23T02:00:00Z"), EventIds.Rift).Alerts);
        Assert.Equal((AlertKind.Open, At("2026-10-23T02:10:00Z")), (open.Kind, open.Deadline));
        Assert.Empty(Plan(At("2026-10-23T02:10:00Z"), EventIds.Rift).Alerts);
    }

    [Fact]
    public void ResetsGetSoonOnly()
    {
        var soon = Assert.Single(Plan(At("2026-10-23T06:55:00Z"), EventIds.Daily).Alerts);
        Assert.Equal((AlertKind.Soon, At("2026-10-23T07:00:00Z")), (soon.Kind, soon.Deadline));

        var atReset = Plan(At("2026-10-23T07:00:00Z"), EventIds.Daily);
        Assert.Empty(atReset.Alerts);
        Assert.Equal(At("2026-10-24T06:55:00Z"), atReset.NextChange);

        // Even a reset given an entry window by a saved override never shows Open.
        var custom = WithChange(EventCatalog.Global, EventIds.Daily, rule => rule with { DurationMinutes = 10, EntryMinutes = 5 });
        var enabled = new HashSet<string> { EventIds.Daily };
        Assert.Empty(AlertPlanner.Plan(custom, enabled, At("2026-10-23T07:00:00Z"), Asia).Alerts);
        Assert.Equal(At("2026-10-24T06:55:00Z"), AlertPlanner.Plan(custom, enabled, At("2026-10-23T07:02:00Z"), Asia).NextChange);
    }

    [Fact]
    public void ActiveEventsWithoutAnEntryWindowGetNoAlert()
    {
        // Global bosses and sieges have unknown end times (duration 0): Soon before the start, nothing after.
        Assert.Equal(AlertKind.Soon, Assert.Single(Plan(At("2026-10-26T11:55:00Z"), EventIds.Siege).Alerts).Kind); // Monday 21:00 server
        Assert.Empty(Plan(At("2026-10-26T12:00:00Z"), EventIds.Siege).Alerts);

        // Legacy windows are scheduled as active but have no entry window.
        var legacy = new HashSet<string> { EventIds.Kaira, EventIds.Siege };
        Assert.Equal(EventPhase.ScheduledActive, EventSchedule.Occurrence(Legacy(EventIds.Kaira), At("2026-10-23T01:10:00Z"), Asia).Phase);
        Assert.Empty(AlertPlanner.Plan(EventCatalog.Legacy, legacy, At("2026-10-23T01:10:00Z"), Asia).Alerts);
        Assert.Empty(AlertPlanner.Plan(EventCatalog.Legacy, legacy, At("2026-10-26T12:10:00Z"), Asia).Alerts);
    }

    [Fact]
    public void OpenHidesSoonForTheSameEvent()
    {
        // With an hour's lead the next Shugo is always within it; while entry is open only Open shows.
        var open = Plan(At("2026-10-23T00:01:00Z"), TimeSpan.FromMinutes(60), EventIds.Shugo);
        Assert.Equal(AlertKind.Open, Assert.Single(open.Alerts).Kind);
        Assert.Equal(At("2026-10-23T00:03:00Z"), open.NextChange);

        var soon = Assert.Single(Plan(At("2026-10-23T00:03:00Z"), TimeSpan.FromMinutes(60), EventIds.Shugo).Alerts);
        Assert.Equal((AlertKind.Soon, TimeSpan.FromMinutes(57)), (soon.Kind, soon.Remaining));
    }

    [Fact]
    public void OverlappingWindowsChangeAtTheNextStart()
    {
        var custom = WithChange(EventCatalog.Global, EventIds.Shugo, rule => rule with { DurationMinutes = 90, EntryMinutes = 90 });
        var plan = AlertPlanner.Plan(custom, new HashSet<string> { EventIds.Shugo }, At("2026-10-23T00:30:00Z"), Asia);
        Assert.Equal((AlertKind.Open, At("2026-10-23T01:30:00Z")), (plan.Alerts.Single().Kind, plan.Alerts.Single().Deadline));
        Assert.Equal(At("2026-10-23T01:00:00Z"), plan.NextChange);
    }

    [Fact]
    public void DisabledRulesAreSkippedEntirely()
    {
        var none = Plan(At("2026-10-23T01:56:00Z"));
        Assert.Empty(none.Alerts);
        Assert.Null(none.NextChange);
        Assert.Null(Plan(At("2026-10-23T01:56:00Z"), "not-a-rule").NextChange);

        // Rift and Shugo are both five minutes out; only the enabled one shows.
        Assert.Equal([(EventIds.Shugo, AlertKind.Soon, At("2026-10-23T02:00:00Z"), At("2026-10-23T02:00:00Z"))], Shown(Plan(At("2026-10-23T01:56:00Z"), EventIds.Shugo)));

        // A disabled rule is not even checked; an enabled unusable one is reported.
        var broken = WithChange(EventCatalog.Global, EventIds.Rift, rule => rule with { Hours = [] });
        Assert.Single(AlertPlanner.Plan(broken, new HashSet<string> { EventIds.Shugo }, At("2026-10-23T01:56:00Z"), Asia).Alerts);
        Assert.Throws<ArgumentException>(() => AlertPlanner.Plan(broken, new HashSet<string> { EventIds.Rift }, At("2026-10-23T01:56:00Z"), Asia));
    }

    [Fact]
    public void LeadTimesOutsideOneToSixtyMinutesAreRejected()
    {
        foreach (var lead in new[] { TimeSpan.Zero, TimeSpan.FromSeconds(59), TimeSpan.FromMinutes(60) + OneTick, TimeSpan.FromMinutes(-5), TimeSpan.MaxValue })
        {
            var error = Assert.Throws<ArgumentOutOfRangeException>(() => Plan(At("2026-10-23T00:00:00Z"), lead, EventIds.Shugo));
            Assert.Equal("leadTime", error.ParamName);
        }

        Assert.Equal(At("2026-10-23T00:59:00Z"), Plan(At("2026-10-23T00:10:00Z"), TimeSpan.FromMinutes(1), EventIds.Shugo).NextChange);
        Assert.Equal(AlertKind.Soon, Assert.Single(Plan(At("2026-10-23T00:10:00Z"), TimeSpan.FromMinutes(60), EventIds.Shugo).Alerts).Kind);
        Assert.Equal(TimeSpan.FromMinutes(5), AlertPlanner.DefaultLeadTime);
    }

    [Fact]
    public void InvalidArgumentsAreRejected()
    {
        var enabled = new HashSet<string> { EventIds.Shugo };
        foreach (var offset in new[] { Offset(14) + TimeSpan.FromMinutes(1), Offset(-13), new TimeSpan(9, 0, 1) })
        {
            var error = Assert.Throws<ArgumentOutOfRangeException>(() => AlertPlanner.Plan(EventCatalog.Global, enabled, At("2026-10-23T00:00:00Z"), offset));
            Assert.Equal("serverOffset", error.ParamName);
        }

        Assert.Throws<ArgumentNullException>(() => AlertPlanner.Plan(null!, enabled, At("2026-10-23T00:00:00Z"), Asia));
        Assert.Throws<ArgumentNullException>(() => AlertPlanner.Plan(EventCatalog.Global, null!, At("2026-10-23T00:00:00Z"), Asia));
        Assert.Throws<ArgumentException>(() => AlertPlanner.Plan([Global(EventIds.Shugo), null!], enabled, At("2026-10-23T00:00:00Z"), Asia));
    }

    [Fact]
    public void AlertsAreOrderedOpenFirstThenByDeadlineThenById()
    {
        var all = EventCatalog.Ids.ToArray();

        // Rift and Shugo start together: same deadline, so by id.
        Assert.Equal(
            [(EventIds.Rift, AlertKind.Soon, At("2026-10-23T02:00:00Z"), At("2026-10-23T02:00:00Z")), (EventIds.Shugo, AlertKind.Soon, At("2026-10-23T02:00:00Z"), At("2026-10-23T02:00:00Z"))],
            Shown(Plan(At("2026-10-23T01:56:00Z"), all)));

        // Both open (Shugo closes first), and with a 30-minute lead the 02:30 Invasion is Soon.
        Assert.Equal(
            [
                (EventIds.Shugo, AlertKind.Open, At("2026-10-23T02:00:00Z"), At("2026-10-23T02:03:00Z")),
                (EventIds.Rift, AlertKind.Open, At("2026-10-23T02:00:00Z"), At("2026-10-23T02:10:00Z")),
                (EventIds.Invasion, AlertKind.Soon, At("2026-10-23T02:30:00Z"), At("2026-10-23T02:30:00Z")),
            ],
            Shown(Plan(At("2026-10-23T02:01:00Z"), TimeSpan.FromMinutes(30), all)));

        // Kaira and Shugo (16:00 server) coincide with the daily reset (07:00 UTC).
        Assert.Equal([EventIds.Daily, EventIds.Kaira, EventIds.Shugo], Plan(At("2026-10-23T06:56:00Z"), all).Alerts.Select(alert => alert.Rule.Id));
    }

    [Fact]
    public void InstantsAreUtcWhateverOffsetNowHas()
    {
        var plan = Plan(At("2026-10-23T02:01:00Z").ToOffset(Offset(-7)), TimeSpan.FromMinutes(30), [.. EventCatalog.Ids]);
        Assert.Equal(Shown(Plan(At("2026-10-23T02:01:00Z"), TimeSpan.FromMinutes(30), [.. EventCatalog.Ids])), Shown(plan));
        Assert.Equal(TimeSpan.Zero, plan.NextChange!.Value.Offset);
        Assert.All(plan.Alerts, alert => Assert.Equal((TimeSpan.Zero, TimeSpan.Zero), (alert.Start.Offset, alert.Deadline.Offset)));
    }

    /// <summary>
    /// The plan stays the same on every whole minute (when every rule changes) up to NextChange and differs at
    /// it, over eight days that include a weekly reset, for both presets and several lead times.
    /// </summary>
    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 5)]
    [InlineData(false, 60)]
    [InlineData(true, 5)]
    public void NextChangeIsExactlyWhenTheAlertsChange(bool legacy, int leadMinutes)
    {
        var rules = legacy ? EventCatalog.Legacy : EventCatalog.Global;
        var enabled = EventCatalog.Ids.ToHashSet(StringComparer.Ordinal);
        var lead = TimeSpan.FromMinutes(leadMinutes);
        AlertPlan PlanAt(DateTimeOffset now) => AlertPlanner.Plan(rules, enabled, now, Asia, lead);

        var now = At("2026-10-21T00:00:00Z");
        var plan = PlanAt(now);
        var changes = 0;
        while (now < At("2026-10-29T00:00:00Z"))
        {
            var change = plan.NextChange!.Value;
            Assert.True(change > now);
            for (var minute = now.AddMinutes(1); minute < change; minute = minute.AddMinutes(1))
            {
                Assert.Equal(Shown(plan), Shown(PlanAt(minute)));
            }

            Assert.Equal(Shown(plan), Shown(PlanAt(change - OneTick)));
            var next = PlanAt(change);
            Assert.NotEqual(Shown(plan), Shown(next));
            (now, plan) = (change, next);
            changes++;
        }

        Assert.True(changes > 8 * 24, $"Only {changes} changes over eight days.");
    }

    [Fact]
    public void PlanningWithoutAlertsAllocatesNothing()
    {
        var enabled = new HashSet<string>(EventCatalog.Ids, StringComparer.Ordinal);
        var quiet = At("2026-10-23T00:10:00Z"); // nothing within five minutes, nothing open
        Assert.Empty(AlertPlanner.Plan(EventCatalog.Global, enabled, quiet, Asia).Alerts);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) AlertPlanner.Plan(EventCatalog.Global, enabled, quiet, Asia);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
