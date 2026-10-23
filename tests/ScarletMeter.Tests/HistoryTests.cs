using System.Text.Json;
using AionDpsMeter.Core.Models;
using ScarletMeter.Engine;

namespace ScarletMeter.Tests;

/// <summary>
/// Boss-fight history through the engine: synthetic fights are completed and saved through
/// CompleteAndPersistActiveSessions (the path the demo and the engine's shutdown use), then listed, filtered,
/// paged and reloaded from upstream's real SQLite store in the test's data directory.
/// </summary>
public sealed class HistoryTests : IDisposable
{
    private static readonly DateTime Day1 = new(2026, 10, 20, 20, 0, 0, DateTimeKind.Local);
    private const int AlphaId = 7;
    private const int BetaId = 8;

    private readonly TestEngineHost host = new(CaptureMode.None);
    private readonly SyntheticCombat combat;
    private readonly Player alpha;
    private readonly Player beta;
    private readonly Player pet;
    private readonly CharacterClass spirit = SyntheticCombat.Class(10, "Elementalist spirit");

    public HistoryTests()
    {
        combat = new SyntheticCombat(host);
        alpha = combat.Player(AlphaId, "Alpha", SyntheticCombat.Class(11, "Gladiator"), isUser: true);
        beta = combat.Player(BetaId, "Beta", SyntheticCombat.Class(16, "Elementalist"));
        combat.Entities.RegisterSummon(55, BetaId);
        pet = combat.Entities.GetOrCreateSessionPlayer(55, spirit);
    }

    private ICombatEngine Engine => combat.Engine;

