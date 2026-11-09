using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using ScarletMeter.App.Formatting;

namespace ScarletMeter.Tests;

/// <summary>
/// The 12-hour clock every time a player reads is written in (Formatting/ClockText; Harry, 2026-11-09: "change all
/// timestamps from 24h to 12hr format like 9:34pm, 9:34am"), and a guard that no 24-hour format is left in the app.
/// </summary>
public sealed class ClockTextTests
{
    // Midnight and noon are 12, the hour has no leading zero, the minutes always two digits, am or pm in lowercase
    // straight after.
    [Theory]
    [InlineData(0, 0, "12:00am")]
    [InlineData(0, 5, "12:05am")]
    [InlineData(1, 15, "1:15am")]
    [InlineData(9, 34, "9:34am")]
    [InlineData(11, 59, "11:59am")]
    [InlineData(12, 0, "12:00pm")]
    [InlineData(12, 30, "12:30pm")]
    [InlineData(13, 0, "1:00pm")]
    [InlineData(21, 34, "9:34pm")]
    [InlineData(23, 59, "11:59pm")]
    public void TimesReadOnTheTwelveHourClock(int hour, int minute, string expected)
    {
        Assert.Equal(expected, ClockText.Time(hour, minute));
        Assert.Equal(expected, ClockText.Time(new DateTime(2026, 11, 9, hour, minute, 42)));
    }

    // The combat log's times keep their seconds and milliseconds.
    [Fact]
    public void PreciseTimesKeepTheirSecondsAndMilliseconds()
    {
        Assert.Equal("9:05:33.120pm", ClockText.Precise(new DateTime(2026, 11, 9, 21, 5, 33, 120)));
        Assert.Equal("12:00:00.007am", ClockText.Precise(new DateTime(2026, 11, 9, 0, 0, 0, 7)));
    }

