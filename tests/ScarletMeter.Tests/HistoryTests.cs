using System.Text.Json;
using AionDpsMeter.Core.Models;
using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.Services.Services.Session.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ScarletMeter.Engine;

namespace ScarletMeter.Tests;

/// <summary>
/// Boss-fight history through the engine: synthetic fights are completed and saved through
/// CompleteAndPersistActiveSessions (the path the demo and the engine's shutdown use), then listed, filtered,
/// paged and reloaded from upstream's real SQLite store in the test's data directory. Damaged rows are made
/// through upstream's own database context, as a damaged or edited file would hold them. The store's startup
/// cleanup deletes fights that ended before the retention period, which includes a row damaged to end near
/// DateTime.MinValue and, from 2026-11-19, every fight dated <see cref="Day1"/>: TestEngineHost hands the store
/// out only after that cleanup has finished, so it never meets these rows.
/// </summary>
public sealed class HistoryTests : IDisposable
{
    private static readonly DateTime Day1 = new(2026, 10, 20, 20, 0, 0, DateTimeKind.Local);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);
    private const int AlphaId = 7;
    private const int BetaId = 8;

    private readonly CapturingLoggerProvider log = new();
    private readonly TestEngineHost host;
    private readonly GatedHistoryStore store;
    private readonly SyntheticCombat combat;
    private readonly Player alpha;
    private readonly Player beta;
    private readonly Player pet;
    private readonly CharacterClass spirit = SyntheticCombat.Class(10, "Elementalist spirit");

    public HistoryTests()
    {
        // Upstream's real store, behind a gate that can hold a save (which upstream makes under its session lock).
        host = new TestEngineHost(CaptureMode.None, services =>
        {
            services.AddLogging(logging => logging.AddProvider(log));
            services.AddSingleton<ICombatHistoryStore>(provider =>
                new GatedHistoryStore(ActivatorUtilities.CreateInstance<CombatHistoryStore>(provider)));
        });
        store = (GatedHistoryStore)host.Services.GetRequiredService<ICombatHistoryStore>();
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
        Assert.True(page.IsComplete);
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
        Save(901, SyntheticCombat.AultrossCode, Day1, 20);

        Assert.Throws<ArgumentException>(() => Engine.GetHistory(new HistoryQuery(Page: 0)));
        Assert.Throws<ArgumentException>(() => Engine.GetHistory(new HistoryQuery(PageSize: 500)));
        Assert.Throws<ArgumentNullException>(() => Engine.GetHistory(null!));
        // Upstream would compute this page's skip in an int that wraps round, and return page 1.
        Assert.Throws<ArgumentException>(() => Engine.GetHistory(new HistoryQuery(Page: 20_000_000, PageSize: 200)));
        Assert.Empty(Engine.GetHistory(new HistoryQuery(Page: HistoryQuery.MaxPage, PageSize: HistoryQuery.MaxPageSize)).Entries);
    }

    [Fact]
    public void HitsWithNoTimeOrFarFromTheFightAreLeftOut()
    {
        var fightId = SaveAndFind(901, Day1, 60);
        // Alpha: a sound hit and an empty one (no time, no skill). Beta: a hit fifty days after the fight, which
        // would stretch the one-point-per-second graph to millions of points, a sound hit and a null.
        Damage(901, row => row.HitsByPlayerJson =
            $$"""{"7":[{{HitJson(Day1.AddSeconds(10), 1_234)}},{}],"8":[{{HitJson(Day1.AddDays(50), 5)}},{{HitJson(Day1.AddSeconds(5), 4_321)}},null]}""");

        var fight = Assert.IsType<HistoryFight>(Engine.GetHistoryFight(fightId));
        Assert.Equal([1_234L], fight.Details[AlphaId].CombatLog.Select(hit => hit.Damage));
        Assert.Equal([4_321L], fight.Details[BetaId].CombatLog.Select(hit => hit.Damage));
        Assert.Equal(2, fight.Details[BetaId].Graph.Points.Count); // one hit: seconds 0 and 1, not fifty days of points
        Assert.Contains(log.Warnings, w => w.Contains("3 damaged saved entries were left out", StringComparison.Ordinal));
    }

    [Fact]
    public void ImpossibleBuffFiguresAreBoundedOrLeftOut()
    {
        var fightId = SaveAndFind(901, Day1, 60);
        // An uptime of 1e300 seconds, a buff lasting 46 days (upstream's decoder rejects anything over an hour),
        // one applied at the end of time, and one sound buff.
        Damage(901, row =>
        {
            row.BuffStatsByPlayerJson = """{"7":[{"buffId":500,"buffName":"Might","applicationCount":1,"effectiveDurationSeconds":1e300},null]}""";
            row.BuffEventsByPlayerJson =
                $$"""{"7":[{"entityId":7,"buffId":500,"durationMs":4000000000,"appliedAt":"{{Day1:o}}"},""" +
                """{"entityId":7,"buffId":600,"durationMs":1000,"appliedAt":"9999-12-31T23:59:59"},""" +
                $$"""{"entityId":7,"buffId":700,"buffName":"Sound","durationMs":5000,"appliedAt":"{{Day1.AddSeconds(1):o}}"}]}""";
        });

        var details = Engine.GetHistoryFight(fightId)!.Details[AlphaId];
        var might = Assert.Single(details.Buffs);
        Assert.Equal(details.Duration, might.Uptime); // no longer than the fight
        Assert.Equal(100, might.UptimePercent);
        Assert.Equal([700], details.Graph.Buffs.Select(span => span.BuffId));
    }

    [Fact]
    public void AFightSavedWithAnUnusableStartOrEndShowsItsPlayersOnly()
    {
        Guid[] fights =
        [
            SaveAndFind(901, Day1, 20),
            SaveAndFind(902, Day1.AddDays(1), 20),
            SaveAndFind(903, Day1.AddDays(2), 20),
            SaveAndFind(904, Day1.AddDays(3), 20),
            SaveAndFind(905, Day1.AddDays(4), 20),
        ];
        Damage(901, row => row.SessionStart = DateTime.MinValue);                  // unset
        Damage(902, row => row.SessionEnd = row.SessionStart.AddDays(3));          // longer than any fight
        Damage(903, row => row.SessionEnd = row.SessionStart.AddMinutes(-1));      // ends before it starts
        // An hour long and in order, but so near DateTime's limits that the window's slack would overflow:
        // only the margin from those limits rejects these two.
        Damage(904, row => (row.SessionStart, row.SessionEnd) = (DateTime.MinValue.AddHours(1), DateTime.MinValue.AddHours(2)));
        Damage(905, row => (row.SessionStart, row.SessionEnd) = (DateTime.MaxValue.AddHours(-2), DateTime.MaxValue.AddHours(-1)));

        foreach (var fightId in fights)
        {
            var fight = Assert.IsType<HistoryFight>(Engine.GetHistoryFight(fightId));
            Assert.Equal(["Alpha", "Beta"], fight.Players.Select(p => p.Name).Order(StringComparer.Ordinal));
            Assert.Empty(fight.Details); // nothing in it can be timed
        }
        Assert.Equal(fights.Length, Engine.GetHistory(new HistoryQuery()).TotalCount); // all still listed
        Assert.Equal(fights.Length, log.Warnings.Count(w => w.Contains("unusable start", StringComparison.Ordinal)));
    }

    [Fact]
    public void AnUnreadableRowLeavesTheRestOfTheHistoryReadable()
    {
        var first = SaveAndFind(901, Day1, 60);
        var broken = SaveAndFind(902, Day1.AddDays(1), 60);
        SaveAndFind(903, Day1.AddDays(2), 60, SyntheticCombat.FedivWraithCode);
        Assert.Equal(1, Sql("UPDATE CombatSessions SET SessionEnd = 'not a date' WHERE TargetId = 902"));
        Assert.Throws<FormatException>(() => combat.History.GetSessionList()); // the premise: upstream's own read fails

        // Upstream's query fails on that row; the engine reads the list again without it.
        var page = Engine.GetHistory(new HistoryQuery());
        Assert.False(page.IsComplete);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal([Day1.AddDays(2), Day1], page.Entries.Select(e => e.Start));
        Assert.Equal(["Fediv Wraith", "Aultross"], page.Entries.Select(e => e.TargetName));
        Assert.All(page.Entries, entry => Assert.True(entry.Completed));
        Assert.Contains(log.Errors, e => e.Contains("could not be read as saved", StringComparison.Ordinal));
        Assert.Contains(log.Warnings, w => w.Contains("because they could not be read: 1", StringComparison.Ordinal));

        // Every later page is read the same way, so pages agree: this one would not contain the damaged row,
        // and upstream would have returned it whole yet counted the damaged row. Filters still apply.
        var second = Engine.GetHistory(new HistoryQuery(PageSize: 1, Page: 2));
        Assert.False(second.IsComplete);
        Assert.Equal(2, second.TotalCount);
        Assert.Equal([Day1], second.Entries.Select(e => e.Start));
        Assert.Equal([Day1], Starts(new HistoryQuery("  aultross ")));   // trimmed, any case, as upstream matches
        Assert.Equal([Day1.AddDays(2)], Starts(new HistoryQuery("WRAITH")));
        Assert.Empty(Starts(new HistoryQuery("Nobody")));
        Assert.Empty(Engine.GetHistory(new HistoryQuery(From: Day1.AddDays(1), To: Day1.AddDays(1).AddHours(1))).Entries);

        Assert.Null(Engine.GetHistoryFight(broken)); // logged, not thrown
        Assert.Contains(log.Errors, e => e.Contains($"Fight {broken} could not be read", StringComparison.Ordinal));
        Assert.NotNull(Engine.GetHistoryFight(first));

        // Once upstream reads every row again, the very next call is upstream's own query.
        Assert.Equal(1, Sql("UPDATE CombatSessions SET SessionEnd = '2026-10-21 20:01:00.5' WHERE TargetId = 902"));
        var healed = Engine.GetHistory(new HistoryQuery());
        Assert.True(healed.IsComplete);
        Assert.Equal(3, healed.TotalCount);
    }

    [Fact]
    public void DamagedModeEndsWhenUpstreamCanReadEveryRowAgain()
    {
        // The second audit's probe: one row upstream cannot read (text in a date column) and one it reads its own
        // way (text in a number column reads as 0). Only the first is left out, and once it is repaired upstream
        // reads every row, so the history is whole again, however the row-by-row read would have judged the second.
        SaveAndFind(901, Day1, 20);
        SaveAndFind(902, Day1.AddDays(1), 20);
        SaveAndFind(903, Day1.AddDays(2), 20);
        Assert.Equal(1, Sql("UPDATE CombatSessions SET SessionEnd = 'not a date' WHERE TargetId = 902"));
        Assert.Equal(1, Sql("UPDATE CombatSessions SET PlayerCount = 'x' WHERE TargetId = 903"));

        var damaged = Engine.GetHistory(new HistoryQuery());
        Assert.False(damaged.IsComplete);
        Assert.Equal(2, damaged.TotalCount);
        Assert.Equal(0, Assert.Single(damaged.Entries, e => e.Start == Day1.AddDays(2)).PlayerCount);

        Assert.Equal(1, Sql("UPDATE CombatSessions SET SessionEnd = '2026-10-21 20:00:20.5' WHERE TargetId = 902"));
        Assert.Equal(3, combat.History.GetSessionList().Count); // the premise: upstream reads every row again
        var healed = Engine.GetHistory(new HistoryQuery());
        Assert.True(healed.IsComplete);
        Assert.Equal(3, healed.TotalCount);
        // The rows both reads could read come out exactly as upstream reads them.
        Assert.Equal(damaged.Entries, healed.Entries.Where(e => damaged.Entries.Any(d => d.FightId == e.FightId)));
    }

    [Fact]
    public void AHistoryThatCannotBeReadAtAllIsEmptyAndIncomplete()
    {
        var fightId = SaveAndFind(901, Day1, 20);
        Sql("DROP TABLE CombatSessions");

        var page = Engine.GetHistory(new HistoryQuery());
        Assert.False(page.IsComplete);
        Assert.Empty(page.Entries);
        Assert.Equal(0, page.TotalCount);
        Assert.Null(Engine.GetHistoryFight(fightId));
        Assert.Contains(log.Errors, e => e.Contains("could not be read at all", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ASavedFightLoadsWhileUpstreamHoldsItsSessionLock()
    {
        var token = TestContext.Current.CancellationToken;
        var saved = SaveAndFind(901, Day1, 20);
        Fight(902, SyntheticCombat.FedivWraithCode, Day1.AddDays(1), 20);

        // Completing the running fight saves it under upstream's session lock; the gate holds that save, and
        // with it the lock, as a slow save would.
        store.HoldSaves();
        var completing = Task.Run(() => combat.CompleteAndSave(Day1.AddDays(1).AddSeconds(21)), token);
        Task? throughSessions = null;
        try
        {
            Assert.True(store.SaveEntered.Wait(Patience, token));

            // The premise: a read through upstream's session manager waits for the lock.
            throughSessions = Task.Run(() => combat.Sessions.GetHistorySession(saved), token);
            var first = await Task.WhenAny(throughSessions, Task.Delay(TimeSpan.FromMilliseconds(300), token));
            Assert.NotSame(throughSessions, first);

            // The engine reads a saved fight from the store, so it does not wait.
            var loaded = await Task.Run(() => Engine.GetHistoryFight(saved), token).WaitAsync(Patience, token);
            Assert.Equal(saved, loaded?.Entry.FightId);
        }
        finally
        {
            // Both waiting tasks finish before the host, and its database, go away.
            store.ReleaseSaves();
            await completing.WaitAsync(Patience, token);
            if (throughSessions is not null) await throughSessions.WaitAsync(Patience, token);
        }
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

    // Saves a boss fight (Aultross unless told otherwise) on the given entity and returns its id.
    private Guid SaveAndFind(int entityId, DateTime start, int seconds, int mobCode = SyntheticCombat.AultrossCode)
    {
        Save(entityId, mobCode, start, seconds);
        using var db = Database();
        return db.Sessions.Single(row => row.TargetId == entityId).SessionId;
    }

    // Changes a saved row through upstream's own context, as a damaged or edited database would hold it. JSON
    // written here is left uncompressed, which upstream's store reads as is.
    private void Damage(int entityId, Action<CombatSessionEntity> change)
    {
        using var db = Database();
        change(db.Sessions.Single(row => row.TargetId == entityId));
        db.SaveChanges();
    }

    // Runs SQL the entity cannot express, such as text in a date column; returns the rows it changed.
    private int Sql(string sql)
    {
        using var db = Database();
        return db.Database.ExecuteSqlRaw(sql);
    }

    private CombatHistoryDbContext Database() =>
        host.Services.GetRequiredService<IDbContextFactory<CombatHistoryDbContext>>().CreateDbContext();

    // A saved hit as upstream's JSON holds it, with the fields the details read.
    private static string HitJson(DateTime at, long damage) =>
        $$$"""{"dateTime":"{{{at:o}}}","damage":{{{damage}}},"skill":{"id":1101,"name":"Strike"}}""";

    private IEnumerable<DateTime> Starts(HistoryQuery query) => Engine.GetHistory(query).Entries.Select(e => e.Start);

    private static string Json(object? value) => JsonSerializer.Serialize(value);

    public void Dispose() => host.Dispose();

    /// <summary>
    /// Upstream's history store with a gate on Save, which upstream calls under its session lock when a fight
    /// completes: holding the gate holds the lock. Everything else passes straight through.
    /// </summary>
    private sealed class GatedHistoryStore(ICombatHistoryStore inner) : ICombatHistoryStore
    {
        private readonly ManualResetEventSlim open = new(initialState: true);

        public ManualResetEventSlim SaveEntered { get; } = new();

        public void HoldSaves()
        {
            SaveEntered.Reset();
            open.Reset();
        }

        public void ReleaseSaves() => open.Set();

        public void Save(HistorySessionSnapshot snapshot)
        {
            SaveEntered.Set();
            open.Wait(TimeSpan.FromSeconds(10), CancellationToken.None); // never hangs a test run for good
            inner.Save(snapshot);
        }

        public void FlushPendingSaves() => inner.FlushPendingSaves();

        public IReadOnlyList<HistorySessionListItem> GetSessionList() => inner.GetSessionList();

        public int GetSessionCount(DateTime? dateFrom, DateTime? dateTo, string? bossNameContains, IReadOnlySet<Guid>? excludeSessionIds = null) =>
            inner.GetSessionCount(dateFrom, dateTo, bossNameContains, excludeSessionIds);

        public IReadOnlyList<HistorySessionListItem> GetSessionPage(DateTime? dateFrom, DateTime? dateTo, string? bossNameContains,
            int skip, int take, IReadOnlySet<Guid>? excludeSessionIds = null) =>
            inner.GetSessionPage(dateFrom, dateTo, bossNameContains, skip, take, excludeSessionIds);

        public HistorySessionSnapshot? GetSession(Guid sessionId) => inner.GetSession(sessionId);
    }
}
