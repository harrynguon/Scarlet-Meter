using System.Windows;
using ScarletMeter.App.Settings;

namespace ScarletMeter.App.Windows;

/// <summary>
/// The overlay window's height rules. The window is as tall as the page's content: the header, the target card in a
/// fight, the totals, the bar area and the world-event notices. The bar area is the one part the player sizes, in
/// rows (<see cref="AppearanceSettings.BarRows"/>, 0 to <see cref="MostRows"/>; Harry: "make sure the resizing only
/// changes the size of the dps bar area"), by dragging the meter's bottom edge: <see cref="RowsFor"/> turns the height
/// a resize ends at into rows. Everything else keeps its own height, so the window grows when the notices open or the
/// target card comes, and shrinks a moment after they go. It grows downwards; near the bottom of the screen it moves
/// up just enough to stay inside the monitor's work area, never above the work area's top (or the player's own top,
/// if that is higher), and it is never made taller than the work area. Pure, so the rules are tested without a
/// window; all values are device-independent pixels.
/// </summary>
public static class OverlayFit
{
    /// <summary>The most rows the bar area may be (Harry: "max height of 10 players worth").</summary>
    public const int MostRows = AppearanceSettings.MaxBarRows;

    /// <summary>A row's height with its gap at row size 1 (app.css, .row: 26 px and a 3 px margin).</summary>
    public const double RowPixels = 29;

    /// <summary>A row's height with its gap at a row size (Settings, Appearance; the rows' zoom).</summary>
    public static double RowStep(double rowScale) => RowPixels * rowScale;

    /// <summary>The tallest a resize may make the meter: its content with no rows, <paramref name="emptyHeight"/>, and <see cref="MostRows"/> rows.</summary>
    public static double MostHeight(double emptyHeight, double rowScale) => emptyHeight + MostRows * RowStep(rowScale);

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
    /// The bar area's rows for a window <paramref name="height"/> tall whose content with no rows is
    /// <paramref name="emptyHeight"/>: the nearest whole number of rows, 0 to <see cref="MostRows"/>, so no row is cut
    /// in half at the bottom. Null until the page has reported its heights.
    /// </summary>
    public static int? RowsFor(double height, double emptyHeight, double rowStep)
    {
        if (!double.IsFinite(height) || !double.IsFinite(emptyHeight) || !double.IsFinite(rowStep) || rowStep <= 0) return null;
        var rows = Math.Round((height - Math.Ceiling(emptyHeight)) / rowStep, MidpointRounding.AwayFromZero);
        return (int)Math.Clamp(rows, 0, MostRows);
    }

    /// <summary>
    /// A window rectangle being resized by its <paramref name="edge"/> (WM_SIZING's WMSZ_ value; device pixels), held
    /// between <paramref name="least"/> and <paramref name="most"/> pixels tall by stopping the edge being dragged: the
    /// top (or a top corner) stops and the bottom stays, the bottom (or a bottom corner) stops and the top stays; the
    /// left and right edges leave the height alone. WPF's own MinHeight and MaxHeight keep the top and move the bottom,
    /// so a drag of the top edge past the most carried the whole window up with the cursor (Harry).
    /// </summary>
    public static (int Top, int Bottom) HoldDraggedEdge(int top, int bottom, int edge, double least, double most)
    {
        const int TopEdge = 3, TopLeft = 4, TopRight = 5, BottomRight = 8;
        if (edge is < TopEdge or > BottomRight || double.IsNaN(least) || double.IsNaN(most)) return (top, bottom);
        var fewest = (int)Math.Ceiling(Math.Clamp(least, 0, int.MaxValue));
        var greatest = Math.Max(fewest, (int)Math.Floor(Math.Clamp(most, 0, int.MaxValue)));
        var height = Math.Clamp(bottom - top, fewest, greatest);
        return edge is TopEdge or TopLeft or TopRight ? (bottom - height, bottom) : (top, top + height);
    }

    /// <summary>
    /// Whether a measured height is worth passing to the window: the first one (nothing was reported yet,
    /// <paramref name="reported"/> is NaN), or one that changed by half a pixel or more.
    /// </summary>
    public static bool IsNewHeight(double reported, double height) =>
        double.IsNaN(reported) || Math.Abs(height - reported) >= 0.5;
}
