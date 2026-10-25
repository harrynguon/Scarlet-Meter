using System.Windows;

namespace ScarletMeter.App.Windows;

/// <summary>
/// Where the overlay window goes so all of its content shows. Only the player list scrolls, past "Rows before
/// scrolling" (the page reports it at that capped height), so when the page's content (the DPS rows and the
/// world-event notices) is taller than the height the player gave the window, the window grows to fit, and it goes back to the player's own placement once the content fits again. It grows downwards; near
/// the bottom of the screen it moves up just enough to stay inside the monitor's work area, never above the work
/// area's top (or the player's own top, if that is higher), and it is never made taller than the work area unless the
/// player's own height already is. Pure, so the rules are tested without a window.
/// </summary>
public static class OverlayFit
{
    /// <summary>
    /// The window's top and height for content <paramref name="contentHeight"/> tall, given the player's own top and
    /// height and the work area of the monitor showing the window, all in device-independent pixels.
    /// </summary>
    public static (double Top, double Height) For(double playerTop, double playerHeight, double contentHeight, Rect workArea)
    {
        var needed = Math.Ceiling(contentHeight);
        if (double.IsNaN(needed) || double.IsInfinity(needed) || needed <= playerHeight) return (playerTop, playerHeight);
        var height = Math.Min(needed, Math.Max(playerHeight, workArea.Height));
        // The bottom it may reach: the work area's, or the player's own if the player put the window lower.
        var bottom = Math.Max(workArea.Bottom, playerTop + playerHeight);
        var top = Math.Max(Math.Min(playerTop, bottom - height), Math.Min(playerTop, workArea.Top));
        return (top, height);
    }

    /// <summary>
    /// Whether a measured content height is worth passing to the window: the first one (nothing was reported yet,
    /// <paramref name="reported"/> is NaN), or one that changed by half a pixel or more.
    /// </summary>
    public static bool IsNewHeight(double reported, double height) =>
        double.IsNaN(reported) || Math.Abs(height - reported) >= 0.5;
}
