using ScarletMeter.App.Formatting;
using ScarletMeter.Engine;

namespace ScarletMeter.Tests;

/// <summary>Helpers behind the details and history pages: buff lanes, RATmeter's Copy DPS line, icon URLs and hit tags.</summary>
public sealed class DetailFormatTests
{
    [Fact]
    public void BuffSpansMergePerBuffAndSortByUptime()
    {
        var spans = new[]
        {
            new BuffSpan(1, "Blessing", null, 0, 10, 1),
            new BuffSpan(1, "Blessing", null, 5, 20, 1),    // overlaps the first: merged into 0-20
            new BuffSpan(1, "Blessing", null, 40, 50, 1),
            new BuffSpan(2, "Haste", null, 0, 90, 1),
            new BuffSpan(3, "Shield", null, 95, 200, 1),    // clipped to the fight's 100 seconds
            new BuffSpan(4, "Nothing", null, 30, 30, 1),    // empty: dropped
        };
        var lanes = DetailFormat.Chart(new DamageGraph(100, [], spans)).Lanes;

        Assert.Equal(new[] { "Haste", "Blessing", "Shield" }, lanes.Select(l => l.Name));
        var blessing = lanes[1];
        Assert.Equal(new[] { (0.0, 20.0), (40.0, 50.0) }, blessing.Spans);
        Assert.Equal(30, blessing.UptimePercent, 6);
        Assert.Equal(3, blessing.Count);
        Assert.Equal(5, lanes[2].UptimePercent, 6);
    }

    [Fact]
    public void ChartPointsCarryDamageAndCumulativeDps()
    {
        var chart = DetailFormat.Chart(new DamageGraph(2, [new GraphPoint(0, 100, 100), new GraphPoint(1, 300, 200)], []));
        Assert.Equal(2, chart.TotalSeconds);
        Assert.Equal(new ChartPoint(1, 300, 200), chart.Points[1]);
    }

    [Fact]
    public void CopyDpsMatchesRatmetersFormatForTheTopTen()
    {
        var players = Enumerable.Range(1, 12)
            .Select(i => new PlayerRow(i, $"P{i}", false, true, 11, "Gladiator", "", 0, i * 1_000_000, i * 10_000, 0, 0, 0))
            .ToList();
        var line = CopyDps.Format(players, p => p.Name);
        Assert.StartsWith("P12 : 120.0K DPS (12.00M dmg), P11 : 110.0K DPS (11.00M dmg)", line);
        Assert.Equal(10, line.Split(", ").Length);
        Assert.DoesNotContain("P2 :", line);
        Assert.Equal("********", CopyDps.Format([players[0] with { Name = "Abcdefgh" }], _ => "********").Split(' ')[0]);
    }

    [Theory]
    [InlineData("https://assets.playnccdn.com/static-aion2-gamedata/resources/ICON_GL_SKILL_001.png", true)]
    [InlineData("https://assets.playnccdn.com/other/ICON.png", false)]
    [InlineData("http://assets.playnccdn.com/static-aion2-gamedata/resources/ICON.png", false)]
    [InlineData("https://assets.playnccdn.com.evil.example/static-aion2-gamedata/resources/ICON.png", false)]
    [InlineData("https://assets.playnccdn.com/static-aion2-gamedata/resources/../../x.png", false)]
    [InlineData(null, false)]
    public void SkillIconsOnlyComeFromNcsCdn(string? icon, bool allowed) =>
        Assert.Equal(allowed ? icon : null, SkillIcons.Url(icon, enabled: true));

    [Fact]
    public void SkillIconsCanBeTurnedOff() =>
        Assert.Null(SkillIcons.Url("https://assets.playnccdn.com/static-aion2-gamedata/resources/ICON_GL_SKILL_001.png", enabled: false));

    [Fact]
    public void HitFlagsReadAsRatmetersTags() =>
        Assert.Equal(new[] { "CRIT", "BACK", "x2" }, DetailFormat.Tags(HitFlags.DoubleDamage | HitFlags.Critical | HitFlags.BackAttack));
}
