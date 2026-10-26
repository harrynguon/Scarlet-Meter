using System.Windows;

namespace ScarletMeter.App.Windows;

/// <summary>
/// The overlay window's height rules. The window is as tall as the page's content: the header, the target card in a
/// fight, the totals, the bar area and the world-event notices. Nothing in it is sized by hand: the bar area has a row
/// for each player shown, up to five, and scrolls past that (Pages/BarArea; Harry, 2026-10-26: "the player won't need
/// to fiddle with resizing"), so the window grows when a fight's players come, an event's line opens or the target card
/// appears, and shrinks a moment after they go. It grows downwards; near the bottom of the screen it moves up just
/// enough to stay inside the monitor's work area, never above the work area's top (or the player's own top, if that is
/// higher), and it is never made taller than the work area unless the player's own height already is. The bar area the
/// player sized by dragging the meter's edge (#37, commit 3500895) is in the history for reference. Pure, so the rules
/// are tested without a window; all values are device-independent pixels.
/// </summary>
public static class OverlayFit
{
    /// <summary>
    /// The window's top and height when its content needs at least <paramref name="leastHeight"/> and may be at most
    /// <paramref name="mostHeight"/> tall, given the player's own top and height and the work area of the monitor
    /// showing the window. The overlay passes its content's height as both, so the window is as tall as its content.
    /// </summary>
    public static (double Top, double Height) For(
        double playerTop, double playerHeight, double leastHeight, Rect workArea, double mostHeight = double.PositiveInfinity)
    {
        var needed = Math.Ceiling(leastHeight);
        if (double.IsNaN(needed) || double.IsInfinity(needed)) return (playerTop, playerHeight);
        var most = Math.Ceiling(mostHeight);
        if (most >= needed && playerHeight > most) return (playerTop, most);
        if (needed <= playerHeight) return (playerTop, playerHeight);
        var height = Math.Min(needed, Math.Max(playerHeight, workArea.Height));
        // The bottom it may reach: the work area's, or the player's own if the player put the window lower.
        var bottom = Math.Max(workArea.Bottom, playerTop + playerHeight);
        var top = Math.Max(Math.Min(playerTop, bottom - height), Math.Min(playerTop, workArea.Top));
        return (top, height);
    }

    /// <summary>
    /// Whether a measured height is worth passing to the window: the first one (nothing was reported yet,
    /// <paramref name="reported"/> is NaN), or one that changed by half a pixel or more.
    /// </summary>
    public static bool IsNewHeight(double reported, double height) =>
        double.IsNaN(reported) || Math.Abs(height - reported) >= 0.5;
}
