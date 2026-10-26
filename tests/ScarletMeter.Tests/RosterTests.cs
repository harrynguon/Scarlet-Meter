using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ScarletMeter.App.Formatting;
using ScarletMeter.App.Pages;
using ScarletMeter.App.Roster;
using ScarletMeter.App.Storage;
using ScarletMeter.Engine;
using ScarletMeter.Engine.Maps;
using ScarletMeter.WorldEvents;

namespace ScarletMeter.Tests;

/// <summary>
/// The character roster: its recharge rules (RechargeCalendar, RosterRules: A2Tools' Roster plugin at 7a4720b, the
/// resets from the world-event schedule), its file (RosterStore over ProtectedJsonFile, checked by RosterNormalizer),
/// what it takes from the engine (RosterService) and the rows the page shows (RosterPage.Rows). Characters and counts are
/// made up.
/// </summary>
public sealed class RosterTests : IDisposable
{
    private static readonly TimeSpan ServerOffset = EventCatalog.DefaultServerOffset;
    private static readonly RechargeCalendar Calendar = RechargeCalendar.Default(ServerOffset);
    private static readonly DateTimeOffset Monday = new(2026, 10, 26, 2, 30, 0, TimeSpan.Zero); // a Monday, 02:30 UTC
    private readonly TestDataDirectory directory = new();

    public void Dispose() => directory.Dispose();

    private static RosterCount Seen(int first, int second, DateTimeOffset at) => new(first, second, at);

    // ---------- When recharges come ----------

