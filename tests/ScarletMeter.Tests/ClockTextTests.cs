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
    [InlineData(1, 34, "1:34am")]
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

    // No 24-hour clock is left for a player to read: no "HH" format in the app's code or pages (the log file's own, which
    // sorts and reads the same everywhere, is the one kept), no culture's own short time (24-hour in most languages), and no
    // schedule hour padded and joined to its minutes by hand ("{h:00}:{minute:00}"). A new time goes through ClockText, the
    // one file that joins a clock's parts itself.
    [Fact]
    public void NoTwentyFourHourClockIsLeftInTheApp()
    {
        var app = Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App");
        var kept = Path.Combine(app, "Logging", "LogFloodGuard.cs");
        var clock = Path.Combine(app, "Formatting", "ClockText.cs");
        var found = new List<string>();
        foreach (var file in Directory.EnumerateFiles(app, "*.*", SearchOption.AllDirectories))
        {
            if (!(file.EndsWith(".cs", StringComparison.Ordinal) || file.EndsWith(".razor", StringComparison.Ordinal))) continue;
            var relative = Path.GetRelativePath(app, file);
            if (relative.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || relative.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || string.Equals(file, kept, StringComparison.OrdinalIgnoreCase)
                || string.Equals(file, clock, StringComparison.OrdinalIgnoreCase)) continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var code = lines[i].TrimStart();
                if (code.StartsWith("//", StringComparison.Ordinal) || code.StartsWith("///", StringComparison.Ordinal)) continue;
                if (Regex.IsMatch(lines[i], @"\bHH\b|ToShortTimeString|ToLongTimeString|ToString\(""[tT]""|:00\}:\{"))
                    found.Add($"{relative}:{i + 1}: {code}");
            }
        }
        Assert.True(File.Exists(kept) && File.Exists(clock), "the files this test leaves out are where it expects them");
        Assert.True(found.Count == 0, "24-hour times left in the app:\n" + string.Join("\n", found));
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ScarletMeter.slnx"))) return dir.FullName;
        throw new InvalidOperationException("ScarletMeter.slnx was not found above " + AppContext.BaseDirectory);
    }
}
