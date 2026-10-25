using System.Windows;

namespace ScarletMeter.App.Windows;

/// <summary>
/// The overlay's two placements: the player's own top and height (the top is what is saved and what the window returns
/// to; the overlay passes its content's height as the height to show, OverlayWindow) and the one the window shows now,
/// with how the player's drags and resizes and the system's moves change them. Pure, so the rules are tested without a window;
/// <see cref="OverlayWindow"/> applies them. All values are device-independent pixels.
/// </summary>
public sealed class OverlayPlacement
{
    /// <summary>The player's own top; NaN until <see cref="Start"/>.</summary>
    public double PlayerTop { get; private set; } = double.NaN;

    /// <summary>The player's own height; NaN until <see cref="Start"/>.</summary>
    public double PlayerHeight { get; private set; } = double.NaN;

    /// <summary>The placement the window shows now.</summary>
    public (double Top, double Height) Shown { get; private set; } = (double.NaN, double.NaN);

    /// <summary>Whether the window has been laid out, so the player's placement is known.</summary>
    public bool Started => !double.IsNaN(PlayerTop);

    /// <summary>The window's first laid-out placement, which is the player's own (saved, or the default).</summary>
    public void Start(double top, double height)
    {
        PlayerTop = top;
        PlayerHeight = height;
        Shown = (top, height);
    }

    /// <summary>
    /// Where the window goes when its content needs at least <paramref name="leastHeight"/> and the meter may be at
    /// most <paramref name="mostHeight"/> tall, on this work area (<see cref="OverlayFit.For"/>).
    /// </summary>
    public (double Top, double Height) Target(double leastHeight, Rect workArea, double mostHeight = double.PositiveInfinity) =>
        OverlayFit.For(PlayerTop, PlayerHeight, leastHeight, workArea, mostHeight);

    /// <summary>
    /// Whether the window waits a moment before going to <paramref name="target"/>: it is shorter than what the window
    /// shows, so content that comes and goes (a share near the threshold, the target card between pulls) does not
    /// resize it on every render. Growing never waits, so nothing is cut; nor does a window that no longer fits the
    /// work area (moved to a shorter monitor, a larger display scale, a taller taskbar), whose bottom is off screen.
    /// </summary>
    public bool WaitsToShrink((double Top, double Height) target, Rect workArea) =>
        target.Height < Shown.Height - 0.5
        && Shown.Top >= workArea.Top - 0.5 && Shown.Top + Shown.Height <= workArea.Bottom + 0.5;

    /// <summary>The window now shows <paramref name="placement"/>.</summary>
    public void Showing((double Top, double Height) placement) => Shown = placement;

    /// <summary>
    /// The player finished dragging or resizing the window, which now shows <paramref name="top"/> and
    /// <paramref name="height"/>. A resize makes what the player sees their own placement. A move makes where they
    /// dropped the window their own top and keeps their own height under a grown one, so a grown window that was moved
    /// up for the screen's bottom stays where it was dropped. A click on the header that moved nothing changes nothing.
    /// </summary>
    public void PlayerPlaced(double top, double height)
    {
        if (!Started) return;
        if (Math.Abs(height - Shown.Height) >= 0.5)
        {
            PlayerTop = top;
            PlayerHeight = height;
        }
        else if (Math.Abs(top - Shown.Top) >= 0.5)
        {
            PlayerTop = top;
        }
        Shown = (top, height);
    }

    /// <summary>
    /// Something other than the player and the fit moved the window to <paramref name="top"/> (a display change,
    /// Windows parking a minimized window off screen): the player's own top moves by the same distance, so it comes
    /// back exactly when the window does.
    /// </summary>
    public void MovedTo(double top)
    {
        if (!Started) return;
        PlayerTop += top - Shown.Top;
        Shown = (top, Shown.Height);
    }
}
