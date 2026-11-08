using ScarletMeter.WorldEvents.FieldBosses;

namespace ScarletMeter.Tests;

/// <summary>
/// The field-boss catalog (A2Tools' table), what an observation says at an instant, the merge of sources (the newest
/// observation stands, the player's map winning a tie) and the overlay's notice planner: Soon from exactly the lead time
/// before a respawn until it, Up for a quarter of an hour after, and the exact next change.
/// </summary>
public sealed class FieldBossTimerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 5, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Tick = TimeSpan.FromTicks(1);
    private static readonly TimeSpan Lead = TimeSpan.FromMinutes(10);
    private const int Gartua = 101021;
    private const int Teegant = 101006;

    private static FieldBossTimer Dead(int id, DateTimeOffset? back, DateTimeOffset observed, FieldBossSource source = FieldBossSource.NotMeter) =>
        new(id, false, back, observed, source);

    private static FieldBossTimer Up(int id, DateTimeOffset? since, DateTimeOffset observed, FieldBossSource source = FieldBossSource.NotMeter) =>
        new(id, true, since, observed, source);

    private static FieldBossAlertPlan Plan(DateTimeOffset now, params FieldBossTimer[] timers) =>
        FieldBossAlertPlanner.Plan(FieldBossBoard.Merge(timers, now), timers.Select(t => t.BossId).ToHashSet(), now, Lead);

    // ---------- The catalog ----------

    // Eternal Gartua as the game names it (literals, not the catalog's own constants), and every boss on its map.
    [Fact]
    public void TheCatalogKnowsEternalGartuaInVerteronAndEveryMapsBosses()
    {
        var gartua = FieldBossCatalog.Find(101021);
        Assert.Equal(new FieldBoss(101021, 2101074, "Eternal Gartua", 1010), gartua);
        Assert.Same(gartua, FieldBossCatalog.FindByNpcCode(2101074));
        Assert.Equal("Immortal Gartua", FieldBossCatalog.Find(111021)?.Name);
        Assert.Equal((24, 24, 8), (FieldBossCatalog.OnMap(1010).Count, FieldBossCatalog.OnMap(1110).Count, FieldBossCatalog.OnMap(20).Count));
        Assert.Equal(FieldBossCatalog.All.Count, FieldBossCatalog.All.Select(b => b.Id).Distinct().Count());
        Assert.Equal(FieldBossCatalog.All.Count, FieldBossCatalog.All.Select(b => b.NpcCode).Distinct().Count());
        // The map follows the id's range: 101001 and up Verteron, 111001 and up Altgard, 2001 to 2008 the Abyss.
        Assert.All(FieldBossCatalog.All, boss => Assert.Equal(boss.Id switch { >= 111001 => 1110, >= 101001 => 1010, _ => 20 }, boss.MapId));
        Assert.Null(FieldBossCatalog.Find(101025));
        Assert.Equal((1010, 1110, (int?)null), (FieldBossMaps.HomeOf(1503), FieldBossMaps.HomeOf(2503), FieldBossMaps.HomeOf(3503)));
    }

    // ---------- An observation at an instant ----------

    [Fact]
    public void ADeadBossIsRespawningUntilItsTimeAndUpSinceItAfter()
    {
        var back = Now.AddMinutes(30);
        var timer = Dead(Gartua, back, Now);
        Assert.Equal(new FieldBossState(FieldBossStatus.Respawning, back), timer.StateAt(back - Tick));
        Assert.Equal(new FieldBossState(FieldBossStatus.Up, back), timer.StateAt(back));
        Assert.Equal(new FieldBossState(FieldBossStatus.Dead, null), Dead(Gartua, null, Now).StateAt(Now));
        Assert.Equal(new FieldBossState(FieldBossStatus.Up, null), Up(Gartua, null, Now).StateAt(Now));
        Assert.Equal(new FieldBossState(FieldBossStatus.Up, Now.AddMinutes(-3)), Up(Gartua, Now.AddMinutes(-3), Now).StateAt(Now));
    }

    // ---------- The merge ----------

    [Fact]
    public void TheNewestObservationStandsAndThePlayersMapWinsATie()
    {
        var older = Dead(Gartua, Now.AddHours(2), Now.AddMinutes(-5));
        var newer = Up(Gartua, null, Now.AddMinutes(-1));
        Assert.Same(newer, FieldBossBoard.Merge([older, newer], Now)[Gartua]);
        Assert.Same(newer, FieldBossBoard.Merge([newer, older], Now)[Gartua]);

        var shared = Dead(Gartua, Now.AddHours(2), Now);
        var own = Dead(Gartua, Now.AddHours(3), Now, FieldBossSource.Map);
        Assert.Same(own, FieldBossBoard.Merge([shared, own], Now)[Gartua]);
        Assert.Same(own, FieldBossBoard.Merge([own, shared], Now)[Gartua]);
    }

    // Another PC's clock ahead of this one: its observation counts as made now, so it cannot outrank the player's map.
    [Fact]
    public void AnObservationDatedAheadOfNowCannotOutrankThePlayersMap()
    {
        var ahead = Dead(Gartua, Now.AddHours(2), Now.AddMinutes(20));
        var own = Dead(Gartua, Now.AddHours(3), Now, FieldBossSource.Map);
        Assert.Same(own, FieldBossBoard.Merge([ahead, own], Now)[Gartua]);
        Assert.Same(own, FieldBossBoard.Merge([own, ahead], Now)[Gartua]);
        // Without the player's map it stands.
        Assert.Same(ahead, FieldBossBoard.Merge([ahead], Now)[Gartua]);
    }

    // ---------- The notice planner ----------

    [Fact]
    public void SoonAppearsExactlyAtTheLeadTimeAndTurnsUpAtTheRespawn()
    {
        var back = Now.AddMinutes(30);
        var timer = Dead(Gartua, back, Now);

        var before = Plan(back - Lead - Tick, timer);
        Assert.Empty(before.Alerts);
        Assert.Equal(back - Lead, before.NextChange);

        var atLead = Plan(back - Lead, timer);
        var soon = Assert.Single(atLead.Alerts);
        Assert.Equal((FieldBossAlertKind.Soon, back, Gartua), (soon.Kind, soon.At, soon.Boss.Id));
        Assert.Equal(back, atLead.NextChange);

        Assert.Equal(FieldBossAlertKind.Soon, Assert.Single(Plan(back - Tick, timer).Alerts).Kind);

        var atRespawn = Plan(back, timer);
        var up = Assert.Single(atRespawn.Alerts);
        Assert.Equal((FieldBossAlertKind.Up, back), (up.Kind, up.At));
        Assert.Equal(back + FieldBossAlertPlanner.UpNoticeFor, atRespawn.NextChange);

        var gone = Plan(back + FieldBossAlertPlanner.UpNoticeFor, timer);
        Assert.Empty(gone.Alerts);
        Assert.Null(gone.NextChange);
    }

    // A boss seen up without since when (NotMeter's up entries): its notice runs a quarter of an hour from when it was seen.
    [Fact]
    public void ABossSeenUpWithoutASinceIsUpFromWhenItWasSeen()
    {
        var seen = Now.AddMinutes(-14);
        var plan = Plan(Now, Up(Gartua, null, seen));
        Assert.Equal((FieldBossAlertKind.Up, seen), (Assert.Single(plan.Alerts).Kind, plan.Alerts[0].At));
        Assert.Empty(Plan(Now, Up(Gartua, null, Now.AddMinutes(-15))).Alerts);
    }

    // Only chosen bosses with a timer get a notice; a boss down with no time gets none; an id the catalog does not know none.
    [Fact]
    public void OnlyChosenBossesWithATimeGetANotice()
    {
        var board = FieldBossBoard.Merge([Dead(Gartua, Now.AddMinutes(5), Now), Dead(Teegant, null, Now), Dead(999999, Now.AddMinutes(5), Now)], Now);
        Assert.Empty(FieldBossAlertPlanner.Plan(board, new HashSet<int> { Teegant, 999999 }, Now, Lead).Alerts);
        Assert.Empty(FieldBossAlertPlanner.Plan(board, new HashSet<int>(), Now, Lead).Alerts);
        Assert.Equal(Gartua, Assert.Single(FieldBossAlertPlanner.Plan(board, new HashSet<int> { Gartua, Teegant }, Now, Lead).Alerts).Boss.Id);
    }

    // Up before Soon, each by its instant, then by id.
    [Fact]
    public void NoticesAreUpFirstThenByTheirInstant()
    {
        var plan = Plan(Now,
            Dead(101002, Now.AddMinutes(8), Now),
            Dead(101001, Now.AddMinutes(3), Now),
            Up(101005, Now.AddMinutes(-2), Now),
            Up(101004, Now.AddMinutes(-2), Now));
        Assert.Equal([101004, 101005, 101001, 101002], plan.Alerts.Select(a => a.Boss.Id));
    }

    [Fact]
    public void ALeadTimeOutsideOneToSixtyMinutesThrows()
    {
        var board = FieldBossBoard.Merge([], Now);
        Assert.Throws<ArgumentOutOfRangeException>(() => FieldBossAlertPlanner.Plan(board, new HashSet<int>(), Now, TimeSpan.FromMinutes(1) - Tick));
        Assert.Throws<ArgumentOutOfRangeException>(() => FieldBossAlertPlanner.Plan(board, new HashSet<int>(), Now, TimeSpan.FromMinutes(60) + Tick));
        FieldBossAlertPlanner.Plan(board, new HashSet<int>(), Now, TimeSpan.FromMinutes(60));
    }

    // The plan stays the same up to its next change and differs at it, minute by minute over a respawn and its notice.
    [Fact]
    public void ThePlanIsConstantUntilItsNextChange()
    {
        var timers = new[] { Dead(Gartua, Now.AddMinutes(37), Now), Up(Teegant, Now.AddMinutes(4), Now) };
        for (var at = Now; at < Now.AddHours(1); at += TimeSpan.FromSeconds(30))
        {
            var plan = Plan(at, timers);
            if (plan.NextChange is not { } next) continue;
            var justBefore = Plan(next - Tick, timers);
            Assert.Equal(Shown(plan), Shown(justBefore));
            Assert.NotEqual(Shown(plan), Shown(Plan(next, timers)));
        }
    }

    private static List<(int, FieldBossAlertKind, DateTimeOffset)> Shown(FieldBossAlertPlan plan) =>
        [.. plan.Alerts.Select(a => (a.Boss.Id, a.Kind, a.At))];
}
