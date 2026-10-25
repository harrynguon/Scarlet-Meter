using ScarletMeter.App.BossTimers;

namespace ScarletMeter.Tests;

/// <summary>
/// The boss-timers window's lock: only aion2timers.com's own https pages show in it; a web page elsewhere goes to the
/// default browser, and only when the player clicked it; everything else is refused.
/// </summary>
public sealed class BossTimersSiteTests
{
    [Fact]
    public void TheWindowOpensOnTheSitesWorldBossTimers()
    {
        Assert.Equal("https://www.aion2timers.com/world-boss-timer/", BossTimersSite.Start.AbsoluteUri);
        Assert.Equal(SiteNavigation.Allow, BossTimersSite.Decide(BossTimersSite.Start.AbsoluteUri, playerAsked: false));
    }

    [Theory]
    [InlineData("https://www.aion2timers.com/")]
    [InlineData("https://aion2timers.com/world-boss-timer/")]
    [InlineData("https://WWW.AION2TIMERS.COM/world-boss-timer/?server=1&faction=2")]
    [InlineData("https://www.aion2timers.com/other-page/#section")]
    [InlineData("https://www.aion2timers.com:443/world-boss-timer/")]
    public void TheSitesOwnPagesShowEvenWithoutAClick(string address)
    {
        Assert.Equal(SiteNavigation.Allow, BossTimersSite.Decide(address, playerAsked: false));
        Assert.Equal(SiteNavigation.Allow, BossTimersSite.Decide(address, playerAsked: true));
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=x")]
    [InlineData("http://example.org/")]
    [InlineData("http://www.aion2timers.com/world-boss-timer/")] // the site, but not over https
    [InlineData("https://www.aion2timers.com:8443/")] // the site's host on another port
    [InlineData("https://aion2timers.com.example.org/")] // lookalikes
    [InlineData("https://evil-aion2timers.com/")]
    [InlineData("https://aion2timers.com./")]
    [InlineData("https://www.a\u0456on2timers.com/")] // a Cyrillic "i"
    public void AnotherWebPageOpensInTheBrowserOnlyWhenThePlayerClickedIt(string address)
    {
        Assert.Equal(SiteNavigation.OpenInBrowser, BossTimersSite.Decide(address, playerAsked: true));
        Assert.Equal(SiteNavigation.Block, BossTimersSite.Decide(address, playerAsked: false));
    }

    [Theory]
    [InlineData("https://user:password@www.aion2timers.com/")]
    [InlineData("https://@www.aion2timers.com/")]
    [InlineData("https://www.aion2timers.com@example.org/")]
    [InlineData("http://user@example.org/")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hi")]
    [InlineData("ms-settings:privacy")]
    [InlineData("mailto:someone@example.org")]
    [InlineData("about:blank")]
    [InlineData("/world-boss-timer/")]
    [InlineData("")]
    [InlineData(null)]
    public void EverythingElseIsRefusedEvenAfterAClick(string? address)
    {
        Assert.Equal(SiteNavigation.Block, BossTimersSite.Decide(address, playerAsked: true));
        Assert.Equal(SiteNavigation.Block, BossTimersSite.Decide(address, playerAsked: false));
    }

    [Fact]
    public void OnlyWebPagesWithoutCredentialsCanReachTheBrowser()
    {
        Assert.True(BossTimersSite.IsWebPage(new Uri("https://example.org/page")));
        Assert.False(BossTimersSite.IsWebPage(new Uri("https://name@example.org/")));
        Assert.False(BossTimersSite.IsWebPage(new Uri("file:///C:/Windows/")));
        Assert.False(BossTimersSite.IsWebPage(new Uri("ftp://example.org/")));
    }
}
