using ScarletMeter.WorldEvents;
using static ScarletMeter.Tests.WorldEventsTestData;

namespace ScarletMeter.Tests;

/// <summary>
/// Recurrence, ported from the harrynguon/AIon2-Dps-Meter fork's tests/companion.test.mjs (legacy and Global
/// rules), plus the A2Tools reproduction table of its docs/TIMER-COMPARISON.md and Scarlet's own boundaries.
/// </summary>
public sealed class EventScheduleTests
{
    private static readonly TimeSpan OneTick = TimeSpan.FromTicks(1);

    [Theory]
    [InlineData(-8)]
    [InlineData(0)]
    [InlineData(5.5)]
    [InlineData(9)]
    [InlineData(11)]
    public void UtcSchedulesDoNotMoveWithTheServerClock(double offsetHours)
    {
        var kaira = EventSchedule.Occurrence(Legacy(EventIds.Kaira), At("2026-10-23T00:59:59Z"), Offset(offsetHours));
        Assert.Equal(At("2026-10-23T01:00:00Z"), kaira.Start);
    }

    [Fact]
    public void FridayNahmaUsesTheServerDateAndRollsToSundayAfterItsWindow()
    {
        var nahma = Legacy(EventIds.Nahma);
        Assert.Equal(At("2026-10-23T12:00:00Z"), EventSchedule.Occurrence(nahma, At("2026-10-23T11:59:59Z"), Asia).Start);
        Assert.Equal(At("2026-10-25T12:00:00Z"), EventSchedule.Occurrence(nahma, At("2026-10-23T12:45:00Z"), Asia).Start);
    }

    [Fact]
    public void LegacyRiftHasExactPortalAndActivityBoundaries()
    {
        var rift = Legacy(EventIds.Rift);
        EventOccurrence RiftAt(DateTimeOffset now) => EventSchedule.Occurrence(rift, now, Asia);

        Assert.Equal(EventPhase.PortalOpen, RiftAt(At("2026-10-23T00:00:00Z")).Phase);
        Assert.Equal(EventPhase.PortalOpen, RiftAt(At("2026-10-23T00:09:59Z")).Phase);
        Assert.Equal(EventPhase.PortalOpen, RiftAt(At("2026-10-23T00:10:00Z") - OneTick).Phase);
        Assert.Equal(EventPhase.ActiveEntryClosed, RiftAt(At("2026-10-23T00:10:00Z")).Phase);
        Assert.True(RiftAt(At("2026-10-23T00:59:59Z")).IsActive);
        Assert.True(RiftAt(At("2026-10-23T01:00:00Z") - OneTick).IsActive);

        var after = RiftAt(At("2026-10-23T01:00:00Z"));
        Assert.False(after.IsActive);
        Assert.Equal(At("2026-10-23T03:00:00Z"), after.Start);
    }

    [Fact]
    public void ResetsRollOverTheYearAndTheWeekWithoutAnActiveState()
    {
        var beforeNewYear = EventSchedule.Occurrence(Legacy(EventIds.Daily), At("2026-12-31T23:59:59Z"), Asia);
        Assert.Equal(At("2027-01-01T00:00:00Z"), beforeNewYear.Start);
        Assert.Equal(EventPhase.Upcoming, beforeNewYear.Phase);

        var atNewYear = EventSchedule.Occurrence(Legacy(EventIds.Daily), At("2027-01-01T00:00:00Z"), Asia);
        Assert.Equal(At("2027-01-02T00:00:00Z"), atNewYear.Start);
        Assert.Equal(EventPhase.Upcoming, atNewYear.Phase);

        Assert.Equal(At("2026-11-04T00:00:00Z"), EventSchedule.Occurrence(Legacy(EventIds.Weekly), At("2026-10-28T00:00:00Z"), Asia).Start);
        Assert.Equal(At("2027-01-06T00:00:00Z"), EventSchedule.Occurrence(Legacy(EventIds.Weekly), At("2026-12-30T00:00:00Z"), Asia).Start);

        // Global resets are on the UTC clock.
        Assert.Equal(At("2027-01-01T07:00:00Z"), EventSchedule.Occurrence(Global(EventIds.Daily), At("2026-12-31T07:00:00Z"), Asia).Start);
        Assert.Equal(At("2027-01-06T07:00:00Z"), EventSchedule.Occurrence(Global(EventIds.Weekly), At("2026-12-30T07:00:00Z"), Asia).Start);
        Assert.Equal(At("2026-10-28T07:00:00Z"), EventSchedule.Occurrence(Global(EventIds.Weekly), At("2026-10-24T09:00:00Z"), Asia).Start);
    }

