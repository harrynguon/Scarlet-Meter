using AionDpsMeter.Core.Models;
using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.Services.Services.Session.Persistence;
using Microsoft.Extensions.DependencyInjection;
using ScarletMeter.Engine;
using ScarletMeter.Engine.Upstream;

namespace ScarletMeter.Tests;

/// <summary>
/// Training-dummy fights through the engine over the real service graph: once the meter knows the user, a fight
/// between other players and a dummy nearby is not shown, listed or saved; the user's own dummy fight is, with
/// everyone in it; until the meter knows the user, nothing changes from upstream. Names, ids and numbers are
/// synthetic; the dummy is a Training Scarecrow from upstream's mobs.json, which counts dummies as bosses.
/// </summary>
public sealed class DummyFightTests : IDisposable
{
    /// <summary>A Training Scarecrow in upstream's mobs.json.</summary>
    private const int ScarecrowCode = 2_300_229;
    private const int UserId = 7;
    private const int StrangerId = 8;
    private const int BossEntity = 900;
    private const int DummyEntity = 950;
    private const int OtherDummyEntity = 951;
    private const long TargetHp = 1_000_000_000;
    private static readonly DateTime Start = new(2026, 10, 26, 18, 0, 0, DateTimeKind.Local);

    private readonly TestEngineHost host = new(CaptureMode.None);
    private readonly SyntheticCombat combat;
    private readonly Skill strike = SyntheticCombat.Skill(1, "Synthetic strike");

    public DummyFightTests()
    {
        combat = new SyntheticCombat(host);
    }

    private ICombatEngine Engine => combat.Engine;

    [Fact]
    public void TheScarecrowIsADummyThatUpstreamCountsAsABoss()
    {
        var dummy = Dummy();
        Assert.True(dummy.IsDummy);
        Assert.True(dummy.IsBoss);
        Assert.False(Boss().IsDummy);
    }

