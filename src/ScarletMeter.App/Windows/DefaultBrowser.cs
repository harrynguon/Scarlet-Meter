using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ScarletMeter.App.Windows;

/// <summary>
/// Hands a page to the player's default browser through the shell. Callers check the address first (an https
/// release page, a public web page): only an absolute http or https address is ever handed over, and it came from
/// the network or a page, so it is never logged.
/// </summary>
internal static class DefaultBrowser
{
    public static void Open(Uri page, ILogger logger)
    {
        if (!page.IsAbsoluteUri || (page.Scheme != Uri.UriSchemeHttps && page.Scheme != Uri.UriSchemeHttp)) return;
        try
        {
            using var _ = Process.Start(new ProcessStartInfo { FileName = page.AbsoluteUri, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(ex, "Opening a page in the default browser failed.");
        }
    }
}
