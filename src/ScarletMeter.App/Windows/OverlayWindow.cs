using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using ScarletMeter.App.Pages;
using ScarletMeter.App.Settings;

namespace ScarletMeter.App.Windows;

/// <summary>
/// The meter overlay: chromeless, transparent and always on top. The page paints its own translucent panel
/// (background opacity is a setting), is dragged by its header (<see cref="BeginDrag"/>) and resized from
/// the window edges through WindowChrome. Nothing in it scrolls: when the page's content is taller than the player's
/// height, the window grows to fit and returns to the player's own placement once it fits again
/// (<see cref="FitToContent"/>, <see cref="OverlayFit"/>); the player's placement, not the grown one, is what is
/// saved. Closing it closes the tool windows first, then the app.
/// </summary>
public sealed class OverlayWindow : ScarletWindow
{
    private readonly Func<ValueTask> closeToolWindows;

    // The player's own top and height (the width is always the player's): what the window returns to and what is
    // saved. NaN until the window is first laid out.
    private double playerTop = double.NaN;
    private double playerHeight = double.NaN;
    // The content height the page last reported (NaN before the first), and the placement OverlayFit last gave the
    // window beyond the player's own (null while the window is at the player's placement).
    private double contentHeight = double.NaN;
    private (double Top, double Height)? fitted;
    // Inside the system's move or resize loop (the player dragging the header or an edge): no fitting meanwhile.
    private bool sizingOrMoving;
    // Set while Refit moves the window, so LocationChanged does not take the move for the player's.
    private bool applying;

    public OverlayWindow(IServiceProvider services, Func<ValueTask> closeToolWindows)
        : base(services, typeof(MeterPage), WindowKeys.Overlay)
    {
        this.closeToolWindows = closeToolWindows;
        Title = "Scarlet Meter";
        Width = 400;
        Height = 260;
        MinWidth = 300;
        MinHeight = 96;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResize;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        // Edge resizing for a chromeless window; no caption area, because the page's header holds buttons.
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });
        // Aero Snap would maximize the overlay when it is dragged to the top edge.
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
            else if (WindowState == WindowState.Normal) Refit();
        };
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(OnMessage);
        Loaded += (_, _) =>
        {
            playerTop = Top;
            playerHeight = ActualHeight;
            Refit();
        };
        LocationChanged += (_, _) => FollowOutsideMove();
        RestorePlacement();
    }

    /// <summary>
    /// The page's content height in device-independent pixels (CSS pixels at the WebView's zoom of 1): the window
    /// grows to show all of it, or returns to the player's own placement when it fits. Call on the UI thread.
    /// </summary>
    public void FitToContent(double height)
    {
        contentHeight = height;
        Refit();
    }

    protected override Rect PlacementBounds =>
        double.IsNaN(playerTop) ? base.PlacementBounds : new Rect(Left, playerTop, ActualWidth, playerHeight);

    // Applies OverlayFit to the player's placement and the last content height, except while the player is dragging
    // or resizing (that ends in WmExitSizeMove, which fits again) or the window is minimized.
    private void Refit()
    {
        if (sizingOrMoving || !IsLoaded || WindowState != WindowState.Normal || double.IsNaN(playerTop) || double.IsNaN(contentHeight)) return;
        var target = OverlayFit.For(playerTop, playerHeight, contentHeight, ScreenPlacement.WorkAreaOf(this));
        fitted = Math.Abs(target.Top - playerTop) < 0.5 && Math.Abs(target.Height - playerHeight) < 0.5 ? null : target;
        applying = true;
        try
        {
            if (Math.Abs(Top - target.Top) >= 0.5) Top = target.Top;
            if (Math.Abs(ActualHeight - target.Height) >= 0.5) Height = target.Height;
        }
        finally
        {
            applying = false;
        }
    }

    private IntPtr OnMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmEnterSizeMove)
        {
            sizingOrMoving = true;
        }
        else if (msg == NativeMethods.WmExitSizeMove)
        {
            sizingOrMoving = false;
            TakePlayersPlacement();
            Refit();
        }
        return IntPtr.Zero;
    }

    // The player finished dragging or resizing. A move (the height as shown before) carries the player's own top by
    // the same distance, so a grown window that was moved up for the screen's bottom keeps its offset; a resize makes
    // what the player now sees their own placement.
    private void TakePlayersPlacement()
    {
        if (double.IsNaN(playerTop)) return;
        var (shownTop, shownHeight) = fitted ?? (playerTop, playerHeight);
        if (Math.Abs(ActualHeight - shownHeight) < 0.5)
        {
            playerTop += Top - shownTop;
        }
        else
        {
            playerTop = Top;
            playerHeight = ActualHeight;
        }
        fitted = null;
    }

    // A move outside the player's drag and outside Refit (a display change, Windows moving the window): the player's
    // own top follows it.
    private void FollowOutsideMove()
    {
        if (applying || sizingOrMoving || WindowState != WindowState.Normal || double.IsNaN(playerTop)) return;
        var shownTop = fitted?.Top ?? playerTop;
        playerTop += Top - shownTop;
        if (fitted is { } f) fitted = (Top, f.Height);
    }

    protected override System.Drawing.Color WebViewBackground => System.Drawing.Color.Transparent;

    /// <summary>
    /// Starts a native window move while the left button is down on the page's header. The move message is
    /// posted rather than sent, so the modal move loop runs outside the Blazor event handler that asked
    /// for it; a quick click simply ends the loop at once.
    /// </summary>
    public void BeginDrag()
    {
        if (WindowState != WindowState.Normal) return;
        var handle = new WindowInteropHelper(this).Handle;
        Mouse.Capture(null);
        NativeMethods.ReleaseCapture();
        NativeMethods.PostMessage(handle, NativeMethods.WmNclButtonDown, NativeMethods.HtCaption, IntPtr.Zero);
    }

    protected override ValueTask BeforeReleaseAsync() => closeToolWindows();
}