    [Fact]
    public void TheHistoryStoreIsTheDummyFilterAroundUpstreamsStore()
    {
        // AddScarletEngine leaves one registration, which the whole graph (upstream's session manager, which saves
        // finished fights, included) resolves to the decorator around upstream's own store.
        var services = new ServiceCollection().AddScarletEngine(
            new EngineOptions { DataDirectory = host.DataDirectory, Capture = CaptureMode.None });
        var registration = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ICombatHistoryStore));
        Assert.Equal(ServiceLifetime.Singleton, registration.Lifetime); // upstream's, which its session manager relies on

        var store = Assert.IsType<DummyFightHistoryStore>(host.Services.GetRequiredService<ICombatHistoryStore>());
        Assert.IsType<CombatHistoryStore>(store.Inner);
    }

    [Fact]
    public void AStrangersDummyFightIsNotSaved()
    {
        User();
        var stranger = Stranger();
        var dummy = Dummy();
        combat.Hit(stranger, dummy, Start, 500, strike);
        combat.Hit(stranger, dummy, Start.AddSeconds(2), 500, strike);

        combat.CompleteAndSave(Start.AddSeconds(30));

        Assert.Empty(combat.History.GetSessionList());
        Assert.Equal(0, Engine.GetHistory(new HistoryQuery()).TotalCount);
    }

    [Fact]
    public void TheUsersDummyFightIsShownAndSavedWithEveryoneInIt()
    {
        var user = User();
        var stranger = Stranger();
        var dummy = Dummy();
        combat.Hit(stranger, dummy, Start, 500, strike);
        combat.Hit(user, dummy, Start.AddSeconds(1), 300, strike);

        Assert.Equal(["Alpha", "Beta"], Engine.GetSnapshot().Players.Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.NotNull(Engine.GetPlayerDetails(StrangerId));

        combat.CompleteAndSave(Start.AddSeconds(30));
        var saved = Assert.Single(combat.History.GetSessionList());
        var fight = Assert.IsType<HistoryFight>(Engine.GetHistoryFight(saved.SessionId));
        Assert.Equal(["Alpha", "Beta"], fight.Players.Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void BossFightsWithoutTheUserAreUnchanged()
    {
        User();
        var stranger = Stranger();
        combat.Hit(stranger, Boss(), Start, 500, strike);

        Assert.Equal("Beta", Assert.Single(Engine.GetSnapshot().Players).Name);
        combat.CompleteAndSave(Start.AddMinutes(1));
        Assert.Single(combat.History.GetSessionList());
    }

    [Fact]
    public void EveryDummyFightCountsWhileTheMeterDoesNotKnowTheUser()
    {
        // No player is marked as the user: a meter started after login has not seen the character's info.
        var stranger = Stranger();
        var dummy = Dummy();
        combat.Hit(stranger, dummy, Start, 500, strike);

        Assert.Equal("Beta", Assert.Single(Engine.GetSnapshot().Players).Name);
        Assert.NotNull(Engine.GetPlayerDetails(StrangerId));
        var running = Assert.Single(Engine.GetHistory(new HistoryQuery()).Entries);
        Assert.NotNull(Engine.GetHistoryFight(running.FightId));

        combat.CompleteAndSave(Start.AddSeconds(30));
        Assert.Single(combat.History.GetSessionList());
    }

    [Fact]
    public void AStrangersDummyFightLeavesTheOverlayAsItWas()
    {
        var user = User();
        var stranger = Stranger();
        var boss = Boss();
        var dummy = Dummy();

        // Alone, the stranger's fight shows nothing.
        combat.Hit(stranger, dummy, Start, 500, strike);
        Assert.Same(CombatSnapshot.Empty, Engine.GetSnapshot());

        combat.Hit(user, boss, Start.AddSeconds(1), 2_000, strike);
        combat.Hit(user, boss, Start.AddSeconds(2), 2_000, strike);
        var own = Engine.GetSnapshot();
        Assert.Equal(BossEntity, own.Target?.Id);

        // Five seconds after the user's last hit, upstream makes the stranger's fight the active one.
        combat.Hit(stranger, dummy, Start.AddSeconds(8), 500, strike);
        Assert.Equal(DummyEntity, combat.Sessions.GetActiveTargetInfo()?.Id);
        Assert.Same(own, Engine.GetSnapshot());
        Assert.Null(Engine.GetPlayerDetails(StrangerId));
        Assert.Null(Engine.GetPlayerDetails(UserId));

        // The user's next hit brings the overlay back to the user's fight, moving on.
        combat.Hit(user, boss, Start.AddSeconds(9), 2_000, strike);
        var next = Engine.GetSnapshot();
        Assert.Equal(BossEntity, next.Target?.Id);
        Assert.Equal(6_000, Assert.Single(next.Players).TotalDamage);
        Assert.NotNull(Engine.GetPlayerDetails(UserId));
    }

    // The snapshot's reads are separate upstream calls. When a stranger's hit makes their dummy fight the active one
    // between them, the target read before (the user's boss) and the players read after (the stranger's) would mix two
    // fights, and the overlay, which keeps what it showed while a dummy fight is ignored, would keep the mix up.
    [Fact]
    public void ASnapshotReadAcrossAChangeOfFightIsTakenAgain()
    {
        var user = User();
        var stranger = Stranger();
        var dummy = Dummy();
        combat.Hit(user, Boss(), Start, 2_000, strike);
        var own = Engine.GetSnapshot();

        var switched = false;
        ((UpstreamCombatEngine)Engine).BetweenSnapshotReads = () =>
        {
            if (switched) return;
            switched = true;
            combat.Hit(stranger, dummy, Start.AddSeconds(6), 500, strike); // six seconds after the user's last hit
        };
        Assert.Same(own, Engine.GetSnapshot());
        Assert.True(switched);
        Assert.Equal(DummyEntity, combat.Sessions.GetActiveTargetInfo()?.Id);
    }

    // A fight the user is not in but that is no dummy fight (the user's party fighting on while the user is dead, say)
    // stays up like any other while a stranger's dummy fight is ignored.
    [Fact]
    public void AFightWithoutTheUserStaysUpWhileAStrangersDummyFightIsIgnored()
    {
        User();
        var stranger = Stranger();
        var other = combat.Player(9, "Gamma", SyntheticCombat.Class(17, "Cleric"));
        combat.Hit(stranger, Boss(), Start, 2_000, strike);
        var party = Engine.GetSnapshot();
        Assert.Equal(BossEntity, party.Target?.Id);

        combat.Hit(other, Dummy(), Start.AddSeconds(6), 500, strike);
        Assert.Equal(DummyEntity, combat.Sessions.GetActiveTargetInfo()?.Id);
        Assert.Same(party, Engine.GetSnapshot());
    }

    // Saved fights are never filtered: one saved while the meter did not know the user (or before this change) still
    // lists and opens once it does.
    [Fact]
    public void AFightSavedBeforeTheUserWasKnownStillOpensOnceTheUserIs()
    {
        var stranger = Stranger();
        combat.Hit(stranger, Dummy(), Start, 500, strike);
        combat.CompleteAndSave(Start.AddSeconds(30));
        var saved = Assert.Single(combat.History.GetSessionList());

        User();
        Assert.Equal(saved.SessionId, Assert.Single(Engine.GetHistory(new HistoryQuery()).Entries).FightId);
        Assert.NotNull(Engine.GetHistoryFight(saved.SessionId));
    }

    [Fact]
    public void ResetDropsTheFightTheOverlayKept()
    {
        var user = User();
        var stranger = Stranger();
        combat.Hit(user, Boss(), Start, 2_000, strike);
        Assert.NotSame(CombatSnapshot.Empty, Engine.GetSnapshot());

        Engine.Reset();
        combat.Hit(stranger, Dummy(), Start.AddSeconds(10), 500, strike);
        Assert.Same(CombatSnapshot.Empty, Engine.GetSnapshot());
    }

    [Fact]
    public void AStrangersDummyFightShownBeforeTheUserWasKnownIsNotKept()
    {
        var stranger = Stranger();
        var dummy = Dummy();
        combat.Hit(stranger, dummy, Start, 500, strike);
        Assert.Equal("Beta", Assert.Single(Engine.GetSnapshot().Players).Name);

        // The character's info arrives (a relog): the stranger's fight now counts as one.
        User();
        combat.Hit(stranger, dummy, Start.AddSeconds(1), 500, strike);
        Assert.Same(CombatSnapshot.Empty, Engine.GetSnapshot());
    }

    [Fact]
    public void AStrangersRunningDummyFightIsNeitherListedNorOpened()
    {
        var user = User();
        var stranger = Stranger();
        // Three saved boss fights, an hour apart.
        for (var i = 0; i < 3; i++)
        {
            var at = Start.AddHours(i);
            combat.Hit(user, Boss(BossEntity + i), at, 1_000 * (i + 1), strike);
            combat.CompleteAndSave(at.AddMinutes(1));
        }

        // Running, oldest first: the stranger's dummy fight, a boss fight and the user's own dummy fight.
        var now = Start.AddHours(4);
        combat.Hit(stranger, Dummy(), now, 500, strike);
        combat.Hit(user, Boss(BossEntity + 10), now.AddSeconds(1), 4_000, strike);
        combat.Hit(user, Dummy(OtherDummyEntity), now.AddSeconds(2), 5_000, strike);
        var strangersFight = combat.Sessions.GetTargetHistory(DummyEntity).Single().SessionId;
        var usersDummyFight = combat.Sessions.GetTargetHistory(OtherDummyEntity).Single().SessionId;
        Assert.Equal(6, combat.Sessions.GetHistoryPage(new HistorySessionQuery()).TotalCount); // upstream lists all six

        // Every page agrees on the count, and together they hold every other fight once, newest first.
        var pages = Enumerable.Range(1, 4).Select(page => Engine.GetHistory(new HistoryQuery(Page: page, PageSize: 2))).ToList();
        Assert.All(pages, page => Assert.Equal(5, page.TotalCount));
        Assert.Equal([2, 2, 1, 0], pages.Select(page => page.Entries.Count));
        var entries = pages.SelectMany(page => page.Entries).ToList();
        Assert.Equal([5_000L, 4_000, 3_000, 2_000, 1_000], entries.Select(entry => entry.TotalDamage));
        Assert.Equal([false, false, true, true, true], entries.Select(entry => entry.Completed));
        Assert.DoesNotContain(entries, entry => entry.FightId == strangersFight);

        // A name filter counts only the running fights it matches.
        var scarecrows = Engine.GetHistory(new HistoryQuery("scarecrow"));
        Assert.Equal(1, scarecrows.TotalCount);
        Assert.Equal(usersDummyFight, Assert.Single(scarecrows.Entries).FightId);

        Assert.Null(Engine.GetHistoryFight(strangersFight));
        Assert.NotNull(Engine.GetHistoryFight(usersDummyFight));
    }

    // Synthetic players: the user, as the character's info marks one at login, and a stranger.
    private Player User() => combat.Player(UserId, "Alpha", SyntheticCombat.Class(11, "Gladiator"), isUser: true);

    private Player Stranger() => combat.Player(StrangerId, "Beta", SyntheticCombat.Class(14, "Ranger"));

    private Mob Dummy(int entityId = DummyEntity) => combat.Target(entityId, ScarecrowCode, TargetHp);

    private Mob Boss(int entityId = BossEntity) => combat.Target(entityId, SyntheticCombat.AultrossCode, TargetHp);

    public void Dispose() => host.Dispose();
}
