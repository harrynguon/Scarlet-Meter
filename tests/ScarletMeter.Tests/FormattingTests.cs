using ScarletMeter.App.Formatting;
using ScarletMeter.App.Windows;

namespace ScarletMeter.Tests;

/// <summary>Display helpers: RATmeter-compatible name masking, ping bands and the image URLs the pages may load.</summary>
public sealed class FormattingTests
{
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