    [Fact]
    public void SavedBossFightsAreListedNewestFirstWithTheirTotals()
    {
        var totals = new[]
        {
            Save(901, SyntheticCombat.AultrossCode, Day1, 60),
            Save(902, SyntheticCombat.FedivWraithCode, Day1.AddDays(1), 70),
            Save(903, SyntheticCombat.AultrossCode, Day1.AddDays(2), 80),
        };

        var page = Engine.GetHistory(new HistoryQuery());
        Assert.Equal(3, page.TotalCount);
        Assert.Equal((1, 50), (page.Page, page.PageSize));
        Assert.Equal(["Aultross", "Fediv Wraith", "Aultross"], page.Entries.Select(e => e.TargetName)); // names from mobs.json
        Assert.Equal(Enumerable.Reverse(totals), page.Entries.Select(e => e.TotalDamage));
        Assert.All(page.Entries, entry =>
        {
            Assert.True(entry.Completed);
            Assert.Equal(2, entry.PlayerCount); // the summon's damage is its owner's
            Assert.Equal(1_000_000_000, entry.TargetHpTotal);
            Assert.Equal(entry.End - entry.Start, entry.Duration);
            Assert.Equal(entry.TotalDamage / entry.Duration.TotalSeconds, entry.PartyDps, 6);
            Assert.Equal(DateTimeKind.Local, entry.Start.Kind); // the database returns unspecified times
        });
        Assert.Equal(Day1.AddDays(2), page.Entries[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(80.5), page.Entries[0].Duration); // Beta's last hit lands half a second late
    }

    [Fact]
    public void NameAndDateFiltersSelectFights()
    {
        Save(901, SyntheticCombat.AultrossCode, Day1, 60);
        Save(902, SyntheticCombat.FedivWraithCode, Day1.AddDays(1), 70);
        Save(903, SyntheticCombat.AultrossCode, Day1.AddDays(2), 80);

        Assert.Equal([Day1.AddDays(2), Day1], Starts(new HistoryQuery("  aultross ")));   // trimmed, any case
        Assert.Equal([Day1.AddDays(1)], Starts(new HistoryQuery("WRAITH")));
        Assert.Empty(Starts(new HistoryQuery("Nobody")));

        // Fights are matched by their end time, both bounds inclusive.
        var secondDay = Day1.Date.AddDays(1);
        Assert.Equal([Day1.AddDays(1)], Starts(new HistoryQuery(From: secondDay, To: secondDay.AddDays(1).AddTicks(-1))));
        Assert.Equal([Day1.AddDays(2), Day1.AddDays(1)], Starts(new HistoryQuery(From: secondDay)));
        Assert.Equal([Day1.AddDays(2), Day1.AddDays(1)], Starts(new HistoryQuery(From: secondDay.ToUniversalTime()))); // UTC works too
        Assert.Equal([Day1], Starts(new HistoryQuery("Aultross", To: secondDay)));
    }

    [Fact]
    public void PagesSplitTheListAcrossRunningAndSavedFights()
    {
        Save(901, SyntheticCombat.AultrossCode, Day1, 60);
        Save(902, SyntheticCombat.FedivWraithCode, Day1.AddDays(1), 70);
        Save(903, SyntheticCombat.AultrossCode, Day1.AddDays(2), 80);
        Fight(904, SyntheticCombat.FedivWraithCode, Day1.AddDays(3), 30); // still running

        var first = Engine.GetHistory(new HistoryQuery(PageSize: 3));
        Assert.Equal(4, first.TotalCount);
        Assert.Equal([false, true, true], first.Entries.Select(e => e.Completed)); // the running fight comes first
        Assert.Equal([Day1.AddDays(3), Day1.AddDays(2), Day1.AddDays(1)], first.Entries.Select(e => e.Start));

        var second = Engine.GetHistory(new HistoryQuery(Page: 2, PageSize: 3));
        Assert.Equal(4, second.TotalCount);
        Assert.Equal(2, second.Page);
        Assert.Equal([Day1], second.Entries.Select(e => e.Start));

        var beyond = Engine.GetHistory(new HistoryQuery(Page: 3, PageSize: 3));
        Assert.Empty(beyond.Entries);
        Assert.Equal(4, beyond.TotalCount);
    }

    [Fact]
    public void SavedFightCarriesTheDetailsTheLiveFightShowed()
    {
        Fight(903, SyntheticCombat.AultrossCode, Day1, 80);
        var liveAlpha = Engine.GetPlayerDetails(AlphaId)!;
        var liveBeta = Engine.GetPlayerDetails(BetaId)!;
        var livePlayers = Engine.GetSnapshot().Players;
        combat.CompleteAndSave(Day1.AddSeconds(81));

        var entry = Assert.Single(Engine.GetHistory(new HistoryQuery()).Entries);
        var fight = Assert.IsType<HistoryFight>(Engine.GetHistoryFight(entry.FightId));
        Assert.Equal(entry, fight.Entry);
        Assert.Equal(livePlayers, fight.Players);
        Assert.Equal([AlphaId, BetaId], fight.Details.Keys.Order());

        // Saved through upstream's JSON and SQLite, read back and mapped by the same code: every figure, hit
        // and time is the same as in the live view.
        Assert.Equal(Json(liveAlpha), Json(fight.Details[AlphaId]));
        Assert.Equal(Json(liveBeta), Json(fight.Details[BetaId]));
        Assert.Contains(fight.Details[BetaId].CombatLog, hit => hit.SummonName == "Player_55"); // upstream's placeholder for a summon
        Assert.NotEmpty(fight.Details[AlphaId].Buffs);
        Assert.NotEmpty(fight.Details[AlphaId].Graph.Points);
    }

    [Fact]
    public void RunningBossFightCanBeOpenedBeforeItEnds()
    {
        Fight(904, SyntheticCombat.FedivWraithCode, Day1, 30);

        var entry = Assert.Single(Engine.GetHistory(new HistoryQuery()).Entries);
        Assert.False(entry.Completed);
        var fight = Engine.GetHistoryFight(entry.FightId)!;
        Assert.False(fight.Entry.Completed);
        Assert.Equal(Json(Engine.GetPlayerDetails(AlphaId)), Json(fight.Details[AlphaId]));
    }

    [Fact]
    public void OnlyBossFightsAreSavedAndUnknownIdsFindNothing()
    {
        Save(905, mobCode: 0, Day1, 20); // not a boss: upstream does not save it

        Assert.Equal(0, Engine.GetHistory(new HistoryQuery()).TotalCount);
        Assert.Null(Engine.GetHistoryFight(Guid.NewGuid()));
    }

    [Fact]
    public void InvalidQueriesAreRejectedAtTheEngine()
    {
        Assert.Throws<ArgumentException>(() => Engine.GetHistory(new HistoryQuery(Page: 0)));
        Assert.Throws<ArgumentException>(() => Engine.GetHistory(new HistoryQuery(PageSize: 500)));
        Assert.Throws<ArgumentNullException>(() => Engine.GetHistory(null!));
    }

    // One boss fight of the given length: Alpha and Beta hit every second (Alpha critically every third second),
    // with a DoT tick of Alpha's and a hit of Beta's summon every ten seconds, and a buff on Alpha at the pull.
    private void Fight(int entityId, int mobCode, DateTime start, int seconds)
    {
        var mob = combat.Target(entityId, mobCode, hp: 1_000_000_000);
        var strike = SyntheticCombat.Skill(1101, "Strike");
        var bolt = SyntheticCombat.Skill(1601, "Bolt");
        var claw = SyntheticCombat.Skill(1001, "Claw");
        for (var s = 0; s <= seconds; s++)
        {
            var at = start.AddSeconds(s);
            combat.Hit(alpha, mob, at, 1_000 + s, strike, s % 3 == 0 ? HitFlags.Critical : HitFlags.BackAttack);
            if (s == 0) combat.Buff(AlphaId, 500, "Might", at, TimeSpan.FromSeconds(20));
            if (s % 10 == 5) combat.Hit(alpha, mob, at.AddMilliseconds(250), 200, strike, dot: true);
            combat.Hit(beta, mob, at.AddMilliseconds(500), 500, bolt);
            if (s % 10 == 5) combat.Hit(pet, mob, at.AddMilliseconds(750), 300, claw, characterClass: spirit);
        }
    }

    // Plays a fight, saves it, and returns its total damage as the live snapshot showed it.
    private long Save(int entityId, int mobCode, DateTime start, int seconds)
    {
        Fight(entityId, mobCode, start, seconds);
        var total = Engine.GetSnapshot().Players.Sum(p => p.TotalDamage);
        combat.CompleteAndSave(start.AddSeconds(seconds + 1));
        return total;
    }

    private IEnumerable<DateTime> Starts(HistoryQuery query) => Engine.GetHistory(query).Entries.Select(e => e.Start);

    private static string Json(object? value) => JsonSerializer.Serialize(value);

    public void Dispose() => host.Dispose();
}