    [Fact]
    public void FractionalOffsetsShiftHourlySchedules()
    {
        Assert.Equal(At("2026-10-23T00:30:00Z"), EventSchedule.Occurrence(Legacy(EventIds.Shugo), At("2026-10-23T00:16:00Z"), Offset(5.5)).Start);

        var shugo = Global(EventIds.Shugo);
        var before = EventSchedule.Occurrence(shugo, At("2026-10-23T00:14:59Z"), Offset(5.75));
        Assert.Equal((EventPhase.Upcoming, At("2026-10-23T00:15:00Z")), (before.Phase, before.Start));
        Assert.Equal(EventPhase.EntryOpen, EventSchedule.Occurrence(shugo, At("2026-10-23T00:15:00Z"), Offset(5.75)).Phase);

        // Server :30 at UTC−3:30 is a whole UTC hour.
        Assert.Equal(EventPhase.EntryOpen, EventSchedule.Occurrence(Global(EventIds.Invasion), At("2026-10-23T10:00:00Z"), Offset(-3.5)).Phase);
    }

    [Fact]
    public void WeekdaysAreReadOnTheServerClock()
    {
        // Saturday 16:00 at UTC−8; Nahma's Sunday 21:00 server is Monday 05:00 UTC.
        var west = EventSchedule.Occurrence(Global(EventIds.Nahma), At("2026-10-25T00:00:00Z"), Offset(-8));
        Assert.Equal(At("2026-10-26T05:00:00Z"), west.Start);

        // At the UTC+14 limit, Friday 21:00 server is Friday 07:00 UTC; at UTC−12 it is Saturday 09:00 UTC.
        Assert.Equal(At("2026-10-23T07:00:00Z"), EventSchedule.Occurrence(Global(EventIds.Nahma), At("2026-10-23T06:59:00Z"), Offset(14)).Start);
        Assert.Equal(At("2026-10-24T09:00:00Z"), EventSchedule.Occurrence(Global(EventIds.Nahma), At("2026-10-24T08:59:00Z"), Offset(-12)).Start);
    }

    [Fact]
    public void TimelinePutsActiveEventsFirstAndKeepsSimultaneousOnes()
    {
        // Saturday 21:00 server: the legacy rift, Shugo and siege windows all run at once.
        var all = EventSchedule.Timeline(EventCatalog.Legacy, At("2026-10-24T12:00:00Z"), Asia);

        Assert.Equal(9, all.Count);
        Assert.Equal(
            [EventIds.Rift, EventIds.Shugo, EventIds.Siege, EventIds.Executors, EventIds.Invasion, EventIds.Kaira, EventIds.Daily, EventIds.Nahma, EventIds.Weekly],
            all.Select(occurrence => occurrence.Rule.Id));
        Assert.Equal([true, true, true, false, false, false, false, false, false], all.Select(occurrence => occurrence.IsActive));
        Assert.All(all, occurrence =>
        {
            Assert.True(occurrence.Remaining > TimeSpan.Zero);
            Assert.InRange(occurrence.Progress, 0, 100);
        });
    }

    [Fact]
    public void GlobalPresetHasUtcResetsAndExactEntryBoundaries()
    {
        foreach (var offset in new[] { -8, 0, 9, 11 })
        {
            Assert.Equal(At("2026-10-23T07:00:00Z"), EventSchedule.Occurrence(Global(EventIds.Daily), At("2026-10-23T06:59:59Z"), Offset(offset)).Start);
        }

        Assert.Equal(At("2026-11-04T07:00:00Z"), EventSchedule.Occurrence(Global(EventIds.Weekly), At("2026-10-28T07:00:00Z"), Asia).Start);
        Assert.Equal(EventPhase.PortalOpen, EventSchedule.Occurrence(Global(EventIds.Rift), At("2026-10-23T02:09:59Z"), Asia).Phase);
        Assert.Equal(At("2026-10-23T05:00:00Z"), EventSchedule.Occurrence(Global(EventIds.Rift), At("2026-10-23T02:10:00Z"), Asia).Start);

        foreach (var (id, start, total) in new[] { (EventIds.Shugo, At("2026-10-23T00:00:00Z"), 7), (EventIds.Invasion, At("2026-10-23T00:30:00Z"), 13) })
        {
            var rule = Global(id);
            var lastEntryMillisecond = EventSchedule.Occurrence(rule, start.AddMilliseconds(179_999), Asia);
            Assert.Equal(EventPhase.EntryOpen, lastEntryMillisecond.Phase);
            Assert.Equal(TimeSpan.FromMilliseconds(1), lastEntryMillisecond.Remaining);
            Assert.Equal(EventPhase.ActiveEntryClosed, EventSchedule.Occurrence(rule, start.AddMilliseconds(180_000), Asia).Phase);
            Assert.Equal(start.AddMinutes(total), EventSchedule.Occurrence(rule, start.AddMilliseconds(180_000), Asia).TargetEnd);
            Assert.False(EventSchedule.Occurrence(rule, start.AddMinutes(total), Asia).IsActive);
        }
    }

