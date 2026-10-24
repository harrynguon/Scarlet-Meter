using System.IO;
using System.Text.RegularExpressions;
using ScarletMeter.App.Formatting;

namespace ScarletMeter.Tests;

/// <summary>
/// Each class's bar effect: every player class has a theme of its own, anything else gets lightning, and every
/// theme has styles in app.css (so a theme cannot be added without them).
/// </summary>
public sealed class BarEffectThemesTests
{
    private static readonly int[] PlayerClasses = [11, 12, 13, 14, 15, 16, 17, 18, 19];

    [Fact]
    public void EveryPlayerClassHasItsOwnTheme()
    {
        var themes = PlayerClasses.Select(BarEffectThemes.For).ToList();
        Assert.Equal(themes.Count, themes.Distinct().Count());
        Assert.DoesNotContain(BarEffectThemes.Default, themes);
        Assert.Equal("fire-ice", BarEffectThemes.For(15)); // Sorcerer
        Assert.Equal("venom", BarEffectThemes.For(13));    // Assassin
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)] // an Elementalist's spirit
    [InlineData(99)]
    [InlineData(-1)]
    public void AnythingElseGetsLightning(int classId) =>
        Assert.Equal(BarEffectThemes.Default, BarEffectThemes.For(classId));

    [Fact]
    public void TheListNamesEveryThemeOnce()
    {
        Assert.Equal(BarEffectThemes.All.Count, BarEffectThemes.All.Distinct().Count());
        foreach (var theme in PlayerClasses.Select(BarEffectThemes.For).Append(BarEffectThemes.Default))
            Assert.Contains(theme, BarEffectThemes.All);
    }

    [Fact]
    public void EveryThemeAndPieceHasStyles()
    {
        var css = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App", "wwwroot", "css", "app.css"));
        // The whole name, not a prefix of another (".flush" must not pass on ".flushx").
        static bool Styled(string css, string selector) =>
            Regex.IsMatch(css, Regex.Escape(selector) + @"(?![\w-])");
        foreach (var theme in BarEffectThemes.All)
        {
            Assert.True(Styled(css, $".theme-{theme}"), theme);
            foreach (var piece in BarEffectThemes.Pieces(theme).Distinct())
                Assert.True(Styled(css, $".theme-{theme} .{piece}"), $"{theme} {piece}");
        }
    }

    [Fact]
    public void LightningDrawsNoPiecesAndTheOthersDo()
    {
        Assert.Empty(BarEffectThemes.Pieces(BarEffectThemes.Default));
        foreach (var theme in BarEffectThemes.All.Where(t => t != BarEffectThemes.Default))
            Assert.NotEmpty(BarEffectThemes.Pieces(theme));
    }

    // The directory holding ScarletMeter.slnx, above the test assembly (never the working directory).
    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ScarletMeter.slnx"))) return dir.FullName;
        throw new InvalidOperationException("ScarletMeter.slnx was not found above " + AppContext.BaseDirectory);
    }
}
