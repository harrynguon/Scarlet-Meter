namespace ScarletMeter.App.BossTimers;

/// <summary>What the boss-timers window does with a navigation (<see cref="BossTimersSite.Decide"/>).</summary>
public enum SiteNavigation
{
    /// <summary>Show it in the window: a page of the site.</summary>
    Allow,

    /// <summary>Hand it to the default browser: the player clicked a link to another web page.</summary>
    OpenInBrowser,

    /// <summary>Refuse it: another scheme, a page the player did not ask for, credentials or a malformed address.</summary>
    Block,
}

/// <summary>
/// The one site the boss-timers window shows, aion2timers.com, and the rule for every navigation in it. The meter
/// never reads the site: the player reads it in the window (the site's robots.txt bars crawlers from its API and
/// AI agents altogether, and a person viewing a page is neither). Pure, so the lock is tested without a window.
/// </summary>
public static class BossTimersSite
{
    /// <summary>Where the window opens.</summary>
    public static Uri Start { get; } = new("https://www.aion2timers.com/world-boss-timer/");

    /// <summary>The site's own hosts, as ASCII (punycode) host names.</summary>
    private static readonly string[] Hosts = ["www.aion2timers.com", "aion2timers.com"];

    /// <summary>
    /// The window's answer to a navigation to <paramref name="address"/>: a page of the site shows; a web page
    /// elsewhere opens in the default browser only when the player asked for it (<paramref name="playerAsked"/>: a
    /// click, not a script or a redirect); anything else is refused.
    /// </summary>
    public static SiteNavigation Decide(string? address, bool playerAsked)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) return SiteNavigation.Block;
        if (IsSitePage(uri)) return SiteNavigation.Allow;
        return playerAsked && IsWebPage(uri) ? SiteNavigation.OpenInBrowser : SiteNavigation.Block;
    }

    /// <summary>
    /// An https page of the site on the default port with no credentials in the address (not even an empty "@"). The
    /// host is compared in its ASCII form, so a lookalike written with other characters is not the site.
    /// </summary>
    public static bool IsSitePage(Uri uri) =>
        uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && !HasCredentials(uri)
        && Hosts.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase);

    /// <summary>An http or https address with a host and no credentials: the only kind the default browser is handed.</summary>
    public static bool IsWebPage(Uri uri) =>
        uri.IsAbsoluteUri && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && uri.Host.Length > 0 && !HasCredentials(uri);

    // A user name or password before the host, read from the address as written: Uri drops an empty one ("https://@host").
    private static bool HasCredentials(Uri uri)
    {
        if (uri.UserInfo.Length > 0) return true;
        var text = uri.OriginalString;
        var start = text.IndexOf("://", StringComparison.Ordinal);
        if (start < 0) return true;
        start += 3;
        var end = text.IndexOfAny(['/', '\\', '?', '#'], start);
        return (end < 0 ? text[start..] : text[start..end]).Contains('@');
    }
}
