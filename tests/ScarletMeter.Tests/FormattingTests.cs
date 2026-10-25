using ScarletMeter.App.Combat;
using ScarletMeter.App.Formatting;
using ScarletMeter.App.Windows;

namespace ScarletMeter.Tests;

/// <summary>Display helpers: RATmeter-compatible name masking, ping bands, class colours and the image URLs the pages may load.</summary>
public sealed class FormattingTests
{
    [Theory]
    [InlineData(10, "class-10")]
    [InlineData(19, "class-19")]
    [InlineData(9, "class-0")]
    [InlineData(20, "class-0")]
    [InlineData(0, "class-0")]
    public void ClassIdsMapToTheirGradientOrTheNeutralOne(int classId, string css) =>
        Assert.Equal(css, ClassColors.CssClass(classId));

    [Theory]
    [InlineData("", "")]
    [InlineData("Ab", "**")]
    [InlineData("Abcd", "A***")]
    public void ShortNamesAreFullyOrMostlyMasked(string name, string masked) => Assert.Equal(masked, NicknameMask.Mask(name));

    [Fact]
    public void LongNamesKeepTwoLettersAndOneStableDigit()
    {
        var masked = NicknameMask.Mask("Abcdefg");
        Assert.Equal("Ab**3**", masked); // FNV-1a("Abcdefg") = 1531417303, as upstream computes it
        Assert.Equal(masked, NicknameMask.Mask("Abcdefg"));
        Assert.Equal("Abcdefg".Length, masked.Length);
    }

    [Theory]
    [InlineData(null, "none")]
    [InlineData(12, "good")]
    [InlineData(60, "fair")]
    [InlineData(150, "slow")]
    [InlineData(450, "bad")]
    public void PingBands(int? ping, string level) => Assert.Equal(level, PingLevel.Of(ping));

    // The header ring's colour by the target's HP (Harry: "green = 66->100% hp, yellow = 33-66% hp, red = 0.1-33% hp"):
    // each boundary belongs to the band above it; 0 (down) has no band.
    [Theory]
    [InlineData(100, "hp-high")]
    [InlineData(66, "hp-high")]
    [InlineData(65.99, "hp-mid")]
    [InlineData(33, "hp-mid")]
    [InlineData(32.99, "hp-low")]
    [InlineData(0.1, "hp-low")]
    [InlineData(0.001, "hp-low")]
    [InlineData(0, "")]
    public void HpBands(double hpPercent, string band) => Assert.Equal(band, HpBand.Of(hpPercent));

    // The ring for the fight's phase and its target: the band while the fight runs, " still" once the HP has stopped
    // moving, a neutral ring while the target's total HP is unknown (HpPercent reads 0 then, which would otherwise
    // look like no fight: audit on PR #32), grey when the target is down, nothing with no target.
    [Theory]
    [InlineData(FightPhase.Live, 900L, 1000L, "hp-high")]
    [InlineData(FightPhase.Live, 500L, 1000L, "hp-mid")]
    [InlineData(FightPhase.Live, 100L, 1000L, "hp-low")]
    [InlineData(FightPhase.Settled, 500L, 1000L, "hp-mid still")]
    [InlineData(FightPhase.Live, 500L, 0L, "hp-unknown")]
    [InlineData(FightPhase.Settled, 500L, 0L, "hp-unknown still")]
    [InlineData(FightPhase.Defeated, 0L, 1000L, "done")]
    [InlineData(FightPhase.None, 500L, 1000L, "")]
    public void HeaderRings(FightPhase phase, long hp, long hpTotal, string ring) =>
        Assert.Equal(ring, HpBand.RingClass(phase, new ScarletMeter.Engine.TargetInfo(1, "Synthetic boss", hp, hpTotal, IsBoss: true)));

    [Fact]
    public void NoRingWithoutATarget()
    {
        Assert.Equal(string.Empty, HpBand.RingClass(FightPhase.Live, null));
        Assert.Equal("done", HpBand.RingClass(FightPhase.Defeated, null));
    }

    // Odyle energy the way the game writes it: basic, then charged in brackets, with thousands separators whatever
    // the PC's culture.
    [Fact]
    public void OdyleEnergyReadsAsTheGameWritesIt()
    {
        var energy = new ScarletMeter.Engine.OdyleEnergy(1240, 320);
        Assert.Equal("1,240", OdyleText.Basic(energy));
        Assert.Equal("(+320)", OdyleText.Charged(energy));
        Assert.Equal("(+12,000)", OdyleText.Charged(new ScarletMeter.Engine.OdyleEnergy(0, 12000)));
        Assert.Equal("0", OdyleText.Basic(new ScarletMeter.Engine.OdyleEnergy(0, 0)));
    }

    [Fact]
    public void PagesOnlyGetUrlsOnTheLocalHosts()
    {
        Assert.Equal("https://data.scarlet.example/background-0123abcd.png", WebAssetHosts.BackgroundUrl("background-0123abcd.png"));
        Assert.Null(WebAssetHosts.BackgroundUrl(@"C:\Windows\win.ini"));
        Assert.Null(WebAssetHosts.BackgroundUrl(null));
        Assert.Equal("https://assets.scarlet.example/classes/15.png", WebAssetHosts.ClassIconUrl(15));
        Assert.Null(WebAssetHosts.ClassIconUrl(10));
    }
}