    // Basic energy refills every three hours on the hours that meet the Global daily reset (07:00 UTC): 01:00, 04:00,
    // 07:00 … UTC. A refill at exactly an instant has come by then.
    [Fact]
    public void OdyleRefillsComeEveryThreeHoursFromTheDailyReset()
    {
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 4, 0, 0, TimeSpan.Zero), Calendar.NextOdyleRefill(Monday));
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 7, 0, 0, TimeSpan.Zero), Calendar.NextOdyleRefill(new DateTimeOffset(2026, 10, 26, 4, 0, 0, TimeSpan.Zero)));
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 1, 0, 0, TimeSpan.Zero), Calendar.NextOdyleRefill(new DateTimeOffset(2026, 10, 26, 22, 0, 0, TimeSpan.Zero)));
        Assert.Equal(0, Calendar.OdyleRefillsBetween(Monday, Monday.AddMinutes(89)));
        Assert.Equal(1, Calendar.OdyleRefillsBetween(Monday, Monday.AddMinutes(90)));   // 04:00 itself
        Assert.Equal(8, Calendar.OdyleRefillsBetween(Monday, Monday.AddDays(1)));
        Assert.Equal(0, Calendar.OdyleRefillsBetween(Monday, Monday.AddHours(-5)));      // a clock set back
        // Before the grid's origin as well (floor, not truncation toward zero).
        var early = new DateTimeOffset(2025, 3, 1, 2, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2025, 3, 1, 4, 0, 0, TimeSpan.Zero), Calendar.NextOdyleRefill(early));
    }

    // Daily resets at 07:00 UTC, the weekly on Wednesdays at the same time (the Global preset), counted in (from, to].
    [Fact]
    public void ResetsAreTheWorldEventSchedules()
    {
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 7, 0, 0, TimeSpan.Zero), Calendar.NextDailyReset(Monday));
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 7, 0, 0, TimeSpan.Zero), Calendar.NextWeeklyReset(Monday));
        Assert.Equal(0, Calendar.DailyResetsBetween(Monday, Monday.AddHours(4), 10));
        Assert.Equal(1, Calendar.DailyResetsBetween(Monday, Monday.AddHours(4.5), 10));
        Assert.Equal(3, Calendar.DailyResetsBetween(Monday, Monday.AddDays(3), 10));
        Assert.Equal(2, Calendar.DailyResetsBetween(Monday, Monday.AddDays(30), 2)); // stops once enough
        Assert.Equal(0, Calendar.WeeklyResetsBetween(Monday, Monday.AddDays(2), 5));
        Assert.Equal(1, Calendar.WeeklyResetsBetween(Monday, Monday.AddDays(2.2), 5));
    }

    // The Abyss Siege on the world-event schedule: Mondays, Thursdays and Saturdays at 21:00 server time, 12:00 UTC at the
    // default UTC+9; a siege at an instant is the last one then.
    [Fact]
    public void SiegesAreTheWorldEventSchedules()
    {
        var saturday = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        var monday = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(saturday, Calendar.LastSiege(Monday));
        Assert.Equal(monday, Calendar.NextSiege(Monday));
        Assert.Equal(monday, Calendar.LastSiege(monday));
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero), Calendar.NextSiege(monday));
        Assert.Equal(saturday.AddHours(-1), RechargeCalendar.Default(TimeSpan.FromHours(10)).LastSiege(Monday)); // the server's clock moves it
    }

    // Corridor entries count from the last siege: one just before it does not.
    [Fact]
    public void CorridorRunsCountFromTheLastSiege()
    {
        var saturday = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset[] runs = [saturday.AddHours(-1), saturday, saturday.AddHours(1), Monday.AddHours(-1)];
        Assert.Equal(2, RosterRules.CorridorRunsSinceSiege(runs, Monday, Calendar));
        Assert.Equal(0, RosterRules.CorridorRunsSinceSiege(runs, Monday.AddDays(1), Calendar)); // Monday's siege since
        Assert.Equal(0, RosterRules.CorridorRunsSinceSiege([], Monday, Calendar));
    }

    // ---------- Estimates ----------

    [Fact]
    public void OdyleEnergyGrowsByFifteenEachRefillUpToItsCap()
    {
        var seen = Seen(500, 1200, Monday);
        Assert.Equal(new Estimate(500, 0, false), RosterRules.OdyleNow(seen, 560, Monday.AddMinutes(30), Calendar));
        Assert.Equal(new Estimate(515, 15, false), RosterRules.OdyleNow(seen, 560, Monday.AddMinutes(90), Calendar));
        Assert.Equal(new Estimate(560, 60, true), RosterRules.OdyleNow(seen, 560, Monday.AddDays(2), Calendar));
        Assert.Equal(new Estimate(620, 120, false), RosterRules.OdyleNow(seen, 840, Monday.AddDays(1), Calendar));
        // Already past the cap (a membership that ran out): it stays as it was until spent.
        Assert.Equal(new Estimate(700, 0, true), RosterRules.OdyleNow(Seen(700, 0, Monday), 560, Monday.AddDays(5), Calendar));
        // Years later it does not overflow.
        Assert.Equal(new Estimate(840, 840, true), RosterRules.OdyleNow(Seen(0, 0, Monday), 840, Monday.AddYears(50), Calendar));
    }

    // Full when the refills needed have come: 500 to 560 needs four, the first at 04:00, the fourth nine hours later.
    [Fact]
    public void OdyleEnergyIsFullAfterTheRefillsItNeeds()
    {
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 13, 0, 0, TimeSpan.Zero), RosterRules.OdyleFullAt(Seen(500, 0, Monday), 560, Calendar));
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 4, 0, 0, TimeSpan.Zero), RosterRules.OdyleFullAt(Seen(559, 0, Monday), 560, Calendar));
        Assert.Null(RosterRules.OdyleFullAt(Seen(560, 0, Monday), 560, Calendar));
        Assert.Null(RosterRules.OdyleFullAt(Seen(600, 0, Monday), 560, Calendar));
        // The estimate agrees: full at that instant, not a minute before.
        var full = RosterRules.OdyleFullAt(Seen(500, 0, Monday), 560, Calendar)!.Value;
        Assert.False(RosterRules.OdyleNow(Seen(500, 0, Monday), 560, full.AddMinutes(-1), Calendar).Full);
        Assert.True(RosterRules.OdyleNow(Seen(500, 0, Monday), 560, full, Calendar).Full);
    }

    [Fact]
    public void TheCapIsThePlayersChoiceOrSeenFromAMembership()
    {
        var character = new RosterCharacter { Name = "Alpha" };
        Assert.Equal(560, RosterRules.OdyleCap(character));
        Assert.Equal(840, RosterRules.OdyleCap(character with { MembershipSeen = true }));
        Assert.Equal(560, RosterRules.OdyleCap(character with { MembershipSeen = true, OdyleCap = OdyleCapChoice.Standard }));
        Assert.Equal(840, RosterRules.OdyleCap(character with { OdyleCap = OdyleCapChoice.Membership }));
    }

    // Nightmare +2 at each daily reset up to 14; Ascension Trial +3 at the weekly up to 3. The extra from items is apart.
    [Fact]
    public void ContentsRechargeAtTheirResets()
    {
        Assert.Equal(new Estimate(10, 0, false), RosterRules.ContentNow(RosterRules.Nightmare, Seen(10, 1, Monday), Monday.AddHours(4), Calendar));
        Assert.Equal(new Estimate(12, 2, false), RosterRules.ContentNow(RosterRules.Nightmare, Seen(10, 1, Monday), Monday.AddHours(5), Calendar));
        Assert.Equal(new Estimate(14, 4, true), RosterRules.ContentNow(RosterRules.Nightmare, Seen(10, 1, Monday), Monday.AddDays(9), Calendar));
        Assert.Equal(new Estimate(0, 0, false), RosterRules.ContentNow(RosterRules.Ascension, Seen(0, 0, Monday), Monday.AddDays(2), Calendar));
        Assert.Equal(new Estimate(3, 3, true), RosterRules.ContentNow(RosterRules.Ascension, Seen(0, 0, Monday), Monday.AddDays(3), Calendar));
        Assert.Equal(new Estimate(16, 0, true), RosterRules.ContentNow(RosterRules.Nightmare, Seen(16, 0, Monday), Monday.AddDays(1), Calendar));
    }

    // ---------- What the page shows ----------

    [Fact]
    public void TheRowsAreSortedAsChosenAndMarkThePlayedCharacter()
    {
        var at = Monday;
        RosterCharacter[] characters =
        [
            new() { Name = "Alpha", Server = "SIE", LastSeen = at.AddHours(-1), Odyle = Seen(100, 0, at) },
            new() { Name = "beta", Server = "SIE", LastSeen = at.AddHours(-3), Odyle = Seen(560, 0, at) },
            new() { Name = "Gamma", Server = "TRI", LastSeen = at, Odyle = Seen(300, 50, at) },
            new() { Name = "Delta", Server = "TRI", LastSeen = at.AddHours(-2) },
        ];
        var current = new RosterKey("TRI", "Gamma");
        string[] Names(RosterSort sort) => [.. RosterPage.Rows(characters, current, at, Calendar, sort).Select(r => r.Character.Name)];

        Assert.Equal(["beta", "Gamma", "Alpha", "Delta"], Names(RosterSort.MostOdyle));
        Assert.Equal(["beta", "Gamma", "Alpha", "Delta"], Names(RosterSort.FullSoonest)); // the full one first, then soonest, none last
        Assert.Equal(["Alpha", "beta", "Delta", "Gamma"], Names(RosterSort.Name));
        Assert.Equal(["Gamma", "Alpha", "Delta", "beta"], Names(RosterSort.LastSeen));
        Assert.True(Assert.Single(RosterPage.Rows(characters, current, at, Calendar, RosterSort.Name), r => r.IsCurrent).Character.Name == "Gamma");
        var delta = RosterPage.Rows(characters, current, at, Calendar, RosterSort.Name).Single(r => r.Character.Name == "Delta");
        Assert.Null(delta.Odyle);
        Assert.Null(delta.FullAt);
    }

    [Fact]
    public void TheWordingMarksEstimatesAndRoundsTimeUp()
    {
        Assert.Equal("≈ 735", RosterText.Count(new Estimate(735, 15, false)));
        Assert.Equal("1,200", RosterText.Count(new Estimate(1200, 0, false)));
        Assert.Equal("(+1,200)", RosterText.Extra(1200, always: true));
        Assert.Equal("(+0)", RosterText.Extra(0, always: true));
        Assert.Equal(string.Empty, RosterText.Extra(0, always: false));
        Assert.Equal("4d 15h", RosterText.Until(new TimeSpan(4, 15, 16, 0)));
        Assert.Equal("12h 16m", RosterText.Until(new TimeSpan(12, 15, 1)));
        Assert.Equal("1m", RosterText.Until(TimeSpan.FromSeconds(1)));
        Assert.Equal("now", RosterText.Until(TimeSpan.Zero));
        Assert.Equal("SIE · Gladiator", RosterText.ServerAndClass(new RosterCharacter { Name = "A", Server = "SIE", ClassName = "Gladiator" }));
        Assert.Equal("Gladiator", RosterText.ServerAndClass(new RosterCharacter { Name = "A", ClassName = "Gladiator" }));
    }

    // ---------- The file ----------

    private RosterStore NewStore(bool demo = false, ILogger<RosterStore>? log = null)
    {
        Directory.CreateDirectory(directory.FullPath);
        return new RosterStore(directory.FullPath, demo, log ?? NullLogger<RosterStore>.Instance);
    }

    private static RosterCharacter Alpha(DateTimeOffset at) => new()
    {
        Name = "Alpha",
        Server = "SIE",
        ClassId = 16,
        ClassName = "Spiritmaster",
        CombatPower = 41_000,
        LastSeen = at,
        Odyle = Seen(735, 1200, at),
        Nightmare = Seen(11, 1, at),
        Ascension = Seen(3, 0, at),
        OdyleCap = OdyleCapChoice.Membership,
        MembershipSeen = true,
    };

    [Fact]
    public void TheRosterRoundTripsAndKeepsThePreviousSave()
    {
        var store = NewStore();
        Assert.Equal(StoredFileStatus.Defaults, store.Load(Monday).Status);
        var roster = new RosterFile { Characters = [Alpha(Monday)] };
        store.Save(roster);
        store.Save(roster with { Characters = [Alpha(Monday.AddHours(1))] });

        var loaded = store.Load(Monday.AddHours(2));
        Assert.Equal(StoredFileStatus.Loaded, loaded.Status);
        Assert.True(loaded.CanSave);
        Assert.Null(loaded.Notice);
        Assert.Equal([Alpha(Monday.AddHours(1))], loaded.File.Characters);
        Assert.True(File.Exists(store.BackupPath));
        Assert.EndsWith("roster.json", store.FilePath, StringComparison.Ordinal);
        Assert.EndsWith("demo-roster.json", NewStore(demo: true).FilePath, StringComparison.Ordinal);
        Assert.Contains("\"odyleCap\": \"membership\"", File.ReadAllText(store.FilePath), StringComparison.Ordinal);
        Assert.DoesNotContain("\"key\"", File.ReadAllText(store.FilePath), StringComparison.Ordinal);
    }

    // A damaged roster is kept aside, never thrown away; the previous save is used; the notice never names a character.
    [Fact]
    public void AnUnreadableRosterIsKeptAsideAndThePreviousSaveRestored()
    {
        var store = NewStore();
        store.Save(new RosterFile { Characters = [Alpha(Monday)] });
        store.Save(new RosterFile { Characters = [Alpha(Monday)] });
        File.WriteAllText(store.FilePath, "{ not json");

        var loaded = store.Load(Monday);
        Assert.Equal(StoredFileStatus.RecoveredFromBackup, loaded.Status);
        Assert.Equal([Alpha(Monday)], loaded.File.Characters);
        Assert.Contains("previous save was restored", loaded.Notice, StringComparison.Ordinal);
        Assert.Single(Directory.GetFiles(directory.FullPath, "roster.unreadable-*.json"));
    }

    // One from a newer build is shown but never overwritten.
    [Fact]
    public void ARosterFromANewerBuildIsReadOnly()
    {
        var store = NewStore();
        File.WriteAllText(store.FilePath, "{ \"version\": 99, \"characters\": [ { \"name\": \"Alpha\", \"server\": \"SIE\", \"lastSeen\": \"2026-10-26T01:00:00+00:00\" } ] }");
        var loaded = store.Load(Monday);
        Assert.Equal(StoredFileStatus.ReadOnly, loaded.Status);
        Assert.False(loaded.CanSave);
        Assert.Equal("Alpha", Assert.Single(loaded.File.Characters).Name);
        Assert.Contains("newer version", loaded.Notice, StringComparison.Ordinal);
    }

    // A damaged roster another program holds open cannot be set aside, so saving is off: the next save would replace it.
    [Fact]
    public void ADamagedRosterThatCannotBeSetAsideIsNeverOverwritten()
    {
        var store = NewStore();
        File.WriteAllText(store.FilePath, "{ not json");
        using (new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var loaded = store.Load(Monday);
            Assert.Equal(StoredFileStatus.ReadOnly, loaded.Status);
            Assert.False(loaded.CanSave);
            Assert.Contains("could not be read or set aside", loaded.Notice, StringComparison.Ordinal);
        }
        Assert.Equal("{ not json", File.ReadAllText(store.FilePath));
        Assert.Empty(Directory.GetFiles(directory.FullPath, "roster.unreadable-*.json"));
    }

    // With no roster file, a backup another program holds open cannot be read or protected, so saving is off: the next
    // save's File.Replace would overwrite it.
    [Fact]
    public void WithoutTheFileABackupThatCannotBeOpenedIsNeverOverwritten()
    {
        var store = NewStore();
        File.WriteAllText(store.BackupPath, "{ \"version\": 1, \"characters\": [] }");
        using (new FileStream(store.BackupPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var loaded = store.Load(Monday);
            Assert.Equal(StoredFileStatus.ReadOnly, loaded.Status);
            Assert.False(loaded.CanSave);
            Assert.Contains("could not be opened", loaded.Notice, StringComparison.Ordinal);
        }
    }

    // A cap this build does not know (a later build's) or a number is reset to automatic and said, not taken as a damaged
    // file that is set aside and the roster started over (deep audit on #48).
    [Theory]
    [InlineData("\"lifetime\"")]
    [InlineData("2")]
    [InlineData("\"-1\"")]
    public void AnUnknownCapIsRepairedNotTakenAsDamage(string cap)
    {
        var store = NewStore();
        File.WriteAllText(store.FilePath, "{ \"version\": 1, \"characters\": [ { \"name\": \"Alpha\", \"server\": \"SIE\", \"lastSeen\": \"2026-10-26T01:00:00+00:00\", \"odyleCap\": " + cap + " } ] }");
        var loaded = store.Load(Monday);
        Assert.Equal(StoredFileStatus.Loaded, loaded.Status);
        Assert.Equal(OdyleCapChoice.Automatic, Assert.Single(loaded.File.Characters).OdyleCap);
        Assert.Contains("repaired", loaded.Notice, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(directory.FullPath, "roster.unreadable-*.json"));
        Assert.Equal(OdyleCapChoice.Standard, Assert.Single(RosterLoad("\"STANDARD\"").File.Characters).OdyleCap); // names ignore case
    }

    private RosterLoadResult RosterLoad(string cap)
    {
        var store = NewStore();
        File.WriteAllText(store.FilePath, "{ \"version\": 1, \"characters\": [ { \"name\": \"Alpha\", \"server\": \"SIE\", \"lastSeen\": \"2026-10-26T01:00:00+00:00\", \"odyleCap\": " + cap + " } ] }");
        return store.Load(Monday);
    }

    // A file saved with a byte-order mark (Windows PowerShell, older Notepad) still loads.
    [Fact]
    public void ARosterSavedWithAByteOrderMarkLoads()
    {
        var store = NewStore();
        File.WriteAllText(store.FilePath, "{ \"version\": 1, \"characters\": [] }", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        Assert.Equal(StoredFileStatus.Loaded, store.Load(Monday).Status);
    }

    // What cannot be used goes, what can be repaired is, and the repairs never name a character (they are logged).
    [Fact]
    public void TheNormalizerDropsAndRepairsWithoutNamingAnyone()
    {
        var now = Monday;
        RosterCharacter[] saved =
        [
            Alpha(now),
            Alpha(now.AddHours(-5)) with { CombatPower = 1 },                     // listed twice: the latest kept
            new() { Name = "", Server = "SIE", LastSeen = now },                  // no name
            new() { Name = "Bad\u0007Name", Server = "SIE", LastSeen = now },     // a control character
            new() { Name = "Old", Server = "SIE", LastSeen = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero) }, // no usable date
            new() { Name = "Future", Server = "SIE", LastSeen = now.AddDays(3) },  // a date past tomorrow
            new() { Name = "Dated", Server = "TRI", Odyle = Seen(10, 0, now.AddHours(-1)) }, // no date of its own: its count's
            new() { Name = "Odd", Server = "TRI", LastSeen = now, ClassId = 500, CombatPower = -4, OdyleCap = (OdyleCapChoice)9,
                Odyle = Seen(200_000, 0, now), Nightmare = Seen(3, 0, now.AddDays(9)), Ascension = Seen(1, 0, now) },
        ];
        var (file, repairs) = RosterNormalizer.Normalize(new RosterFile { Characters = saved }, now);

        Assert.Equal(["Alpha", "Dated", "Odd"], file.Characters.Select(c => c.Name).Order(StringComparer.Ordinal));
        Assert.Equal(41_000, file.Characters.Single(c => c.Name == "Alpha").CombatPower);
        Assert.Equal(now.AddHours(-1), file.Characters.Single(c => c.Name == "Dated").LastSeen);
        var odd = file.Characters.Single(c => c.Name == "Odd");
        Assert.Equal((0, 0, OdyleCapChoice.Automatic), (odd.ClassId, odd.CombatPower, odd.OdyleCap));
        Assert.Null(odd.Odyle);
        Assert.Null(odd.Nightmare);
        Assert.Equal(1, odd.Ascension?.First);
        Assert.NotEmpty(repairs);
        Assert.All(repairs, repair =>
        {
            foreach (var name in new[] { "Alpha", "Bad", "Old", "Future", "Dated", "Odd", "SIE", "TRI" })
                Assert.DoesNotContain(name, repair, StringComparison.Ordinal);
        });
        Assert.Empty(RosterNormalizer.Normalize(file, now).Repairs); // a clean roster needs nothing
        Assert.Empty(RosterNormalizer.Normalize(null, now).File.Characters);
    }

    [Fact]
    public void TheNormalizerKeepsTheMostRecentlySeenWhenThereAreTooMany()
    {
        var characters = Enumerable.Range(0, RosterNormalizer.MaxCharacters + 5)
            .Select(i => new RosterCharacter { Name = $"C{i}", Server = "SIE", LastSeen = Monday.AddMinutes(-i) })
            .ToList();
        var (file, repairs) = RosterNormalizer.Normalize(new RosterFile { Characters = characters }, Monday);
        Assert.Equal(RosterNormalizer.MaxCharacters, file.Characters.Count);
        Assert.DoesNotContain(file.Characters, c => c.Name == $"C{RosterNormalizer.MaxCharacters}");
        Assert.Single(repairs);
    }

    // ---------- What the service takes ----------

    private sealed class FakeEngine : ICombatEngine
    {
        public event EventHandler<CharacterProgress>? ProgressObserved;
        public event EventHandler<int?>? MapChanged;
        public int Subscribers => ProgressObserved?.GetInvocationList().Length ?? 0;
        public int MapSubscribers => MapChanged?.GetInvocationList().Length ?? 0;
        public void Raise(CharacterProgress progress) => ProgressObserved?.Invoke(this, progress);
        public void RaiseMap(int? mapId) => MapChanged?.Invoke(this, mapId);
        public CharacterIdentity? Character { get; set; }
        public OdyleEnergy? OdyleEnergy => null;
        public int? MapId => null;
        public bool PausedInAbyss => false;
        public CaptureState Capture => new(false, null);
        public event EventHandler<int>? PingUpdated { add { } remove { } }
        public event EventHandler<OdyleEnergy>? OdyleEnergyUpdated { add { } remove { } }
        public void Start() { }
        public void Stop() { }
        public void Reset() { }
        public CombatSnapshot GetSnapshot() => CombatSnapshot.Empty;
        public PlayerDetails? GetPlayerDetails(long playerId) => null;
        public HistoryPage GetHistory(HistoryQuery query) => throw new NotSupportedException();
        public HistoryFight? GetHistoryFight(Guid fightId) => null;
        public void Dispose() { }
    }

    private static readonly CharacterIdentity AlphaIdentity = new("Alpha", "SIE", 16, "Spiritmaster", 41_000);

    private static CharacterProgress Progress(CharacterIdentity who, params (uint Id, int First, int Second, DateTimeOffset At)[] counts) =>
        new(who, counts.ToDictionary(c => c.Id, c => new EntryCount(c.First, c.Second, c.At)));

    // A report becomes the character's row; the roster is saved a moment after the last change, not on each.
    [Fact]
    public void AReportIsKeptAndSavedAMomentAfterTheLastChange()
    {
        var clock = new FakeTimeProvider(Monday);
        var engine = new FakeEngine();
        var store = NewStore();
        using var service = new RosterService(engine, store, clock, NullLogger<RosterService>.Instance);
        service.Start();
        service.Start();
        Assert.Equal((1, 1), (engine.Subscribers, engine.MapSubscribers));

        engine.Raise(Progress(AlphaIdentity, (EntryIds.OdyleEnergy, 600, 1200, Monday), (EntryIds.Nightmare, 11, 1, Monday), (99, 5, 5, Monday)));
        var alpha = Assert.Single(service.Characters);
        Assert.Equal(("Alpha", "SIE", 16, "Spiritmaster", 41_000), (alpha.Name, alpha.Server, alpha.ClassId, alpha.ClassName, alpha.CombatPower));
        Assert.Equal(Seen(600, 1200, Monday), alpha.Odyle);
        Assert.Equal(Seen(11, 1, Monday), alpha.Nightmare);
        Assert.Null(alpha.Ascension);
        Assert.True(alpha.MembershipSeen); // more than 560 basic
        Assert.Equal(new RosterKey("SIE", "Alpha"), service.Current);

        Assert.False(File.Exists(store.FilePath));
        clock.Advance(RosterService.SaveDelay - TimeSpan.FromMilliseconds(1));
        Assert.False(File.Exists(store.FilePath));
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal([alpha], store.Load(Monday).File.Characters);
    }

    // A count replaces the kept one only when it was read later; an identity without a class or power keeps the kept ones.
    [Fact]
    public void AnOlderReportNeverUndoesANewerOne()
    {
        var clock = new FakeTimeProvider(Monday.AddMinutes(10)); // both reads in the past, as the tap's always are
        using var service = new RosterService(new FakeEngine(), NewStore(), clock, NullLogger<RosterService>.Instance);
        service.Take(Progress(AlphaIdentity, (EntryIds.OdyleEnergy, 300, 0, Monday.AddMinutes(5))));
        service.Take(Progress(AlphaIdentity with { ClassId = 0, ClassName = "", CombatPower = 0 },
            (EntryIds.OdyleEnergy, 310, 0, Monday), (EntryIds.AscensionTrial, 2, 0, Monday)));
        var alpha = Assert.Single(service.Characters);
        Assert.Equal(Seen(300, 0, Monday.AddMinutes(5)), alpha.Odyle);
        Assert.Equal(Seen(2, 0, Monday), alpha.Ascension);
        Assert.Equal((16, "Spiritmaster", 41_000), (alpha.ClassId, alpha.ClassName, alpha.CombatPower));
        Assert.False(alpha.MembershipSeen);
    }

    // A kept count dated after now (a clock set back since) is replaced by the next read, or nothing would ever replace it.
    [Fact]
    public void ACountDatedAfterNowIsReplacedByTheNextRead()
    {
        var clock = new FakeTimeProvider(Monday);
        using var service = new RosterService(new FakeEngine(), NewStore(), clock, NullLogger<RosterService>.Instance);
        service.Take(Progress(AlphaIdentity, (EntryIds.OdyleEnergy, 100, 0, Monday.AddHours(12))));
        service.Take(Progress(AlphaIdentity, (EntryIds.OdyleEnergy, 400, 0, Monday)));
        var alpha = Assert.Single(service.Characters);
        Assert.Equal(Seen(400, 0, Monday), alpha.Odyle);
        Assert.Equal(Monday, alpha.LastSeen);
    }

    // The player's choices: a removal (the character comes back when played) and the cap; both saved.
    [Fact]
    public void RemovingAndTheCapAreSaved()
    {
        var clock = new FakeTimeProvider(Monday);
        var store = NewStore();
        var changes = 0;
        using (var service = new RosterService(new FakeEngine(), store, clock, NullLogger<RosterService>.Instance))
        {
            service.Changed += (_, _) => changes++;
            service.Take(Progress(AlphaIdentity));
            service.Take(Progress(AlphaIdentity with { Name = "Beta" }));
            service.SetOdyleCap(new RosterKey("SIE", "Alpha"), OdyleCapChoice.Standard);
            service.SetOdyleCap(new RosterKey("SIE", "Alpha"), OdyleCapChoice.Standard); // no change
            service.Remove(new RosterKey("SIE", "Beta"));
            service.Remove(new RosterKey("SIE", "Nobody"));
            Assert.Equal(4, changes);
            Assert.Throws<ArgumentOutOfRangeException>(() => service.SetOdyleCap(new RosterKey("SIE", "Alpha"), (OdyleCapChoice)7));
        } // disposal saves what changed
        var saved = Assert.Single(store.Load(Monday).File.Characters);
        Assert.Equal(("Alpha", OdyleCapChoice.Standard), (saved.Name, saved.OdyleCap));
    }

    // A roster that must not be overwritten is never saved, whatever changes.
    [Fact]
    public void ARosterThatCannotBeSavedIsLeftAlone()
    {
        var store = NewStore();
        const string newer = "{ \"version\": 99, \"characters\": [] }";
        File.WriteAllText(store.FilePath, newer);
        var clock = new FakeTimeProvider(Monday);
        using (var service = new RosterService(new FakeEngine(), store, clock, NullLogger<RosterService>.Instance))
        {
            Assert.False(service.CanSave);
            Assert.NotNull(service.Notice);
            service.Take(Progress(AlphaIdentity, (EntryIds.OdyleEnergy, 300, 0, Monday)));
            clock.Advance(TimeSpan.FromMinutes(1));
        }
        Assert.Equal(newer, File.ReadAllText(store.FilePath));
    }

    // A failed save is logged once, the roster kept, and saved with the next change once the file is free.
    [Fact]
    public void AFailedSaveIsLoggedOnceAndTriedAgain()
    {
        var store = NewStore();
        var clock = new FakeTimeProvider(Monday);
        var log = new RecordingLogger<RosterService>();
        using var service = new RosterService(new FakeEngine(), store, clock, log);
        using (new FileStream(store.FilePath + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            service.Take(Progress(AlphaIdentity));
            clock.Advance(RosterService.SaveDelay);
            service.Take(Progress(AlphaIdentity with { Name = "Beta" }));
            clock.Advance(RosterService.SaveDelay);
        }
        Assert.Single(log.Entries, e => e.Message.Contains("could not be saved", StringComparison.Ordinal));
        service.Take(Progress(AlphaIdentity));
        clock.Advance(RosterService.SaveDelay);
        Assert.Equal(2, store.Load(Monday).File.Characters.Count);
        Assert.Single(log.Entries, e => e.Message.Contains("works again", StringComparison.Ordinal));
        Assert.All(log.Entries, e => Assert.DoesNotContain("Alpha", e.Message, StringComparison.Ordinal));
    }

    // ---------- Artifact Corridor runs ----------

    // Entering the Artifact Corridor from another map is a run for the character being played: kept, saved and counted
    // since the last siege; a load of the corridor again (a teleport inside) or a change between other maps is none.
    [Fact]
    public void EnteringTheCorridorIsARunForTheCharacterPlayed()
    {
        var clock = new FakeTimeProvider(Monday);
        var engine = new FakeEngine { Character = AlphaIdentity };
        var store = NewStore();
        using var service = new RosterService(engine, store, clock, NullLogger<RosterService>.Instance);
        service.Start();
        engine.RaiseMap(20);
        engine.RaiseMap(MapNames.ArtifactCorridor);
        engine.RaiseMap(MapNames.ArtifactCorridor); // the engine raises a change only, but a repeat is no new run either
        clock.Advance(TimeSpan.FromMinutes(3));
        engine.RaiseMap(20);
        engine.RaiseMap(1010);
        var alpha = Assert.Single(service.Characters);
        Assert.Equal([Monday], alpha.CorridorRuns);
        Assert.Equal(1, RosterRules.CorridorRunsSinceSiege(alpha.CorridorRuns, clock.GetUtcNow(), Calendar));
        clock.Advance(RosterService.SaveDelay);
        Assert.Equal([Monday], Assert.Single(store.Load(clock.GetUtcNow()).File.Characters).CorridorRuns);
    }

    // While the engine is not sure who is playing (after a move, before a record names the character), a run is put on no
    // one, and says so without naming anyone.
    [Fact]
    public void ARunWhileTheCharacterIsNotSureIsNotCounted()
    {
        var clock = new FakeTimeProvider(Monday);
        var log = new RecordingLogger<RosterService>();
        var engine = new FakeEngine();
        using var service = new RosterService(engine, NewStore(), clock, log);
        service.TakeMap(20);
        service.TakeMap(MapNames.ArtifactCorridor);
        Assert.Empty(service.Characters);
        Assert.Single(log.Entries, e => e.Message.Contains("not sure; not counted", StringComparison.Ordinal));
    }

    // A reconnection inside the corridor loads it again after an unknown map: within five minutes of the last run that is
    // the same run; later, entering straight from an unknown map (the Abyss's load missed) is a new one.
    [Fact]
    public void AReconnectionInsideTheCorridorIsNotASecondRun()
    {
        var clock = new FakeTimeProvider(Monday);
        using var service = new RosterService(new FakeEngine { Character = AlphaIdentity }, NewStore(), clock, NullLogger<RosterService>.Instance);
        service.TakeMap(20);
        service.TakeMap(MapNames.ArtifactCorridor);
        clock.Advance(TimeSpan.FromMinutes(1));
        service.TakeMap(null);
        service.TakeMap(MapNames.ArtifactCorridor);
        Assert.Single(Assert.Single(service.Characters).CorridorRuns);

        clock.Advance(RosterService.CorridorReentryWindow);
        service.TakeMap(null);
        service.TakeMap(MapNames.ArtifactCorridor);
        Assert.Equal(2, Assert.Single(service.Characters).CorridorRuns.Count);
    }

    // The most recent runs are kept, oldest first.
    [Fact]
    public void TheMostRecentRunsAreKept()
    {
        var clock = new FakeTimeProvider(Monday);
        using var service = new RosterService(new FakeEngine { Character = AlphaIdentity }, NewStore(), clock, NullLogger<RosterService>.Instance);
        for (var run = 0; run < RosterRules.CorridorRunsKept + 3; run++)
        {
            service.TakeMap(20);
            service.TakeMap(MapNames.ArtifactCorridor);
            clock.Advance(TimeSpan.FromMinutes(10));
        }
        var runs = Assert.Single(service.Characters).CorridorRuns;
        Assert.Equal(RosterRules.CorridorRunsKept, runs.Count);
        Assert.Equal(Monday.AddMinutes(30), runs[0]);
        Assert.Equal(runs.Order(), runs);
    }

    // Saved runs are checked like every date: unusable ones go, the rest in order, the most recent kept.
    [Fact]
    public void TheNormalizerKeepsUsableCorridorRunsInOrder()
    {
        var runs = Enumerable.Range(0, RosterRules.CorridorRunsKept + 2).Select(i => Monday.AddHours(-i)).ToList();
        runs.Add(Monday.AddDays(5)); // past tomorrow
        runs.Add(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var (file, repairs) = RosterNormalizer.Normalize(new RosterFile { Characters = [new RosterCharacter { Name = "Alpha", Server = "SIE", LastSeen = Monday, CorridorRuns = runs }] }, Monday);
        var kept = Assert.Single(file.Characters).CorridorRuns;
        Assert.Equal(RosterRules.CorridorRunsKept, kept.Count);
        Assert.Equal(Monday, kept[^1]);
        Assert.Equal(kept.Order(), kept);
        Assert.NotEmpty(repairs);
        var (none, _) = RosterNormalizer.Normalize(new RosterFile { Characters = [new RosterCharacter { Name = "Alpha", Server = "SIE", LastSeen = Monday, CorridorRuns = null! }] }, Monday);
        Assert.Empty(Assert.Single(none.Characters).CorridorRuns);
    }

    // A record compares a list by reference, so the roster's own equality compares every value, the corridor runs one by
    // one: a change to any property makes two characters differ, and the same values in a new list do not.
    [Fact]
    public void TwoCharactersAreEqualOnlyWhenEveryValueIs()
    {
        var baseline = Alpha(Monday) with { CorridorRuns = [Monday] };
        Assert.Equal(baseline, baseline with { CorridorRuns = [Monday] });
        Assert.Equal(baseline.GetHashCode(), (baseline with { CorridorRuns = [Monday] }).GetHashCode());
        var clone = typeof(RosterCharacter).GetMethod("<Clone>$")!;
        foreach (var property in typeof(RosterCharacter).GetProperties().Where(p => p.CanWrite))
        {
            var copy = (RosterCharacter)clone.Invoke(baseline, null)!;
            object changed = property.GetValue(baseline) switch
            {
                string text => text + "x",
                int number => number + 1,
                bool flag => !flag,
                DateTimeOffset date => date.AddMinutes(1),
                RosterCount count => count with { First = count.First + 1 },
                OdyleCapChoice cap => cap == OdyleCapChoice.Standard ? OdyleCapChoice.Membership : OdyleCapChoice.Standard,
                IReadOnlyList<DateTimeOffset> list => (IReadOnlyList<DateTimeOffset>)[.. list, Monday.AddHours(1)],
                var other => throw new InvalidOperationException($"No change made for {property.Name} ({other?.GetType().Name})."),
            };
            property.SetValue(copy, changed);
            Assert.False(baseline.Equals(copy), property.Name);
        }
    }

    [Fact]
    public void TheRowsCountCorridorRunsSinceTheSiege()
    {
        var saturday = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        var character = new RosterCharacter { Name = "Alpha", Server = "SIE", LastSeen = Monday, CorridorRuns = [saturday.AddHours(-2), saturday.AddHours(2)] };
        Assert.Equal(1, Assert.Single(RosterPage.Rows([character], null, Monday, Calendar, RosterSort.Name)).Corridors);
    }
}