using System.Net;
using System.Net.Sockets;

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
    /// The window's answer to a navigation to <paramref name="address"/>: a page of the site shows; a public web page
    /// elsewhere opens in the default browser only during the player's gesture (<paramref name="playerAsked"/>: a
    /// navigation the page makes while the player clicks, which WebView2 counts as user-initiated); anything else is
    /// refused.
    /// </summary>
    public static SiteNavigation Decide(string? address, bool playerAsked)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) return SiteNavigation.Block;
        if (IsSitePage(uri)) return SiteNavigation.Allow;
        return playerAsked && IsWebPage(uri) ? SiteNavigation.OpenInBrowser : SiteNavigation.Block;
    }

    /// <summary>
    /// A top-level navigation (NavigationStarting). A redirect is never the player's gesture, whatever started the
    /// chain: a site link that redirects elsewhere stops there, rather than leaving through the site.
    /// </summary>
    public static SiteNavigation DecideNavigation(string? address, bool userInitiated, bool redirected) =>
        Decide(address, userInitiated && !redirected);

    /// <summary>
    /// A request for a new window (NewWindowRequested), which never becomes a second browser window: a site page opens
    /// in the window; a clicked page elsewhere goes to the default browser only when the site's own page asked
    /// (<paramref name="openerAddress"/>, the frame that asked), not a frame from elsewhere such as an advert.
    /// </summary>
    public static SiteNavigation DecideNewWindow(string? address, bool userInitiated, string? openerAddress)
    {
        var decision = Decide(address, userInitiated);
        if (decision != SiteNavigation.OpenInBrowser) return decision;
        return Uri.TryCreate(openerAddress, UriKind.Absolute, out var opener) && IsSitePage(opener)
            ? SiteNavigation.OpenInBrowser
            : SiteNavigation.Block;
    }

    /// <summary>
    /// An https page of the site on the default port with no credentials in the address (not even an empty "@"). The
    /// host is compared in its ASCII form, so a lookalike written with other characters is not the site.
    /// </summary>
    public static bool IsSitePage(Uri uri) =>
        uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && !HasCredentials(uri)
        && Hosts.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// An http or https address of a public host with no credentials: the only kind the default browser is handed.
    /// Never this PC or the local network (loopback, private, link-local and the like, single-label names,
    /// <c>localhost</c> and <c>.local</c>), so a page cannot use the player's click to reach a device or a service
    /// there through the browser.
    /// </summary>
    public static bool IsWebPage(Uri uri) =>
        uri.IsAbsoluteUri && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && uri.Host.Length > 0 && !HasCredentials(uri) && IsPublicHost(uri);

    private static bool IsPublicHost(Uri uri)
    {
        switch (uri.HostNameType)
        {
            case UriHostNameType.IPv4:
            case UriHostNameType.IPv6:
                return IPAddress.TryParse(uri.IdnHost.Trim('[', ']'), out var address) && IsPublic(address);
            case UriHostNameType.Dns:
                var host = uri.IdnHost.TrimEnd('.');
                return host.Contains('.')
                    && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                    && !host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
                    && !host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
            default:
                return false;
        }
    }

    // Not this PC, the local network or a reserved range: IPv4 0/8, 10/8, 100.64/10, 127/8, 169.254/16, 172.16/12,
    // 192.168/16 and 224 upwards; IPv6 loopback, unspecified, link-local, site-local, unique-local and multicast.
    private static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return false;
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] is 0 or 10 or 127 || b[0] >= 224
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168));
        }
        return !(address.Equals(IPAddress.IPv6Any) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal
            || address.IsIPv6Multicast || (address.GetAddressBytes()[0] & 0xFE) == 0xFC);
    }

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
