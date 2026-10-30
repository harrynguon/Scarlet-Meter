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

    // 840, a membership's, unless the player chose 560 (Harry, 2026-10-27: "make the default 'full' odyle energy 840
    // (membership) as opposed to 560").
    [Fact]
    public void TheCapIs840UnlessThePlayerChose560()
    {
        var character = new RosterCharacter { Name = "Alpha" };
        Assert.Equal(840, RosterRules.OdyleCap(character));
        Assert.Equal(560, RosterRules.OdyleCap(character with { OdyleCap = OdyleCapChoice.Standard }));
        Assert.Equal(840, RosterRules.OdyleCap(character with { OdyleCap = OdyleCapChoice.Membership }));
        Assert.Equal(OdyleCapChoice.Automatic, character.OdyleCap); // a new character's
    }

    // The cap list shows 840 for the default and for a saved choice of 840 (from before it was the default), 560 for 560. A
    // gear score or bound Kina not read yet says why when pointed at (both come at login, so a meter started mid-session
    // has neither for the character played; Harry, 2026-10-27, of a bound Kina shown as "?"); once read,
    // the gear score's tip says what it is and the bound Kina has none.
    [Fact]
    public void TheRosterPageShowsTheCapAndWhyAValueIsNotRead()
    {
        var character = new RosterCharacter { Name = "Alpha" };
        Assert.Equal(OdyleCapChoice.Automatic, RosterPage.CapShown(character));
        Assert.Equal(OdyleCapChoice.Automatic, RosterPage.CapShown(character with { OdyleCap = OdyleCapChoice.Membership }));
        Assert.Equal(OdyleCapChoice.Standard, RosterPage.CapShown(character with { OdyleCap = OdyleCapChoice.Standard }));

        Assert.EndsWith(RosterPage.NotReadYet, RosterPage.GearScoreTitle(character), StringComparison.Ordinal);
        Assert.Equal("Gear score (the equipment's item level)", RosterPage.GearScoreTitle(character with { GearScore = 2_040 }));
        Assert.EndsWith(RosterPage.NotReadYet, RosterPage.BoundKinaTitle(character), StringComparison.Ordinal);
        Assert.EndsWith(RosterPage.NotReadYet, RosterPage.BoundKinaTitle(character with { Kina = new RosterKina(2_000_135, null, Monday) }), StringComparison.Ordinal);
        Assert.Null(RosterPage.BoundKinaTitle(character with { Kina = new RosterKina(2_000_135, 0, Monday) }));
    }

    // A roster saved before 840 was the default (its membershipSeen flag, false or true) loads unrepaired, the automatic
    // cap now 840 and the player's own choices kept, and the flag is not written again.
    [Fact]
    public void ARosterFromBefore840WasTheDefaultLoadsWithIt()
    {
        var store = NewStore();
        File.WriteAllText(store.FilePath, "{ \"version\": 1, \"characters\": ["
            + " { \"name\": \"Alpha\", \"server\": \"SIE\", \"lastSeen\": \"2026-10-26T01:00:00+00:00\", \"odyleCap\": \"automatic\", \"membershipSeen\": false },"
            + " { \"name\": \"Beta\", \"server\": \"SIE\", \"lastSeen\": \"2026-10-26T01:00:00+00:00\", \"odyleCap\": \"automatic\", \"membershipSeen\": true },"
            + " { \"name\": \"Gamma\", \"server\": \"SIE\", \"lastSeen\": \"2026-10-26T01:00:00+00:00\", \"odyleCap\": \"standard\", \"membershipSeen\": true } ] }");
        var loaded = store.Load(Monday);
        Assert.Equal(StoredFileStatus.Loaded, loaded.Status);
        Assert.Null(loaded.Notice);
        Assert.Equal([840, 840, 560], loaded.File.Characters.Select(RosterRules.OdyleCap));
        store.Save(loaded.File);
        Assert.DoesNotContain("membershipSeen", File.ReadAllText(store.FilePath), StringComparison.Ordinal);
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
            new() { Name = "Alpha", Server = "SIE", LastSeen = at.AddHours(-1), Odyle = Seen(100, 0, at), CombatPower = 109_396 },
            new() { Name = "beta", Server = "SIE", LastSeen = at.AddHours(-3), Odyle = Seen(560, 0, at) },
            new() { Name = "Gamma", Server = "TRI", LastSeen = at, Odyle = Seen(300, 50, at), CombatPower = 41_000 },
            new() { Name = "Delta", Server = "TRI", LastSeen = at.AddHours(-2), CombatPower = 41_000 },
        ];
        var current = new RosterKey("TRI", "Gamma");
        string[] Names(RosterSort sort) => [.. RosterPage.Rows(characters, current, at, Calendar, sort).Select(r => r.Character.Name)];

        // Most power first (Harry, 2026-10-28), a tie by name (Gamma comes before Delta here, so the order is the sort's),
        // a power not known yet last; the same name and power on two servers, by server.
        Assert.Equal(["Alpha", "Delta", "Gamma", "beta"], Names(RosterSort.MostPower));
        RosterCharacter[] twins =
        [
            new() { Name = "Echo", Server = "TRI", LastSeen = at, CombatPower = 41_000 },
            new() { Name = "Echo", Server = "SIE", LastSeen = at, CombatPower = 41_000 },
        ];
        Assert.Equal(["SIE", "TRI"], RosterPage.Rows(twins, null, at, Calendar, RosterSort.MostPower).Select(r => r.Character.Server));
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
        // The cap straight after a count or its extra (Harry, 2026-10-30): "6 (+1)/14", "2/3".
        Assert.Equal("/14", RosterText.OutOf(14));
        Assert.Equal("/3", RosterText.OutOf(3));
        // The corridors a side can enter: as the game said, or the most there can be starred while not known.
        Assert.Equal("/2", RosterText.CorridorsOutOf(2));
        Assert.Equal("/0", RosterText.CorridorsOutOf(0));
        Assert.Equal("/3*", RosterText.CorridorsOutOf(null));
        Assert.Equal("4d 15h", RosterText.Until(new TimeSpan(4, 15, 16, 0)));
        Assert.Equal("12h 16m", RosterText.Until(new TimeSpan(12, 15, 1)));
        Assert.Equal("1m", RosterText.Until(TimeSpan.FromSeconds(1)));
        Assert.Equal("now", RosterText.Until(TimeSpan.Zero));
        // An instant on another day: its day apart from its time, for Last played's two lines; today's has no day.
        var now = new DateTimeOffset(new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Local));
        var yesterday = new DateTimeOffset(new DateTime(2026, 10, 27, 21, 32, 0, DateTimeKind.Local));
        Assert.Equal(("Tue 27 Oct", "21:32", "Tue 27 Oct 21:32"), (RosterText.Day(yesterday, now), RosterText.Time(yesterday), RosterText.At(yesterday, now)));
        var earlierToday = now.AddMinutes(-175);
        Assert.Equal((string.Empty, "09:05", "09:05"), (RosterText.Day(earlierToday, now), RosterText.Time(earlierToday), RosterText.At(earlierToday, now)));
        Assert.Equal("SIE · Gladiator", RosterText.ServerClassAndLevel(new RosterCharacter { Name = "A", Server = "SIE", ClassName = "Gladiator" }));
        Assert.Equal("Gladiator", RosterText.ServerClassAndLevel(new RosterCharacter { Name = "A", ClassName = "Gladiator" }));
        Assert.Equal("SIE · Sorcerer · Lv 45", RosterText.ServerClassAndLevel(new RosterCharacter { Name = "A", Server = "SIE", ClassName = "Sorcerer", Level = 45 }));
        Assert.Equal("Lv 9", RosterText.ServerClassAndLevel(new RosterCharacter { Name = "A", Level = 9 }));
        Assert.Equal("41,000", RosterText.KnownNumber(41_000));
        Assert.Equal("?", RosterText.KnownNumber(0));
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
        Kina = new RosterKina(1_234_567, 33_940, at),
        Note = "Main · healer",
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
    [InlineData("null")]
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
                Level = 100, GearScore = 100_001,
                Odyle = Seen(200_000, 0, now), Nightmare = Seen(3, 0, now.AddDays(9)), Ascension = Seen(1, 0, now) },
            new() { Name = "Below", Server = "TRI", LastSeen = now, Level = -1, GearScore = -1 },
            new() { Name = "Edge", Server = "TRI", LastSeen = now, Level = 99, GearScore = 100_000 },
        ];
        var (file, repairs) = RosterNormalizer.Normalize(new RosterFile { Characters = saved }, now);

        Assert.Equal(["Alpha", "Below", "Dated", "Edge", "Odd"], file.Characters.Select(c => c.Name).Order(StringComparer.Ordinal));
        Assert.Equal(41_000, file.Characters.Single(c => c.Name == "Alpha").CombatPower);
        Assert.Equal(now.AddHours(-1), file.Characters.Single(c => c.Name == "Dated").LastSeen);
        var odd = file.Characters.Single(c => c.Name == "Odd");
        Assert.Equal((0, 0, OdyleCapChoice.Automatic), (odd.ClassId, odd.CombatPower, odd.OdyleCap));
        Assert.Equal((0, 0), (odd.Level, odd.GearScore)); // past the bounds
        var below = file.Characters.Single(c => c.Name == "Below");
        Assert.Equal((0, 0), (below.Level, below.GearScore));
        var edge = file.Characters.Single(c => c.Name == "Edge");
        Assert.Equal((99, 100_000), (edge.Level, edge.GearScore)); // the bounds kept
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
        public event EventHandler<AbyssArtifacts>? ArtifactsObserved;
        public event EventHandler? ConnectionStarted;
        public void RaiseConnection() => ConnectionStarted?.Invoke(this, EventArgs.Empty);
        public int ConnectionSubscribers => ConnectionStarted?.GetInvocationList().Length ?? 0;
        public int Subscribers => ProgressObserved?.GetInvocationList().Length ?? 0;
        public int MapSubscribers => MapChanged?.GetInvocationList().Length ?? 0;
        public void Raise(CharacterProgress progress) => ProgressObserved?.Invoke(this, progress);
        public void RaiseMap(int? mapId) => MapChanged?.Invoke(this, mapId);
        public void RaiseArtifacts(AbyssArtifacts artifacts) => ArtifactsObserved?.Invoke(this, artifacts);
        public int ArtifactSubscribers => ArtifactsObserved?.GetInvocationList().Length ?? 0;
        public CharacterIdentity? Character { get; set; }
        public OdyleEnergy? OdyleEnergy => null;
        public ShugoKeys? ShugoKeys => null;
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
        Assert.Equal(OdyleCapChoice.Automatic, alpha.OdyleCap); // 840 by default, whatever the game sends
        Assert.Equal(new RosterKey("SIE", "Alpha"), service.Current);

        Assert.False(File.Exists(store.FilePath));
        clock.Advance(RosterService.SaveDelay - TimeSpan.FromMilliseconds(1));
        Assert.False(File.Exists(store.FilePath));
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal([alpha], store.Load(Monday).File.Characters);
    }

    // A count replaces the kept one only when it was read later; an identity without a class, power, level or gear score
    // keeps the kept ones, and one with them replaces them (a level gained, new gear).
    [Fact]
    public void AnOlderReportNeverUndoesANewerOne()
    {
        var clock = new FakeTimeProvider(Monday.AddMinutes(10)); // both reads in the past, as the tap's always are
        using var service = new RosterService(new FakeEngine(), NewStore(), clock, NullLogger<RosterService>.Instance);
        service.Take(Progress(AlphaIdentity with { Level = 45, GearScore = 2_040 }, (EntryIds.OdyleEnergy, 300, 0, Monday.AddMinutes(5))));
        service.Take(Progress(AlphaIdentity with { ClassId = 0, ClassName = "", CombatPower = 0 },
            (EntryIds.OdyleEnergy, 310, 0, Monday), (EntryIds.AscensionTrial, 2, 0, Monday)));
        var alpha = Assert.Single(service.Characters);
        Assert.Equal(Seen(300, 0, Monday.AddMinutes(5)), alpha.Odyle);
        Assert.Equal(Seen(2, 0, Monday), alpha.Ascension);
        Assert.Equal((16, "Spiritmaster", 41_000, 45, 2_040), (alpha.ClassId, alpha.ClassName, alpha.CombatPower, alpha.Level, alpha.GearScore));

        service.Take(Progress(AlphaIdentity with { CombatPower = 43_500, Level = 46, GearScore = 2_100 }));
        alpha = Assert.Single(service.Characters);
        Assert.Equal((43_500, 46, 2_100), (alpha.CombatPower, alpha.Level, alpha.GearScore));
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

    // Before the game has named any character, a run is put on no one, and says so without naming anyone.
    [Fact]
    public void ARunBeforeAnyCharacterIsNamedIsNotCounted()
    {
        var clock = new FakeTimeProvider(Monday);
        var log = new RecordingLogger<RosterService>();
        using var service = new RosterService(new FakeEngine(), NewStore(), clock, log);
        service.TakeMap(20);
        service.TakeMap(MapNames.ArtifactCorridor);
        Assert.Empty(service.Characters);
        Assert.Single(log.Entries, e => e.Message.Contains("before the game named any character; not counted", StringComparison.Ordinal));
    }

    // While the engine is not sure who is playing (entering the Abyss is a server change; the record naming the character
    // can come after the corridor's load), a run is held for the character the roster knew: counted at its own time when
    // the game names that same character within ten minutes, dropped when it names another (a switch) or none in time, so
    // it never lands on the wrong character (deep audit on #49). Nothing logged names anyone.
    [Fact]
    public void ARunWhileTheCharacterIsNotSureIsHeldForTheSameCharacter()
    {
        var clock = new FakeTimeProvider(Monday);
        var log = new RecordingLogger<RosterService>();
        var engine = new FakeEngine();
        using var service = new RosterService(engine, NewStore(), clock, log);
        var beta = AlphaIdentity with { Name = "Beta" };
        service.Take(Progress(AlphaIdentity));

        service.TakeMap(20);
        service.TakeMap(MapNames.ArtifactCorridor);
        Assert.Empty(service.Characters.Single().CorridorRuns);
        clock.Advance(TimeSpan.FromMinutes(2));
        service.Take(Progress(AlphaIdentity));
        Assert.Equal([Monday], service.Characters.Single().CorridorRuns); // at the entry's time, not the naming's

        service.TakeMap(20);
        service.TakeMap(MapNames.ArtifactCorridor);
        service.Take(Progress(beta));
        Assert.Equal([Monday], service.Characters.Single(c => c.Name == "Alpha").CorridorRuns);
        Assert.Empty(service.Characters.Single(c => c.Name == "Beta").CorridorRuns);

        service.TakeMap(20);
        service.TakeMap(MapNames.ArtifactCorridor); // held for Beta, the character the roster knew last
        clock.Advance(RosterService.CorridorConfirmWindow + TimeSpan.FromSeconds(1));
        service.Take(Progress(beta));
        Assert.Empty(service.Characters.Single(c => c.Name == "Beta").CorridorRuns);

        Assert.Contains(log.Entries, e => e.Message.Contains("held until the game names them again", StringComparison.Ordinal));
        Assert.Contains(log.Entries, e => e.Message.Contains("held Artifact Corridor entries counted", StringComparison.Ordinal));
        Assert.Equal(2, log.Entries.Count(e => e.Message.Contains("held Artifact Corridor entries dropped", StringComparison.Ordinal)));
        Assert.All(log.Entries, e => Assert.DoesNotContain("Alpha", e.Message, StringComparison.Ordinal));
    }

    // Two quick runs with a known map between them are two runs: only a reload straight after an unknown map, the corridor
    // the last map known, is the same run.
    [Fact]
    public void TwoQuickRunsAreTwoRuns()
    {
        var clock = new FakeTimeProvider(Monday);
        using var service = new RosterService(new FakeEngine { Character = AlphaIdentity }, NewStore(), clock, NullLogger<RosterService>.Instance);
        service.TakeMap(20);
        service.TakeMap(MapNames.ArtifactCorridor);
        clock.Advance(TimeSpan.FromMinutes(3));
        service.TakeMap(20);
        service.TakeMap(MapNames.ArtifactCorridor);
        clock.Advance(TimeSpan.FromMinutes(1));
        service.TakeMap(20);
        service.TakeMap(null); // a reconnection in the Abyss: the last map known is not the corridor
        service.TakeMap(MapNames.ArtifactCorridor);
        Assert.Equal(3, service.Characters.Single().CorridorRuns.Count);
    }

    // A saved run dated after now (a clock set back since) neither swallows new runs as "the same run" nor breaks the
    // order: it is dropped when a run is added (Codex on #49).
    [Fact]
    public void ARunDatedAfterNowDoesNotSwallowNewRuns()
    {
        var store = NewStore();
        store.Save(new RosterFile { Characters = [new RosterCharacter { Name = "Alpha", Server = "SIE", LastSeen = Monday, CorridorRuns = [Monday.AddHours(6)] }] });
        var clock = new FakeTimeProvider(Monday);
        using var service = new RosterService(new FakeEngine { Character = AlphaIdentity }, store, clock, NullLogger<RosterService>.Instance);
        service.TakeMap(MapNames.ArtifactCorridor);
        clock.Advance(TimeSpan.FromMinutes(6));
        service.TakeMap(null);
        service.TakeMap(MapNames.ArtifactCorridor);
        Assert.Equal([Monday, Monday.AddMinutes(6)], service.Characters.Single().CorridorRuns);
    }

    // A character first seen (or removed and back) through a corridor run has what the engine knows of it.
    [Fact]
    public void ACharacterFirstSeenInTheCorridorHasItsClass()
    {
        using var service = new RosterService(
            new FakeEngine { Character = AlphaIdentity with { ServerId = 2002, Level = 45, GearScore = 2_040 } }, NewStore(), new FakeTimeProvider(Monday), NullLogger<RosterService>.Instance);
        service.TakeMap(20);
        service.TakeMap(MapNames.ArtifactCorridor);
        var alpha = service.Characters.Single();
        Assert.Equal((16, "Spiritmaster", 41_000, 2002), (alpha.ClassId, alpha.ClassName, alpha.CombatPower, alpha.ServerId));
        Assert.Equal((45, 2_040), (alpha.Level, alpha.GearScore));
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
        runs.Add(Monday); // listed twice
        var (file, repairs) = RosterNormalizer.Normalize(new RosterFile { Characters = [new RosterCharacter { Name = "Alpha", Server = "SIE", LastSeen = Monday, CorridorRuns = runs }] }, Monday);
        var kept = Assert.Single(file.Characters).CorridorRuns;
        Assert.Equal(RosterRules.CorridorRunsKept, kept.Count);
        Assert.Equal([Monday.AddHours(-1), Monday], kept.TakeLast(2));
        Assert.Equal(kept.Order(), kept);
        Assert.Contains("2 unusable counts or corridor entries dropped.", repairs);
        Assert.Contains("3 corridor entries listed twice or past the 12 kept dropped.", repairs);
        var (none, _) = RosterNormalizer.Normalize(new RosterFile { Characters = [new RosterCharacter { Name = "Alpha", Server = "SIE", LastSeen = Monday, CorridorRuns = null! }] }, Monday);
        Assert.Empty(Assert.Single(none.Characters).CorridorRuns);
    }

    // A record compares a list by reference, so the roster's own equality compares every value, the corridor runs and the
    // passages one by one: a change to any property makes two characters differ, and the same values in a new list do not.
    [Fact]
    public void TwoCharactersAreEqualOnlyWhenEveryValueIs()
    {
        var baseline = Alpha(Monday) with { CorridorRuns = [Monday], Passages = new RosterPassages([true, false, false], Monday, Monday) };
        var same = baseline with { CorridorRuns = [Monday], Passages = new RosterPassages([true, false, false], Monday, Monday) };
        Assert.Equal(baseline, same);
        Assert.Equal(baseline.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(baseline, baseline with { Passages = baseline.Passages! with { SeenAt = Monday.AddMinutes(1) } });
        Assert.NotEqual(baseline, baseline with { Passages = baseline.Passages! with { LeftSeenAt = null } });
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
                RosterKina kina => kina with { Bound = (kina.Bound ?? 0) + 1 },
                RosterPassages passages => passages with { Used = [.. passages.Used.Select(used => !used)] },
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
        // The passages the game said were used, when more, among the corridors of the side's artifacts on its server.
        var passages = character with { ServerId = 1001, Passages = new RosterPassages([true, true, true], saturday.AddHours(3), saturday.AddHours(1)) };
        Assert.Equal(3, Assert.Single(RosterPage.Rows([passages], null, Monday, Calendar, RosterSort.Name)).Corridors);
        var held = new Dictionary<int, RosterArtifacts> { [1001] = Holders(1001, saturday.AddHours(1), Monday.AddHours(10), ArtifactHolder.Elyos, ArtifactHolder.Asmodian, ArtifactHolder.Elyos) };
        var row = Assert.Single(RosterPage.Rows([passages], null, Monday, Calendar, RosterSort.Name, held));
        Assert.Equal((2, 2), (row.Corridors, row.CorridorsOpen));
    }

    // ---------- Kina ----------

    // The Kina from a report replaces the kept one when read later; an item the report does not know keeps its kept value,
    // an older report changes nothing, and a kept one dated after now (a clock set back) is replaced.
    [Fact]
    public void KinaIsKeptFromTheLatestReport()
    {
        var clock = new FakeTimeProvider(Monday.AddMinutes(10));
        using var service = new RosterService(new FakeEngine(), NewStore(), clock, NullLogger<RosterService>.Instance);
        CharacterProgress Report(long? kina, long? bound, DateTimeOffset at) => new(AlphaIdentity, new Dictionary<uint, EntryCount>(), new KinaBalance(kina, bound, at));
        service.Take(Report(1_000, 50, Monday));
        service.Take(Report(2_000, null, Monday.AddMinutes(1)));
        Assert.Equal(new RosterKina(2_000, 50, Monday.AddMinutes(1)), service.Characters.Single().Kina);
        service.Take(Report(1, 1, Monday)); // older
        Assert.Equal(new RosterKina(2_000, 50, Monday.AddMinutes(1)), service.Characters.Single().Kina);
        service.Take(Report(3_000, 60, Monday.AddHours(5))); // dated ahead of the clock
        service.Take(Report(4_000, 70, Monday.AddMinutes(5)));
        Assert.Equal(new RosterKina(4_000, 70, Monday.AddMinutes(5)), service.Characters.Single().Kina);
        service.Take(Report(null, null, Monday.AddMinutes(6)) with { Kina = null });
        Assert.Equal(4_000, service.Characters.Single().Kina?.Kina); // a report without Kina keeps it
    }

    // Saved Kina is checked like every count: out of range, neither item known or an unusable date drops it.
    [Theory]
    [InlineData(-1L, 0L, 0)]
    [InlineData(1L << 40, 0L, 0)]
    [InlineData(5L, -3L, 0)]
    [InlineData(null, null, 0)]
    [InlineData(5L, null, 9)]
    public void SavedKinaIsChecked(long? kina, long? bound, int daysAhead)
    {
        var saved = new RosterCharacter { Name = "Alpha", Server = "SIE", LastSeen = Monday, Kina = new RosterKina(kina, bound, Monday.AddDays(daysAhead)) };
        var (file, repairs) = RosterNormalizer.Normalize(new RosterFile { Characters = [saved] }, Monday);
        Assert.Null(Assert.Single(file.Characters).Kina);
        Assert.NotEmpty(repairs);
        var kept = saved with { Kina = new RosterKina(5, null, Monday) };
        Assert.Equal(kept.Kina, Assert.Single(RosterNormalizer.Normalize(new RosterFile { Characters = [kept] }, Monday).File.Characters).Kina);
    }

    // Most Kina first: Kina and Kina (Bound) together; a character without any known last.
    [Fact]
    public void TheRowsSortByKina()
    {
        RosterCharacter[] characters =
        [
            new() { Name = "Alpha", Server = "SIE", LastSeen = Monday, Kina = new RosterKina(100, 2_900, Monday) },
            new() { Name = "Beta", Server = "SIE", LastSeen = Monday, Kina = new RosterKina(2_000, null, Monday) },
            new() { Name = "Gamma", Server = "SIE", LastSeen = Monday },
        ];
        Assert.Equal(["Alpha", "Beta", "Gamma"], RosterPage.Rows(characters, null, Monday, Calendar, RosterSort.MostKina).Select(r => r.Character.Name));
    }

    // The header's totals: each item summed over the characters that know it, and how many know neither, not counted as none.
    [Fact]
    public void KinaTotalsSayHowManyAreNotKnown()
    {
        RosterCharacter[] characters =
        [
            new() { Name = "Alpha", Server = "SIE", LastSeen = Monday, Kina = new RosterKina(100, 2_900, Monday) },
            new() { Name = "Beta", Server = "SIE", LastSeen = Monday, Kina = new RosterKina(2_000, null, Monday) },
            new() { Name = "Gamma", Server = "SIE", LastSeen = Monday },
            new() { Name = "Delta", Server = "SIE", LastSeen = Monday, Kina = new RosterKina(null, null, Monday) },
        ];
        Assert.Equal((2_100L, 2_900L, 2), RosterPage.KinaTotals(RosterPage.Rows(characters, null, Monday, Calendar, RosterSort.Name)));
        Assert.Equal((0L, 0L, 0), RosterPage.KinaTotals([]));
    }

    // ---------- Notes ----------

    // A note is the player's own line: spaces at either end trimmed, control characters and other spaces as plain spaces,
    // text that is not valid UTF-16 as U+FFFD, and cut at the limit without splitting a character.
    [Fact]
    public void ANoteIsCleaned()
    {
        Assert.Equal(string.Empty, RosterNormalizer.CleanNote(null));
        Assert.Equal(string.Empty, RosterNormalizer.CleanNote(" \r\n\t "));
        Assert.Equal("main  healer", RosterNormalizer.CleanNote("  main\r\nhealer\t "));
        Assert.Equal("a b", RosterNormalizer.CleanNote("a\u0007b"));
        Assert.Equal("note", RosterNormalizer.CleanNote("\u0007note\u001B")); // a control character at an end goes too
        Assert.Equal("a b", RosterNormalizer.CleanNote("a　b"));
        Assert.Equal("x�y", RosterNormalizer.CleanNote("x\uD800y"));
        Assert.Equal("Siel's Wings · 2/3 ☆", RosterNormalizer.CleanNote("Siel's Wings · 2/3 ☆"));
        var short1 = new string('a', RosterNormalizer.MaxNoteLength - 1);
        Assert.Equal(short1, RosterNormalizer.CleanNote(short1 + "\U0001F600")); // the pair would pass the limit: it goes whole
        Assert.Equal(short1 + "b", RosterNormalizer.CleanNote(short1 + "b\U0001F600"));
        Assert.Equal(RosterNormalizer.MaxNoteLength, RosterNormalizer.CleanNote(new string('n', 5_000)).Length);
        Assert.Equal(short1, RosterNormalizer.CleanNote(short1 + " next words")); // a cut that ends on a space

        // Pointing at the box shows the note whole (a long one is cut in its box), or how to write one.
        Assert.Equal("main", RosterPage.NoteTitle(new RosterCharacter { Name = "A", Note = "main" }));
        Assert.Contains("Enter", RosterPage.NoteTitle(new RosterCharacter { Name = "A" }), StringComparison.Ordinal);
    }

    // The player sets a note: cleaned, kept through later reports and corridor runs, saved; unchanged, or on a character
    // not in the roster, it changes nothing.
    [Fact]
    public void ANoteIsKeptThroughReportsAndSaved()
    {
        var clock = new FakeTimeProvider(Monday);
        var store = NewStore();
        var changes = 0;
        using (var service = new RosterService(new FakeEngine { Character = AlphaIdentity }, store, clock, NullLogger<RosterService>.Instance))
        {
            service.Take(Progress(AlphaIdentity));
            service.Changed += (_, _) => changes++;
            service.SetNote(new RosterKey("SIE", "Alpha"), "  main\n");
            service.SetNote(new RosterKey("SIE", "Alpha"), "main ");
            service.SetNote(new RosterKey("SIE", "Nobody"), "x");
            Assert.Equal(1, changes);
            Assert.Single(service.Characters);

            clock.Advance(TimeSpan.FromMinutes(1));
            service.Take(Progress(AlphaIdentity, (EntryIds.OdyleEnergy, 100, 0, clock.GetUtcNow())));
            service.TakeMap(20);
            service.TakeMap(MapNames.ArtifactCorridor);
            Assert.Equal("main", service.Characters.Single().Note);
        }
        Assert.Equal("main", Assert.Single(store.Load(clock.GetUtcNow()).File.Characters).Note);

        // A note alone is a change to save.
        using (var service = new RosterService(new FakeEngine(), store, clock, NullLogger<RosterService>.Instance))
            service.SetNote(new RosterKey("SIE", "Alpha"), "alt");
        Assert.Equal("alt", Assert.Single(store.Load(clock.GetUtcNow()).File.Characters).Note);
    }

    // A level or gear score past its bound, the character's only fault, is reset and said repaired (deep audit on #56: the
    // repair went uncounted with the condition removed); one within its bounds needs nothing.
    [Theory]
    [InlineData(100, 0, true)]
    [InlineData(0, 100_001, true)]
    [InlineData(-1, 0, true)]
    [InlineData(99, 100_000, false)]
    public void ALevelOrGearScorePastItsBoundIsSaidRepaired(int level, int gearScore, bool repaired)
    {
        var saved = new RosterCharacter { Name = "Alpha", Server = "SIE", LastSeen = Monday, Level = level, GearScore = gearScore };
        var (file, repairs) = RosterNormalizer.Normalize(new RosterFile { Characters = [saved] }, Monday);
        var character = Assert.Single(file.Characters);
        Assert.Equal(repaired ? (0, 0) : (level, gearScore), (character.Level, character.GearScore));
        if (repaired) Assert.Equal(["Server, class, combat power, level, gear score, cap, note, the order of corridor entries or the corridor passages repaired on 1 character."], repairs);
        else Assert.Empty(repairs);
    }

    // A saved note is cleaned as one from the page, and said repaired without quoting it (the repairs are logged); none
    // saved (null) is empty and needs nothing.
    [Fact]
    public void ASavedNoteIsCleaned()
    {
        var saved = new RosterCharacter { Name = "Alpha", Server = "SIE", LastSeen = Monday, Note = "Alt\u0007 " + new string('n', 300) };
        var (file, repairs) = RosterNormalizer.Normalize(new RosterFile { Characters = [saved] }, Monday);
        var note = Assert.Single(file.Characters).Note;
        Assert.StartsWith("Alt  nnn", note, StringComparison.Ordinal);
        Assert.Equal(RosterNormalizer.MaxNoteLength, note.Length);
        Assert.Equal(["Server, class, combat power, level, gear score, cap, note, the order of corridor entries or the corridor passages repaired on 1 character."], repairs);

        var (none, nothing) = RosterNormalizer.Normalize(new RosterFile { Characters = [saved with { Note = null! }] }, Monday);
        Assert.Equal(string.Empty, Assert.Single(none.Characters).Note);
        Assert.Empty(nothing);
    }

    // ---------- Corridors open: the Abyss's artifacts ----------

    private static readonly DateTimeOffset Saturday = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero); // a siege on the schedule

    private static RosterArtifacts Holders(int serverId, DateTimeOffset seenAt, DateTimeOffset? until, params ArtifactHolder[] holders) =>
        new() { ServerId = serverId, Holders = holders, Since = until is null ? null : Saturday.AddMinutes(10), Until = until, SeenAt = seenAt };

    private static AbyssArtifacts Reading(DateTimeOffset seenAt, params (int Id, ArtifactHolder Holder)[] artifacts) => new(
        [new ArtifactWar(2001, null, null), new ArtifactWar(AbyssArtifacts.LowerLayer, Saturday.AddMinutes(10), Monday.AddHours(10))],
        [.. artifacts.Select(a => new AbyssArtifact(a.Id, a.Holder))], seenAt);

    // A side can run the corridors whose artifact it holds: one nobody holds is closed to both, one the game did not name
    // counts as open (A2Tools' rule). Not known without a side, without holders read on that server, or once they are past.
    [Fact]
    public void TheCorridorsOpenAreTheArtifactsYourSideHolds()
    {
        var now = Monday; // Monday 02:30 UTC; the holding resets at 12:00
        var held = Holders(1001, Saturday.AddHours(1), Monday.AddHours(10), ArtifactHolder.Elyos, ArtifactHolder.Asmodian, ArtifactHolder.Elyos);
        Assert.Equal(2, RosterRules.CorridorsOpen(1001, held, now, Calendar));
        Assert.Equal(1, RosterRules.CorridorsOpen(2002, held with { ServerId = 2002 }, now, Calendar));
        var mixed = held with { Holders = [ArtifactHolder.Nobody, ArtifactHolder.Unknown, ArtifactHolder.Asmodian] };
        Assert.Equal(1, RosterRules.CorridorsOpen(1001, mixed, now, Calendar));
        Assert.Equal(0, RosterRules.CorridorsOpen(1001, held with { Holders = [ArtifactHolder.Asmodian, ArtifactHolder.Asmodian, ArtifactHolder.Nobody] }, now, Calendar));

        Assert.Null(RosterRules.CorridorsOpen(0, held, now, Calendar)); // the side not known
        Assert.Null(RosterRules.CorridorsOpen(1001, null, now, Calendar));
        Assert.Null(RosterRules.CorridorsOpen(1003, held, now, Calendar)); // another server's
        Assert.Null(RosterRules.CorridorsOpen(1001, held, Monday.AddHours(10), Calendar)); // its reset has come

        // Without a reset the meter can use, the schedule stands in: current until a siege after it was read.
        var noReset = held with { Until = null };
        Assert.Equal(2, RosterRules.CorridorsOpen(1001, noReset, now, Calendar));
        Assert.Null(RosterRules.CorridorsOpen(1001, noReset with { SeenAt = Saturday.AddHours(-1) }, now, Calendar));
        var tooFar = held with { Until = held.SeenAt + RosterRules.LongestHolding + TimeSpan.FromMinutes(1) };
        Assert.Null(RosterRules.CorridorsOpen(1001, tooFar with { SeenAt = Saturday.AddHours(-1), Until = Saturday.AddDays(9) }, now, Calendar));
        Assert.Equal(2, RosterRules.CorridorsOpen(1001, tooFar, now, Calendar));
        Assert.Equal(2, RosterRules.CorridorsOpen(1001, held with { Until = held.SeenAt }, now, Calendar)); // a reset not after it was read
        Assert.Equal([true, false, true], RosterRules.OpenCorridors(1001, held, now, Calendar));
        Assert.Null(RosterRules.OpenCorridors(1001, null, now, Calendar));
    }

    // A report with the Abyss passages as the game sends them: "left" a time (its usage time), "zero" a time of 0,
    // "used" a count with no value, null not sent.
    private static CharacterProgress PassagesReport(CharacterIdentity who, DateTimeOffset at, params string?[] passages)
    {
        var entries = new Dictionary<uint, EntryCount>();
        var times = new Dictionary<uint, EntryTime>();
        for (var n = 0; n < passages.Length; n++)
        {
            var id = EntryIds.AbyssPassages[n];
            if (passages[n] == "left") times[id] = new EntryTime(130_000, at);
            if (passages[n] == "zero") times[id] = new EntryTime(0, at);
            if (passages[n] == "used") entries[id] = new EntryCount(0, 0, at);
        }
        return new CharacterProgress(who, entries) { Times = times };
    }

    // The passages are taken once the game has said all three (used as a count, still to run as a time), read when the
    // latest of them was; a report knowing fewer (a meter started mid-session) or with a time of 0 keeps what was kept, as
    // does one read no later. One still to run dates when the passages were last seen handed out; a reading kept from
    // after now (a clock set back) is no later than anything.
    [Fact]
    public void ThePassagesAreTakenOnceTheGameHasSaidAllThree()
    {
        var clock = new FakeTimeProvider(Monday);
        using var service = new RosterService(new FakeEngine(), NewStore(), clock, NullLogger<RosterService>.Instance);
        RosterPassages? Kept() => Assert.Single(service.Characters).Passages;
        service.Take(PassagesReport(AlphaIdentity, Monday, "used", null, "left"));
        Assert.Null(Kept());
        service.Take(PassagesReport(AlphaIdentity, Monday, "used", "zero", "left"));
        Assert.Null(Kept());

        service.Take(PassagesReport(AlphaIdentity, Monday, "used", "used", "left"));
        Assert.Equal(new RosterPassages([true, true, false], Monday, Monday), Kept());

        clock.Advance(TimeSpan.FromHours(2));
        var later = Monday.AddHours(1);
        var mixed = PassagesReport(AlphaIdentity, Monday, "used", "used", "used");
        mixed = mixed with { Entries = new Dictionary<uint, EntryCount>(mixed.Entries) { [EntryIds.AbyssPassages[2]] = new(0, 0, later) } };
        service.Take(mixed);
        Assert.Equal(new RosterPassages([true, true, true], later, Monday), Kept()); // the latest of the three; handed out at Monday

        service.Take(PassagesReport(AlphaIdentity, Monday, "left", "left", "left")); // older
        Assert.Equal(new RosterPassages([true, true, true], later, Monday), Kept());
        service.Take(PassagesReport(AlphaIdentity, later, "left", "used", "used")); // no later
        Assert.Equal(new RosterPassages([true, true, true], later, Monday), Kept());

        service.Take(PassagesReport(AlphaIdentity, Monday.AddDays(1), "used", "used", "used")); // dated after now
        service.Take(PassagesReport(AlphaIdentity, Monday.AddHours(2), "left", "used", "used"));
        Assert.Equal(new RosterPassages([false, true, true], Monday.AddHours(2), Monday.AddHours(2)), Kept());
    }

    // The corridors run since the siege: the meter's own count, or the passages the game said were used since that siege,
    // among those the side can enter (all three while that is not known), when more. All of those used is taken only when
    // one had time left since the siege (before the passages are handed out all three read as used); a reading dated after
    // now is not taken.
    [Fact]
    public void TheCorridorsDoneAreTheMetersOrThePassagesWhenMore()
    {
        var now = Monday; // the last siege Saturday 12:00 UTC
        var alpha = new RosterCharacter { Name = "Alpha", Server = "SIE", ServerId = 1001, LastSeen = now };
        var read = Saturday.AddHours(2);
        Assert.Equal(0, RosterRules.CorridorsDone(alpha, null, now, Calendar));
        var two = alpha with { Passages = new RosterPassages([true, true, false], read, read) };
        Assert.Equal(2, RosterRules.CorridorsDone(two, null, now, Calendar));
        Assert.Equal(3, RosterRules.CorridorsDone(two with { CorridorRuns = [Saturday.AddHours(3), Saturday.AddHours(4), Saturday.AddHours(5)] }, null, now, Calendar));
        var before = two.Passages! with { SeenAt = Saturday.AddHours(-1), LeftSeenAt = Saturday.AddHours(-1) };
        Assert.Equal(1, RosterRules.CorridorsDone(two with { CorridorRuns = [Saturday.AddHours(3)], Passages = before }, null, now, Calendar));
        Assert.Equal(0, RosterRules.CorridorsDone(two with { Passages = two.Passages! with { SeenAt = now.AddMinutes(1) } }, null, now, Calendar));

        var all = alpha with { Passages = new RosterPassages([true, true, true], read, null) };
        Assert.Equal(0, RosterRules.CorridorsDone(all, null, now, Calendar));
        Assert.Equal(0, RosterRules.CorridorsDone(all with { Passages = all.Passages! with { LeftSeenAt = Saturday.AddHours(-1) } }, null, now, Calendar));
        Assert.Equal(0, RosterRules.CorridorsDone(all with { Passages = all.Passages! with { LeftSeenAt = now.AddMinutes(1) } }, null, now, Calendar));
        Assert.Equal(3, RosterRules.CorridorsDone(all with { Passages = all.Passages! with { LeftSeenAt = Saturday.AddHours(1) } }, null, now, Calendar));

        // 1002's artifact the other side's: only 1001's and 1003's passages count.
        var held = Holders(1001, Saturday.AddHours(1), Monday.AddHours(10), ArtifactHolder.Elyos, ArtifactHolder.Asmodian, ArtifactHolder.Elyos);
        var mixed = alpha with { Passages = new RosterPassages([false, true, true], read, read) };
        Assert.Equal(1, RosterRules.CorridorsDone(mixed, held, now, Calendar));
        Assert.Equal(2, RosterRules.CorridorsDone(mixed, null, now, Calendar));
        var ours = alpha with { Passages = new RosterPassages([true, false, true], read, null) };
        Assert.Equal(0, RosterRules.CorridorsDone(ours, held, now, Calendar)); // both of the side's used, none seen handed out
        Assert.Equal(2, RosterRules.CorridorsDone(ours with { Passages = ours.Passages! with { LeftSeenAt = read } }, held, now, Calendar));
    }

    // Saved passages are three flags read at a usable date, or none; a time left later than the reading, or at no usable
    // date, is dropped and the character counted as repaired.
    [Fact]
    public void SavedPassagesAreCheckedOnLoad()
    {
        var saved = new RosterCharacter { Name = "Alpha", Server = "SIE", LastSeen = Monday };
        (RosterPassages? Passages, IReadOnlyList<string> Repairs) Loaded(RosterPassages? passages)
        {
            var (file, repairs) = RosterNormalizer.Normalize(new RosterFile { Characters = [saved with { Passages = passages }] }, Monday);
            return (Assert.Single(file.Characters).Passages, repairs);
        }
        var good = new RosterPassages([true, false, false], Monday.AddHours(-1), Monday.AddHours(-2));
        Assert.Equal(good, Loaded(good).Passages);
        Assert.Empty(Loaded(good).Repairs);
        Assert.Null(Loaded(null).Passages);
        Assert.Empty(Loaded(null).Repairs);
        foreach (var bad in new[] { good with { Used = [true, false] }, good with { Used = [true, false, false, true] }, good with { Used = null! },
                     good with { SeenAt = default }, good with { SeenAt = Monday.AddYears(5) } })
        {
            var (passages, repairs) = Loaded(bad);
            Assert.Null(passages);
            Assert.Equal(["1 unusable count or corridor entry dropped."], repairs);
        }
        foreach (var left in new DateTimeOffset[] { Monday, default })
        {
            var (passages, repairs) = Loaded(good with { LeftSeenAt = left });
            Assert.Equal(good with { LeftSeenAt = null }, passages);
            Assert.Equal(["Server, class, combat power, level, gear score, cap, note, the order of corridor entries or the corridor passages repaired on 1 character."], repairs);
        }
    }

    // Holders read while a character is played are kept for its server, with the lower layer's war; a later reading
    // replaces them, an older one does not, the upper layer's alone says nothing. They are saved and loaded with the roster.
    [Fact]
    public void TheHoldersAreKeptForTheServerOfTheCharacterPlayed()
    {
        var clock = new FakeTimeProvider(Monday);
        var store = NewStore();
        var engine = new FakeEngine { Character = AlphaIdentity with { ServerId = 1001 } };
        var changes = 0;
        using (var service = new RosterService(engine, store, clock, NullLogger<RosterService>.Instance))
        {
            service.Start();
            Assert.Equal((1, 1), (engine.ArtifactSubscribers, engine.ConnectionSubscribers));
            service.Changed += (_, _) => changes++;
            engine.RaiseArtifacts(Reading(Monday.AddMinutes(-5), (2001, ArtifactHolder.Nobody), (1001, ArtifactHolder.Elyos), (1002, ArtifactHolder.Asmodian), (1003, ArtifactHolder.Elyos)));
            var kept = service.Artifacts[1001];
            Assert.Equal([ArtifactHolder.Elyos, ArtifactHolder.Asmodian, ArtifactHolder.Elyos], kept.Holders);
            Assert.Equal((Saturday.AddMinutes(10), Monday.AddHours(10), Monday.AddMinutes(-5)), (kept.Since, kept.Until, kept.SeenAt));

            service.TakeArtifacts(Reading(Monday.AddMinutes(-9), (1001, ArtifactHolder.Asmodian))); // older
            service.TakeArtifacts(Reading(Monday, (2001, ArtifactHolder.Elyos))); // the upper layer alone
            Assert.Equal(kept, service.Artifacts[1001]);
            service.TakeArtifacts(Reading(Monday, (1002, ArtifactHolder.Elyos))); // later: an artifact not said is unknown
            Assert.Equal([ArtifactHolder.Unknown, ArtifactHolder.Elyos, ArtifactHolder.Unknown], service.Artifacts[1001].Holders);
            Assert.Equal(2, changes);

            // One dated after now (a clock set back since) is replaced by the next reading, or nothing would replace it.
            service.TakeArtifacts(Reading(Monday.AddHours(5), (1001, ArtifactHolder.Asmodian)));
            service.TakeArtifacts(Reading(Monday.AddMinutes(-1), (1001, ArtifactHolder.Nobody)));
            Assert.Equal(ArtifactHolder.Nobody, service.Artifacts[1001].Holders[0]);
            service.TakeArtifacts(Reading(Monday, (1002, ArtifactHolder.Elyos)));
            Assert.Equal(5, changes);
        }
        var loaded = store.Load(Monday).File;
        Assert.Equal([ArtifactHolder.Unknown, ArtifactHolder.Elyos, ArtifactHolder.Unknown], Assert.Single(loaded.Artifacts).Holders);
        Assert.Equal(1001, Assert.Single(loaded.Artifacts).ServerId);
        Assert.Contains("\"elyos\"", File.ReadAllText(store.FilePath), StringComparison.Ordinal);
        Assert.Equal((0, 0), (engine.ArtifactSubscribers, engine.ConnectionSubscribers));
        using var again = new RosterService(new FakeEngine(), store, clock, NullLogger<RosterService>.Instance);
        Assert.Equal(Assert.Single(loaded.Artifacts), again.Artifacts[1001]); // the next run starts with them
    }

    // Holders read with no server to put them on are not kept: before any character is named, or for a character whose
    // server id is not known.
    [Fact]
    public void HoldersWithNoServerAreNotKept()
    {
        var log = new RecordingLogger<RosterService>();
        var engine = new FakeEngine();
        using var service = new RosterService(engine, NewStore(), new FakeTimeProvider(Monday), log);
        service.TakeArtifacts(Reading(Monday, (1001, ArtifactHolder.Elyos)));
        engine.Character = AlphaIdentity;
        service.TakeArtifacts(Reading(Monday, (1001, ArtifactHolder.Elyos)));
        Assert.Empty(service.Artifacts);
        Assert.Contains(log.Entries, e => e.Message.Contains("before the game named any character", StringComparison.Ordinal));
        Assert.Contains(log.Entries, e => e.Message.Contains("server of the character being played is not known", StringComparison.Ordinal));
    }

    // Entering the Abyss is a server change, after which the engine is not sure who is playing until the game names the
    // character again: holders read meanwhile are held for the character the roster knew, as a corridor entry is, and
    // kept for its server when the game names that same character within ten minutes; dropped for another, or too late.
    [Fact]
    public void HoldersReadWhileTheCharacterIsNotSureAreHeldForTheSameCharacter()
    {
        var clock = new FakeTimeProvider(Monday);
        var log = new RecordingLogger<RosterService>();
        using var service = new RosterService(new FakeEngine(), NewStore(), clock, log);
        var alpha = AlphaIdentity with { ServerId = 1001 };
        var beta = AlphaIdentity with { Name = "Beta", ServerId = 2002 };
        service.Take(Progress(alpha));

        service.TakeArtifacts(Reading(Monday.AddSeconds(-5), (1001, ArtifactHolder.Asmodian)));
        service.TakeArtifacts(Reading(Monday, (1001, ArtifactHolder.Elyos))); // the latest is the one held
        Assert.Empty(service.Artifacts);
        clock.Advance(TimeSpan.FromMinutes(1));
        service.Take(Progress(alpha));
        Assert.Equal(Monday, service.Artifacts[1001].SeenAt); // at the reading's time
        Assert.Equal(ArtifactHolder.Elyos, service.Artifacts[1001].Holders[0]);

        service.TakeArtifacts(Reading(clock.GetUtcNow(), (1001, ArtifactHolder.Asmodian)));
        service.Take(Progress(beta));
        Assert.Equal(ArtifactHolder.Elyos, service.Artifacts[1001].Holders[0]);
        Assert.False(service.Artifacts.ContainsKey(2002));

        service.TakeArtifacts(Reading(clock.GetUtcNow(), (1001, ArtifactHolder.Asmodian))); // held for Beta
        clock.Advance(RosterService.CorridorConfirmWindow + TimeSpan.FromSeconds(1));
        service.Take(Progress(beta));
        Assert.False(service.Artifacts.ContainsKey(2002));

        Assert.Contains(log.Entries, e => e.Message.Contains("held until the game names them again", StringComparison.Ordinal));
        Assert.Single(log.Entries, e => e.Message.StartsWith("Held Abyss artifact holders kept", StringComparison.Ordinal));
        Assert.Equal(2, log.Entries.Count(e => e.Message.StartsWith("Held Abyss artifact holders dropped", StringComparison.Ordinal)));
        Assert.All(log.Entries, e => Assert.DoesNotContain("Alpha", e.Message, StringComparison.Ordinal));
    }

    // A report without a server id (upstream kept an older short name for a record whose id it did not know) still puts
    // holders on the server the roster knows for that character, when they are read and when they were held.
    [Fact]
    public void HoldersGoToTheServerTheRosterKnowsForTheCharacter()
    {
        var engine = new FakeEngine();
        using var service = new RosterService(engine, NewStore(), new FakeTimeProvider(Monday), NullLogger<RosterService>.Instance);
        service.Take(Progress(AlphaIdentity with { ServerId = 1001 }));
        service.TakeArtifacts(Reading(Monday, (1001, ArtifactHolder.Elyos))); // held: no character sure
        service.Take(Progress(AlphaIdentity));
        Assert.Equal(ArtifactHolder.Elyos, service.Artifacts[1001].Holders[0]);
        engine.Character = AlphaIdentity;
        service.TakeArtifacts(Reading(Monday.AddMinutes(1), (1001, ArtifactHolder.Asmodian)));
        Assert.Equal(ArtifactHolder.Asmodian, service.Artifacts[1001].Holders[0]);
    }

    // At most 64 servers' holders are kept, those read most recently.
    [Fact]
    public void TheHoldersOfTheServersReadMostRecentlyAreKept()
    {
        List<RosterArtifacts> entries = [.. Enumerable.Range(0, RosterNormalizer.MaxServers + 6)
            .Select(i => Holders(i < 35 ? 1001 + i : 2001 + i - 35, Monday.AddMinutes(-i), null, ArtifactHolder.Elyos, ArtifactHolder.Elyos, ArtifactHolder.Elyos))];
        var (file, repairs) = RosterNormalizer.Normalize(new RosterFile { Artifacts = entries }, Monday);
        Assert.Equal(RosterNormalizer.MaxServers, file.Artifacts.Count);
        Assert.DoesNotContain(file.Artifacts, a => a.SeenAt < Monday.AddMinutes(-(RosterNormalizer.MaxServers - 1)));
        Assert.Equal(["6 Abyss artifact records listed twice or past the 64 kept dropped."], repairs);
    }

    // A file holding text that is not valid Unicode (a lone surrogate in a note) cannot be read as JSON at all: it is set
    // aside like any damaged file, never half-read; the note's own cleaning is for what the page sends.
    [Fact]
    public void ARosterWithTextThatIsNotUnicodeIsSetAside()
    {
        var store = NewStore();
        File.WriteAllText(store.FilePath, "{ \"version\": 1, \"characters\": [ { \"name\": \"Alpha\", \"server\": \"SIE\", \"lastSeen\": \"2026-10-26T01:00:00+00:00\", \"note\": \"x\\ud800y\" } ] }");
        var loaded = store.Load(Monday);
        Assert.Empty(loaded.File.Characters);
        Assert.Single(Directory.GetFiles(directory.FullPath, "roster.unreadable-*.json"));
    }

    // What was held for the game to name the character, corridor entries and artifact holders, was seen on its connection:
    // a move to a new one drops it, so a self record there cannot claim it (Codex on #51). Nothing logged names anyone.
    [Fact]
    public void WhatIsHeldIsDroppedWhenTheGameMovesToANewConnection()
    {
        var clock = new FakeTimeProvider(Monday);
        var log = new RecordingLogger<RosterService>();
        var engine = new FakeEngine();
        using var service = new RosterService(engine, NewStore(), clock, log);
        service.Start();
        var alpha = AlphaIdentity with { ServerId = 1001 };
        service.Take(Progress(alpha));

        service.TakeMap(20);
        service.TakeMap(MapNames.ArtifactCorridor); // held: no character sure
        service.TakeArtifacts(Reading(Monday, (1001, ArtifactHolder.Asmodian))); // held too
        engine.RaiseConnection(); // a switch, say, whose records were lost
        clock.Advance(TimeSpan.FromMinutes(2));
        engine.RaiseConnection(); // and back: nothing held is left to claim
        service.Take(Progress(alpha));
        Assert.Empty(service.Characters.Single().CorridorRuns);
        Assert.Empty(service.Artifacts);
        Assert.Single(log.Entries, e => e.Message.Contains("held Artifact Corridor entries dropped: the game moved to a new connection", StringComparison.Ordinal));
        Assert.Single(log.Entries, e => e.Message.StartsWith("Held Abyss artifact holders dropped: the game moved", StringComparison.Ordinal));
        Assert.All(log.Entries, e => Assert.DoesNotContain("Alpha", e.Message, StringComparison.Ordinal));

        // Held and named on the same connection, they are kept as before.
        service.TakeMap(20);
        service.TakeMap(MapNames.ArtifactCorridor);
        service.TakeArtifacts(Reading(clock.GetUtcNow(), (1001, ArtifactHolder.Elyos)));
        service.Take(Progress(alpha));
        Assert.Single(service.Characters.Single().CorridorRuns);
        Assert.Equal(ArtifactHolder.Elyos, service.Artifacts[1001].Holders[0]);
    }

    // A report gives the character its server id; one without (not known this time) keeps the kept one.
    [Fact]
    public void TheServerIdIsKeptFromTheReports()
    {
        using var service = new RosterService(new FakeEngine(), NewStore(), new FakeTimeProvider(Monday), NullLogger<RosterService>.Instance);
        service.Take(Progress(AlphaIdentity with { ServerId = 2002 }));
        service.Take(Progress(AlphaIdentity));
        Assert.Equal(2002, Assert.Single(service.Characters).ServerId);
    }

    // The rows put each character's runs out of the corridors its side can enter, from the holders on its server.
    [Fact]
    public void TheRowsSayHowManyCorridorsAreOpen()
    {
        RosterCharacter[] characters =
        [
            new() { Name = "Alpha", Server = "SIE", ServerId = 1001, LastSeen = Monday },
            new() { Name = "Beta", Server = "SIE", ServerId = 2002, LastSeen = Monday },
            new() { Name = "Gamma", Server = "SIE", LastSeen = Monday },
            new() { Name = "Delta", Server = "TRI", ServerId = 2003, LastSeen = Monday },
        ];
        var holders = new Dictionary<int, RosterArtifacts>
        {
            [1001] = Holders(1001, Saturday.AddHours(1), Monday.AddHours(10), ArtifactHolder.Elyos, ArtifactHolder.Asmodian, ArtifactHolder.Elyos),
            [2002] = Holders(2002, Saturday.AddHours(1), Monday.AddHours(10), ArtifactHolder.Elyos, ArtifactHolder.Nobody, ArtifactHolder.Elyos),
        };
        var rows = RosterPage.Rows(characters, null, Monday, Calendar, RosterSort.Name, holders);
        Assert.Equal([2, 0, null, null], rows.Select(r => r.CorridorsOpen));
        Assert.Equal(3, rows.Select(RosterPage.CorridorsTitle).Distinct().Count());
        Assert.Contains("out of 3", RosterPage.CorridorsTitle(rows[2]), StringComparison.Ordinal);
        Assert.Contains("the 2 whose artifact", RosterPage.CorridorsTitle(rows[0]), StringComparison.Ordinal);
    }

    // Saved holders are checked: a server without a side, the wrong number of holders or an unusable date drops them; a
    // holder this build cannot name is unknown, and a start or reset out of range goes (the schedule stands in). One per
    // server, the one read last. A character's server id without a side is reset. Nothing said names a server.
    [Fact]
    public void SavedHoldersAreChecked()
    {
        var store = NewStore();
        File.WriteAllText(store.FilePath, """
            { "version": 1,
              "characters": [ { "name": "Alpha", "server": "SIE", "serverId": 5, "lastSeen": "2026-10-26T01:00:00+00:00" } ],
              "artifacts": [
                { "serverId": 1001, "holders": [ "asmodian", "asmodian", "asmodian" ], "seenAt": "2026-10-24T01:00:00+00:00" },
                { "serverId": 1001, "holders": [ "elyos", "dragon", "nobody" ], "until": "2027-03-01T00:00:00+00:00", "seenAt": "2026-10-25T01:00:00+00:00" },
                { "serverId": 2002, "holders": [ "elyos", "elyos" ], "seenAt": "2026-10-25T01:00:00+00:00" },
                { "serverId": 7, "holders": [ "elyos", "elyos", "elyos" ], "seenAt": "2026-10-25T01:00:00+00:00" },
                { "serverId": 2003, "holders": [ "elyos", "elyos", "elyos" ], "seenAt": "2019-10-25T01:00:00+00:00" },
                { "serverId": 2004, "holders": [ "elyos", "nobody", "asmodian" ], "since": "2026-10-24T12:10:00+00:00", "until": "2026-10-26T12:00:00+00:00", "seenAt": "2026-10-25T01:00:00+00:00" },
                { "serverId": 2005, "holders": [ "elyos", null, "asmodian" ], "since": "2019-10-24T12:10:00+00:00", "seenAt": "2026-10-25T01:00:00+00:00" }
              ] }
            """);
        var loaded = store.Load(Monday);
        Assert.Equal(0, Assert.Single(loaded.File.Characters).ServerId);
        Assert.Equal([1001, 2004, 2005], loaded.File.Artifacts.Select(a => a.ServerId));
        Assert.Equal([ArtifactHolder.Elyos, ArtifactHolder.Unknown, ArtifactHolder.Asmodian], loaded.File.Artifacts[2].Holders); // a null holder
        Assert.Null(loaded.File.Artifacts[2].Since); // a start before 2025
        var first = loaded.File.Artifacts[0];
        Assert.Equal([ArtifactHolder.Elyos, ArtifactHolder.Unknown, ArtifactHolder.Nobody], first.Holders);
        Assert.Null(first.Until);
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero), loaded.File.Artifacts[1].Until);
        Assert.Contains("3 unusable Abyss artifact records dropped.", loaded.Notice, StringComparison.Ordinal);
        Assert.Contains("1 Abyss artifact record listed twice or past the 64 kept dropped.", loaded.Notice, StringComparison.Ordinal);
        Assert.Contains("Abyss artifact holders or times repaired on 2 servers.", loaded.Notice, StringComparison.Ordinal);
        Assert.Contains("Server, class", loaded.Notice, StringComparison.Ordinal);
        Assert.DoesNotContain("1001", loaded.Notice, StringComparison.Ordinal);
        Assert.Empty(RosterNormalizer.Normalize(loaded.File, Monday).Repairs); // clean once repaired

        // Equal when every value is, the holders one by one (a record compares a list by reference).
        var copy = first with { Holders = [.. first.Holders] };
        Assert.Equal(first, copy);
        Assert.Equal(first.GetHashCode(), copy.GetHashCode());
        Assert.NotEqual(first, copy with { Holders = [ArtifactHolder.Elyos, ArtifactHolder.Elyos, ArtifactHolder.Nobody] });
        Assert.NotEqual(first, copy with { SeenAt = copy.SeenAt.AddMinutes(1) });
        Assert.NotEqual(first, copy with { Since = Saturday });
        Assert.NotEqual(first, copy with { Until = Monday });
        Assert.NotEqual(first, copy with { ServerId = 1002 });
    }

    // ---------- The overlay's last known Odyle energy and Shugo keys (Harry, 2026-10-28) ----------

    // The Shugo Festival keys are the server's: each report's are kept for the server of the character named (the report's
    // server id, or else the one the roster knows for it), a later reading on that server by any of its characters
    // replacing them and an earlier one never; none without a server id. They are saved and read back. Made-up Global ids.
    [Fact]
    public void TheShugoKeysAreKeptForTheServerOfTheCharacterNamed()
    {
        var clock = new FakeTimeProvider(Monday.AddHours(2));
        var store = NewStore();
        using (var service = new RosterService(new FakeEngine(), store, clock, NullLogger<RosterService>.Instance))
        {
            var alpha = AlphaIdentity with { Server = string.Empty, ServerId = 1503 };
            service.Take(Progress(alpha, (EntryIds.ShugoFestivalKeys, 12, 3, Monday)));
            Assert.Equal(new RosterShugoKeys(1503, 12, 3, Monday), service.ShugoKeys[1503]);
            var beta = new CharacterIdentity("Beta", string.Empty, 11, "Gladiator", 38_000, ServerId: 1503);
            service.Take(Progress(beta, (EntryIds.ShugoFestivalKeys, 9, 0, Monday.AddHours(1)))); // another character on it
            Assert.Equal(new RosterShugoKeys(1503, 9, 0, Monday.AddHours(1)), service.ShugoKeys[1503]);
            service.Take(Progress(alpha, (EntryIds.ShugoFestivalKeys, 12, 3, Monday))); // read earlier: not taken
            Assert.Equal(9, service.ShugoKeys[1503].Keys);
            service.Take(Progress(alpha with { ServerId = 0 }, (EntryIds.ShugoFestivalKeys, 6, 0, Monday.AddHours(2)))); // the roster knows Alpha's
            Assert.Equal(6, service.ShugoKeys[1503].Keys);
            service.Take(Progress(new CharacterIdentity("Gamma", string.Empty, 0, string.Empty, 0), (EntryIds.ShugoFestivalKeys, 1, 0, Monday.AddHours(2))));
            Assert.Single(service.ShugoKeys); // Gamma's server is not known
            service.SaveNow();
        }
        Assert.Equal([new RosterShugoKeys(1503, 6, 0, Monday.AddHours(2))], store.Load(Monday.AddHours(2)).File.ShugoKeys);

        // Keys saved while the PC's clock was ahead (dated after now) do not count as later than anything, or no reading
        // would replace them until the clock caught up (deep audit on #60).
        var aheadStore = NewStore(demo: true);
        aheadStore.Save(new RosterFile { ShugoKeys = [new RosterShugoKeys(1503, 20, 0, Monday.AddHours(12))] });
        using var ahead = new RosterService(new FakeEngine(), aheadStore, clock, NullLogger<RosterService>.Instance);
        ahead.Take(Progress(AlphaIdentity with { Server = string.Empty, ServerId = 1503 }, (EntryIds.ShugoFestivalKeys, 4, 0, Monday)));
        Assert.Equal(new RosterShugoKeys(1503, 4, 0, Monday), ahead.ShugoKeys[1503]);
    }

    // The overlay's last known values are for the character the game named last this run, or else the one played last
    // (the meter started while it was in the game), with the keys saved for its server; none for a character whose server
    // is not known, nothing once the character named is removed (another's would read as its: deep audit on #60), and
    // nothing with an empty roster.
    [Fact]
    public void TheLastKnownIsTheCharacterNamedOrElseTheOnePlayedLast()
    {
        var store = NewStore();
        store.Save(new RosterFile
        {
            Characters =
            [
                new() { Name = "Alpha", Server = string.Empty, ServerId = 1503, LastSeen = Monday, Odyle = Seen(500, 100, Monday) },
                new() { Name = "Beta", Server = string.Empty, ServerId = 2101, LastSeen = Monday.AddHours(1), Odyle = Seen(300, 0, Monday.AddHours(1)) },
                new() { Name = "Gamma", Server = string.Empty, LastSeen = Monday.AddMinutes(30) },
            ],
            ShugoKeys = [new RosterShugoKeys(1503, 12, 3, Monday), new RosterShugoKeys(2101, 20, 0, Monday)],
        });
        var clock = new FakeTimeProvider(Monday.AddHours(2));
        using var service = new RosterService(new FakeEngine(), store, clock, NullLogger<RosterService>.Instance);
        var last = service.LastKnown!;
        Assert.Equal(("Beta", 20), (last.Character.Name, last.Shugo!.Keys)); // played last
        service.Take(Progress(new CharacterIdentity("Alpha", string.Empty, 0, string.Empty, 0)));
        last = service.LastKnown!;
        Assert.Equal(("Alpha", 12), (last.Character.Name, last.Shugo!.Keys)); // named this run
        service.Take(Progress(new CharacterIdentity("Gamma", string.Empty, 0, string.Empty, 0)));
        Assert.Null(service.LastKnown!.Shugo); // its server is not known
        service.Remove(new RosterKey(string.Empty, "Gamma"));
        Assert.Null(service.LastKnown); // the one named is gone: none, rather than another character's
        using var empty = new RosterService(new FakeEngine(), NewStore(demo: true), clock, NullLogger<RosterService>.Instance);
        Assert.Null(empty.LastKnown);
    }

    // Saved Shugo keys are checked as the artifact holders are: on a server with a side, counts within the entry list's
    // bound and a usable date; one per server, the one read last. Nothing said names a server.
    [Fact]
    public void SavedShugoKeysAreChecked()
    {
        var store = NewStore();
        File.WriteAllText(store.FilePath, """
            { "version": 1, "characters": [],
              "shugoKeys": [
                { "serverId": 1503, "keys": 12, "extra": 3, "seenAt": "2026-10-24T01:00:00+00:00" },
                { "serverId": 1503, "keys": 15, "extra": 0, "seenAt": "2026-10-25T01:00:00+00:00" },
                { "serverId": 2101, "keys": 21, "extra": 1000000, "seenAt": "2026-10-25T01:00:00+00:00" },
                { "serverId": 7, "keys": 3, "extra": 0, "seenAt": "2026-10-25T01:00:00+00:00" },
                { "serverId": 2102, "keys": -1, "extra": 0, "seenAt": "2026-10-25T01:00:00+00:00" },
                { "serverId": 2103, "keys": 1000001, "extra": 0, "seenAt": "2026-10-25T01:00:00+00:00" },
                { "serverId": 2104, "keys": 3, "extra": 0, "seenAt": "2019-10-25T01:00:00+00:00" },
                null
              ] }
            """);
        var loaded = store.Load(Monday);
        Assert.Equal(
            [new RosterShugoKeys(1503, 15, 0, new DateTimeOffset(2026, 10, 25, 1, 0, 0, TimeSpan.Zero)), new RosterShugoKeys(2101, 21, 1_000_000, new DateTimeOffset(2026, 10, 25, 1, 0, 0, TimeSpan.Zero))],
            loaded.File.ShugoKeys);
        Assert.Contains("5 unusable Shugo Festival key records dropped.", loaded.Notice, StringComparison.Ordinal);
        Assert.Contains("1 Shugo Festival key record listed twice or past the 64 kept dropped.", loaded.Notice, StringComparison.Ordinal);
        Assert.DoesNotContain("1503", loaded.Notice, StringComparison.Ordinal);
        Assert.Empty(RosterNormalizer.Normalize(loaded.File, Monday).Repairs); // clean once repaired

        // No more than 64 servers' keys, the ones read most recently (deep audit on #60).
        var many = Enumerable.Range(1, 65).Select(i => new RosterShugoKeys(i <= 23 ? 1300 + i : 2300 + i - 23, 3, 0, Monday.AddMinutes(-i))).ToList();
        var (trimmed, repairs) = RosterNormalizer.Normalize(new RosterFile { ShugoKeys = many }, Monday);
        Assert.Equal(RosterNormalizer.MaxServers, trimmed.ShugoKeys.Count);
        Assert.DoesNotContain(trimmed.ShugoKeys, keys => keys.SeenAt == Monday.AddMinutes(-65)); // the oldest goes
        Assert.Contains("1 Shugo Festival key record listed twice or past the 64 kept dropped.", repairs);
    }

    // The overlay's last known values are the roster's estimates: basic energy refilled up to the character's cap (840 by
    // default), charged as it was; the keys +3 at each daily reset up to 21, the extra as it was; none without a reading.
    [Fact]
    public void TheLastKnownValuesAreTheRostersEstimates()
    {
        var alpha = new RosterCharacter { Name = "Alpha", Odyle = Seen(500, 1200, Monday) };
        Assert.Equal(new OdyleEnergy(620, 1200), LastKnownValues.Odyle(alpha, Monday.AddDays(1), Calendar)); // eight refills
        Assert.Equal(new OdyleEnergy(560, 1200), LastKnownValues.Odyle(alpha with { OdyleCap = OdyleCapChoice.Standard }, Monday.AddDays(5), Calendar));
        Assert.Equal(new OdyleEnergy(840, 1200), LastKnownValues.Odyle(alpha, Monday.AddDays(5), Calendar));
        Assert.Equal(new OdyleEnergy(500, 1200), LastKnownValues.Odyle(alpha, Monday, Calendar)); // as the game said
        Assert.Null(LastKnownValues.Odyle(alpha with { Odyle = null }, Monday, Calendar));

        var keys = new RosterShugoKeys(1503, 12, 3, Monday);
        Assert.Equal(new ShugoKeys(12, 3), LastKnownValues.Shugo(keys, Monday, Calendar));
        Assert.Equal(new ShugoKeys(15, 3), LastKnownValues.Shugo(keys, Monday.AddDays(1), Calendar)); // one daily reset
        Assert.Equal(new ShugoKeys(21, 3), LastKnownValues.Shugo(keys, Monday.AddDays(5), Calendar)); // up to 21
        Assert.Equal(new ShugoKeys(25, 3), LastKnownValues.Shugo(keys with { Keys = 25 }, Monday.AddDays(5), Calendar)); // more is kept
        Assert.Null(LastKnownValues.Shugo(null, Monday, Calendar));
    }

    // What the overlay shows: the game's values where it has sent them, each on its own, the roster never asked when it
    // has sent both; for one it has not, the roster's last known, estimated and flagged; nothing flagged without either
    // (deep audit on #60: the page's choice was tested only as source text).
    [Fact]
    public void TheOverlayShowsTheGamesValuesFirstAndTheLastKnownForTheRest()
    {
        var game = (Odyle: new OdyleEnergy(700, 50), Shugo: new ShugoKeys(9, 0));
        var last = new RosterLastKnown(new RosterCharacter { Name = "Alpha", Odyle = Seen(500, 1200, Monday) }, new RosterShugoKeys(1503, 12, 3, Monday));
        var later = Monday.AddDays(1);
        RosterLastKnown Unasked() => throw new InvalidOperationException("the roster was asked though the game had sent both");

        Assert.Equal(new OverlayEnergy(game.Odyle, false, game.Shugo, false), LastKnownValues.Shown(game.Odyle, game.Shugo, Unasked, later, Calendar));
        Assert.Equal(new OverlayEnergy(new OdyleEnergy(620, 1200), true, game.Shugo, false), LastKnownValues.Shown(null, game.Shugo, () => last, later, Calendar));
        Assert.Equal(new OverlayEnergy(game.Odyle, false, new ShugoKeys(15, 3), true), LastKnownValues.Shown(game.Odyle, null, () => last, later, Calendar));
        Assert.Equal(new OverlayEnergy(null, false, null, false), LastKnownValues.Shown(null, null, () => null, later, Calendar));
        Assert.Equal(new OverlayEnergy(null, false, null, false), // a character with no reading of either
            LastKnownValues.Shown(null, null, () => new RosterLastKnown(new RosterCharacter { Name = "Beta" }, null), later, Calendar));
    }

    // The overlay's calendar is made again when the server offset in Settings changes, and only then (deep audit on #60:
    // a page that kept the first one would estimate with the old offset until restart).
    [Fact]
    public void TheCalendarFollowsTheServerOffset()
    {
        var cache = new RechargeCalendarCache();
        var nine = cache.For(9);
        Assert.Same(nine, cache.For(9));
        var eight = cache.For(8);
        Assert.NotSame(nine, eight);
        Assert.NotEqual(nine.LastSiege(Monday), eight.LastSiege(Monday)); // the server-time schedule moves with the offset
        Assert.Same(eight, cache.For(8));
    }
}