namespace ScarletMeter.App.BossTimers;

/// <summary>
/// At most one page handed to the default browser per <see cref="Interval"/>. A page can make several navigations
/// during one click of the player's (WebView2 counts each as user-initiated), and each would open a browser tab and
/// take focus from the game; later ones within the interval are dropped. A clock that goes back lets the next one
/// through.
/// </summary>
public sealed class BrowserLaunchGate(TimeProvider time)
{
    /// <summary>The shortest time between two pages handed to the browser.</summary>
    public static TimeSpan Interval { get; } = TimeSpan.FromSeconds(3);

    private DateTimeOffset? last;

    /// <summary>True, and counted, when a page may be handed to the browser now.</summary>
    public bool TryPass()
    {
        var now = time.GetUtcNow();
        if (last is { } previous && now >= previous && now - previous < Interval) return false;
        last = now;
        return true;
    }
}