    // The same words whatever the PC's language: Japanese writes 午前 and 午後 for am and pm, German 24-hour times.
    [Theory]
    [InlineData("ja-JP")]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    public void TimesDoNotFollowThePcsLanguage(string culture)
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            Assert.Equal("9:34pm", ClockText.Time(new DateTime(2026, 11, 9, 21, 34, 0)));
            Assert.Equal("9:05:33.120am", ClockText.Precise(new DateTime(2026, 11, 9, 9, 5, 33, 120)));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(24, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 60)]
    public void AnHourOrMinuteOutsideTheDayIsRefused(int hour, int minute) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ClockText.Time(hour, minute));

    // The words the converted callers write: a fight's date and time, the combat log's, the pet window's login line, and the
    // History window's dates out of order said in dates (not the engine's 24-hour message).
    [Fact]
    public void TheConvertedCallersWriteTheTwelveHourClock()
    {
        Assert.Equal("Sat 24 Oct 9:05pm", DetailFormat.When(new DateTime(2026, 10, 24, 21, 5, 59)));
        Assert.Equal("Sun 25 Oct 12:00am", DetailFormat.When(new DateTime(2026, 10, 25, 0, 0, 0)));
        Assert.Equal("9:05:33.120pm", DetailFormat.LogTime(new DateTime(2026, 10, 24, 21, 5, 33, 120)));
        var login = new DateTimeOffset(new DateTime(2026, 10, 24, 21, 5, 0, DateTimeKind.Local));
        Assert.Equal("from the game at your login of 24 Oct 9:05pm, and each soul since", ScarletMeter.App.Pets.PetText.Synced(login));

        Assert.Equal("The From date (9 Nov) is after the To date (8 Nov).", DetailFormat.RangeProblem(new DateTime(2026, 11, 9), new DateTime(2026, 11, 8)));
        Assert.Null(DetailFormat.RangeProblem(new DateTime(2026, 11, 8, 23, 0, 0), new DateTime(2026, 11, 8))); // one day, whatever the hour
        Assert.Null(DetailFormat.RangeProblem(new DateTime(2026, 11, 9), null));
        Assert.Null(DetailFormat.RangeProblem(null, new DateTime(2026, 11, 8)));

        // The page says it before it asks the engine, whose own message has 24-hour times.
        var page = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App", "Pages", "FightHistoryPage.razor.cs"));
        var check = page.IndexOf("if (DetailFormat.RangeProblem(fromFilter, toFilter) is { } problem)", StringComparison.Ordinal);
        Assert.True(check > 0 && check < page.IndexOf(".Validate();", StringComparison.Ordinal), "the History window checks its dates before the query");
    }

    // What the guard below treats as a 24-hour or culture-dependent time in C# and Razor: an "HH" or "H:mm" format (also
    // "HHmm"), a culture's short or long time, a standard format that carries a time ("t", "T", "g", "G", "f", "F") in
    // ToString or in an interpolation or composite format, and an hour and minutes joined by hand ("{h:00}:{m:00}",
    // "{hours[0]}:{m:D2}"; an hour is a value named h, hour or hours, so a countdown's "{minutes}:{seconds:00}" and a
    // duration's "{TotalHours:D2}:{Minutes:D2}" are let be). In the app's scripts: the browser's time formatting. It cannot
    // see a DateTime written with no format at all ($"{at}"), which a review has to catch.
    private static readonly Regex TwentyFourHour = new(
        @"\bH{1,2}:?mm|\bHH\b|ToShortTimeString|ToLongTimeString|ToString\(""[tTgGfF]""|\{[^{}""]*:[tTgGfF]\}"
        + @"|\{[^{}]*(\b[hH]\b|(?<!Total)[hH]ours?)[^{}]*\}:\{[^{}]*:(00|D2)\}",
        RegexOptions.CultureInvariant);

    private static readonly Regex BrowserTime = new(@"toLocaleTimeString|toTimeString|toLocaleString\(|getHours\(", RegexOptions.CultureInvariant);

    [Theory]
    [InlineData(@"time.ToString(""HH:mm"", CultureInfo.InvariantCulture)")]
    [InlineData(@"$""{at:HH:mm}""")]
    [InlineData(@"time.ToString(""H:mm"")")]
    [InlineData(@"time.ToString(""HHmm"")")]
    [InlineData(@"$""{at:t}""")]
    [InlineData(@"string.Format(""{0:t}"", at)")]
    [InlineData(@"time.ToString(""g"")")]
    [InlineData(@"time.ToShortTimeString()")]
    [InlineData(@"$""{h:00}:{m:00}""")]
    [InlineData(@"$""{h}:{m:00}""")]
    [InlineData(@"$""{h:D2}:{m:D2}""")]
    [InlineData(@"$""{hours[0]:00}:{rule.Minute:00}""")]
    public void TheGuardSeesEachWayOfWritingATwentyFourHourTime(string line) => Assert.Matches(TwentyFourHour, line);

    [Theory]
    [InlineData(@"ClockText.Time(start.DateTime)")]
    [InlineData(@"time.ToString(""ddd d MMM "", CultureInfo.InvariantCulture)")]
    [InlineData(@"$""{remaining.Minutes:00}""")]
    [InlineData(@"$""{(int)remaining.TotalMinutes}:{remaining.Seconds:00}""")]
    [InlineData(@"$""{(int)duration.TotalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}""")]
    [InlineData(@"$""{start:d MMM}""")]
    [InlineData(@"value.ToString(""N0"", CultureInfo.InvariantCulture)")]
    public void TheGuardLetsDatesCountdownsAndNumbersBe(string line) => Assert.DoesNotMatch(TwentyFourHour, line);

    // No 24-hour clock is left for a player to read in the app's code, pages and scripts (the patterns above). Four lines
    // keep one on purpose, each named here by its own text so the list cannot outlive them: the log file's gap summary (the
    // log sorts and reads the same everywhere), the UTC stamp in the name a file unreadable at start is kept aside under,
    // and ClockText's own two joinings of a clock's parts. A new time goes through ClockText.
    [Fact]
    public void NoTwentyFourHourClockIsLeftInTheApp()
    {
        var app = Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App");
        (string File, string Text)[] kept =
        [
            (Path.Combine("Logging", "LogFloodGuard.cs"), "from {First:HH:mm:ss.fff} to {Last:HH:mm:ss.fff}"),
            (Path.Combine("Storage", "ProtectedJsonFile.cs"), "{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}"),
            (Path.Combine("Formatting", "ClockText.cs"), "{Hour(hour)}:{minute:00}{Half(hour)}"),
            (Path.Combine("Formatting", "ClockText.cs"), "{Hour(time.Hour)}:{time.Minute:00}:{time.Second:00}"),
        ];
        var used = new HashSet<int>();
        var found = new List<string>();
        foreach (var file in Directory.EnumerateFiles(app, "*.*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(app, file);
            if (relative.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || relative.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
            var pattern = file.EndsWith(".cs", StringComparison.Ordinal) || file.EndsWith(".razor", StringComparison.Ordinal) ? TwentyFourHour
                : file.EndsWith(".js", StringComparison.Ordinal) ? BrowserTime : null;
            if (pattern is null) continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var code = lines[i].TrimStart();
                if (code.StartsWith("//", StringComparison.Ordinal) || !pattern.IsMatch(lines[i])) continue;
                var exception = Array.FindIndex(kept, k => string.Equals(k.File, relative, StringComparison.OrdinalIgnoreCase)
                    && lines[i].Contains(k.Text, StringComparison.Ordinal));
                if (exception >= 0) used.Add(exception);
                else found.Add($"{relative}:{i + 1}: {code}");
            }
        }
        Assert.True(found.Count == 0, "24-hour times left in the app:\n" + string.Join("\n", found));
        Assert.True(used.Count == kept.Length, "an exception no longer matches its line: "
            + string.Join(", ", kept.Where((_, i) => !used.Contains(i)).Select(k => k.File)));
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ScarletMeter.slnx"))) return dir.FullName;
        throw new InvalidOperationException("ScarletMeter.slnx was not found above " + AppContext.BaseDirectory);
    }
}
