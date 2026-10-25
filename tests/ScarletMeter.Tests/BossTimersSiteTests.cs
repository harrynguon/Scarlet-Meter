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
        Assert.True(BossTimersSite.IsWebPage(new Uri("https://93.184.215.14/")));
        Assert.False(BossTimersSite.IsWebPage(new Uri("https://name@example.org/")));
        Assert.False(BossTimersSite.IsWebPage(new Uri("file:///C:/Windows/")));
        Assert.False(BossTimersSite.IsWebPage(new Uri("ftp://example.org/")));
    }

    // A page cannot use the player's click to reach this PC or the local network through the browser (deep audit on
    // PR #29: loopback, the meter's own 0.0.0.1 and private addresses all reached the browser).
    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://127.8.9.10:8080/")]
    [InlineData("http://localhost:5000/")]
    [InlineData("http://app.localhost/")]
    [InlineData("http://0.0.0.1/")]
    [InlineData("https://[::1]/")]
    [InlineData("https://[::ffff:127.0.0.1]/")]
    [InlineData("http://10.0.0.5/")]
    [InlineData("http://172.16.0.1/")]
    [InlineData("http://172.31.255.254/")]
    [InlineData("http://192.168.1.1/admin")]
    [InlineData("http://169.254.169.254/")]
    [InlineData("http://100.64.0.1/")]
    [InlineData("http://224.0.0.1/")]
    [InlineData("http://[fe80::1]/")]
    [InlineData("http://[fd00::1]/")]
    [InlineData("http://router/")]
    [InlineData("http://printer.local/")]
    public void ThisPcAndTheLocalNetworkAreNeverHandedToTheBrowser(string address) =>
        Assert.Equal(SiteNavigation.Block, BossTimersSite.Decide(address, playerAsked: true));

    [Fact]
    public void ARedirectIsNeverThePlayersClick()
    {
        Assert.Equal(SiteNavigation.OpenInBrowser, BossTimersSite.DecideNavigation("https://example.org/", userInitiated: true, redirected: false));
        Assert.Equal(SiteNavigation.Block, BossTimersSite.DecideNavigation("https://example.org/", userInitiated: true, redirected: true));
        Assert.Equal(SiteNavigation.Block, BossTimersSite.DecideNavigation("https://example.org/", userInitiated: false, redirected: false));
        // The site's own pages still show after a redirect (www to the bare host, say).
        Assert.Equal(SiteNavigation.Allow, BossTimersSite.DecideNavigation("https://aion2timers.com/world-boss-timer/", userInitiated: false, redirected: true));
    }

    [Fact]
    public void OnlyTheSitesOwnPageCanSendAPopupToTheBrowser()
    {
        const string site = "https://www.aion2timers.com/world-boss-timer/";
        Assert.Equal(SiteNavigation.OpenInBrowser, BossTimersSite.DecideNewWindow("https://example.org/", userInitiated: true, openerAddress: site));
        // An advert's frame, or an unknown opener, sends nothing to the browser, even during a click.
        Assert.Equal(SiteNavigation.Block, BossTimersSite.DecideNewWindow("https://example.org/", userInitiated: true, openerAddress: "https://ads.example.net/frame"));
        Assert.Equal(SiteNavigation.Block, BossTimersSite.DecideNewWindow("https://example.org/", userInitiated: true, openerAddress: null));
        Assert.Equal(SiteNavigation.Block, BossTimersSite.DecideNewWindow("https://example.org/", userInitiated: false, openerAddress: site));
        // A site page asked for in a new window opens in the window, whoever asked.
        Assert.Equal(SiteNavigation.Allow, BossTimersSite.DecideNewWindow(site, userInitiated: false, openerAddress: "https://ads.example.net/frame"));
        Assert.Equal(SiteNavigation.Block, BossTimersSite.DecideNewWindow("javascript:alert(1)", userInitiated: true, openerAddress: site));
    }
}