    /// <summary>
    /// docs/TIMER-COMPARISON.md in the fork: what A2Tools shows for Asia (UTC+9). A2Tools counts to the next
    /// joinable start: the running occurrence while its entry is open, otherwise the next one.
    /// </summary>
    [Theory]
    [InlineData(EventIds.Daily, "2026-10-23T06:59:59Z", "2026-10-23T07:00:00Z", EventPhase.Upcoming)]
    [InlineData(EventIds.Daily, "2026-10-23T07:00:00Z", "2026-10-24T07:00:00Z", EventPhase.Upcoming)]
    [InlineData(EventIds.Weekly, "2026-10-28T07:00:00Z", "2026-11-04T07:00:00Z", EventPhase.Upcoming)]
    [InlineData(EventIds.Rift, "2026-10-23T01:59:59Z", "2026-10-23T02:00:00Z", EventPhase.Upcoming)]
    [InlineData(EventIds.Rift, "2026-10-23T02:09:59Z", "2026-10-23T02:00:00Z", EventPhase.PortalOpen)]
    [InlineData(EventIds.Rift, "2026-10-23T02:10:00Z", "2026-10-23T05:00:00Z", EventPhase.Upcoming)]
    [InlineData(EventIds.Shugo, "2026-10-23T00:02:59Z", "2026-10-23T00:00:00Z", EventPhase.EntryOpen)]
    [InlineData(EventIds.Shugo, "2026-10-23T00:03:00Z", "2026-10-23T01:00:00Z", EventPhase.ActiveEntryClosed)]
    [InlineData(EventIds.Invasion, "2026-10-23T00:33:00Z", "2026-10-23T01:30:00Z", EventPhase.ActiveEntryClosed)]
    public void GlobalPresetReproducesA2Tools(string id, string now, string joinableStart, EventPhase phase)
    {
        var occurrence = EventSchedule.Occurrence(Global(id), At(now), Asia);
        Assert.Equal(phase, occurrence.Phase);
        Assert.Equal(At(joinableStart), occurrence.IsEntryOpen ? occurrence.Start : occurrence.Next);
    }

    [Fact]
    public void A2ToolsRiftPortalEndsAtTenPast()
    {
        Assert.Equal(At("2026-10-23T02:10:00Z"), EventSchedule.Occurrence(Global(EventIds.Rift), At("2026-10-23T02:09:59Z"), Asia).TargetEnd);
    }

    [Fact]
    public void GlobalAndLegacyAgreeOnBossesAndDisagreeOnResetsAndRiftsAtUtcPlusNine()
    {
        // TIMER-COMPARISON.md: Kaira and Nahma agree at UTC+9 (nine is divisible by three); resets and rifts do not.
        var disagreements = new HashSet<string>(StringComparer.Ordinal);
        var kairaDiffersAtUtcPlusEight = false;
        for (var now = At("2026-10-21T00:00:00Z"); now < At("2026-10-29T00:00:00Z"); now = now.AddMinutes(30))
        {
            foreach (var id in EventCatalog.Ids)
            {
                if (EventSchedule.Occurrence(Legacy(id), now, Asia).Next != EventSchedule.Occurrence(Global(id), now, Asia).Next) disagreements.Add(id);
            }

            kairaDiffersAtUtcPlusEight |= EventSchedule.Occurrence(Legacy(EventIds.Kaira), now, Offset(8)).Next
                != EventSchedule.Occurrence(Global(EventIds.Kaira), now, Offset(8)).Next;
        }

        Assert.Equal(new HashSet<string> { EventIds.Daily, EventIds.Weekly, EventIds.Rift }, disagreements);
        Assert.True(kairaDiffersAtUtcPlusEight);
    }

    [Fact]
    public void ResultsDependOnTheInstantNotOnItsOffset()
    {
        var instant = At("2026-10-23T02:05:00Z");
        var expected = EventSchedule.Timeline(EventCatalog.Global, instant, Asia);
        foreach (var offset in new[] { Offset(-11), Offset(5.75), Offset(13), TimeSpan.Zero })
        {
            Assert.Equal(expected, EventSchedule.Timeline(EventCatalog.Global, instant.ToOffset(offset), Asia));
        }

        Assert.All(expected, occurrence =>
        {
            Assert.Equal(TimeSpan.Zero, occurrence.Start.Offset);
            Assert.Equal(TimeSpan.Zero, occurrence.Next.Offset);
            Assert.Equal(TimeSpan.Zero, occurrence.End.Offset);
            Assert.Equal(TimeSpan.Zero, occurrence.TargetEnd.Offset);
        });
    }

    [Fact]
    public void StartEndAndEntryEndDescribeTheSameOccurrence()
    {
        // Upcoming: the next occurrence's window (the fork's end pointed at the previous one, which its UI never read).
        var upcoming = EventSchedule.Occurrence(Global(EventIds.Shugo), At("2026-10-23T00:30:00Z"), Asia);
        Assert.Equal(
            (At("2026-10-23T01:00:00Z"), At("2026-10-23T01:07:00Z"), (DateTimeOffset?)At("2026-10-23T01:03:00Z"), At("2026-10-23T01:00:00Z")),
            (upcoming.Start, upcoming.End, upcoming.EntryEnd, upcoming.TargetEnd));

        var reset = EventSchedule.Occurrence(Global(EventIds.Daily), At("2026-10-23T00:30:00Z"), Asia);
        Assert.Equal((reset.Start, (DateTimeOffset?)null), (reset.End, reset.EntryEnd));

        // A legacy boss window has no entry window: active without a countdown to an entry close.
        var kaira = EventSchedule.Occurrence(Legacy(EventIds.Kaira), At("2026-10-23T01:10:00Z"), Asia);
        Assert.Equal((EventPhase.ScheduledActive, At("2026-10-23T01:30:00Z"), (DateTimeOffset?)null), (kaira.Phase, kaira.TargetEnd, kaira.EntryEnd));
    }

    [Fact]
    public void ProgressFillsOverTheFinalHourAndOverTheActiveCountdown()
    {
        var shugo = Global(EventIds.Shugo);
        Assert.Equal(0, EventSchedule.Occurrence(Global(EventIds.Rift), At("2026-10-23T01:00:00Z"), Asia).Progress); // an hour ahead
        Assert.Equal(0, EventSchedule.Occurrence(Global(EventIds.Weekly), At("2026-10-24T00:00:00Z"), Asia).Progress); // days ahead
        Assert.Equal(50, EventSchedule.Occurrence(shugo, At("2026-10-23T00:30:00Z"), Asia).Progress, 9); // 30 minutes ahead
        Assert.Equal(50, EventSchedule.Occurrence(shugo, At("2026-10-23T00:01:30Z"), Asia).Progress, 9); // half the entry window
    }

    [Fact]
    public void UnusableInputsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => EventSchedule.Occurrence(null!, At("2026-10-23T00:00:00Z"), Asia));
        var noHours = Global(EventIds.Shugo) with { Hours = [] };
        Assert.Equal("rule", Assert.Throws<ArgumentException>(() => EventSchedule.Occurrence(noHours, At("2026-10-23T00:00:00Z"), Asia)).ParamName);
        var entryTooLong = Global(EventIds.Shugo) with { EntryMinutes = 8 };
        Assert.Throws<ArgumentException>(() => EventSchedule.Occurrence(entryTooLong, At("2026-10-23T00:00:00Z"), Asia));

        foreach (var offset in new[] { Offset(14) + OneTick, Offset(-12) - TimeSpan.FromMinutes(1), new TimeSpan(5, 30, 30) })
        {
            var error = Assert.Throws<ArgumentOutOfRangeException>(() => EventSchedule.Occurrence(Global(EventIds.Shugo), At("2026-10-23T00:00:00Z"), offset));
            Assert.Equal("serverOffset", error.ParamName);
        }

        Assert.Throws<ArgumentException>(() => EventSchedule.Timeline([Global(EventIds.Shugo), null!], At("2026-10-23T00:00:00Z"), Asia));
    }
}
