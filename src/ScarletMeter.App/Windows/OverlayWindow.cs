using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using ScarletMeter.App.Pages;
using ScarletMeter.App.Settings;

namespace ScarletMeter.App.Windows;

/// <summary>
/// The meter overlay: chromeless, transparent and always on top. The page paints its own translucent panel
/// (background opacity is a setting), is dragged by its header (<see cref="BeginDrag"/>) and resized from
/// the window edges through WindowChrome. Nothing in it scrolls: when the page's content is taller than the player's
/// height, the window grows to fit at once and returns to the player's own placement a moment after it fits again
/// (<see cref="FitToContent"/>, <see cref="OverlayPlacement"/>, <see cref="OverlayFit"/>); the player's placement,
/// not the grown one, is what is saved. Its WebView does not zoom, so the page's CSS pixels are the window's.
/// Closing it closes the tool windows first, then the app.
/// </summary>
public sealed class OverlayWindow : ScarletWindow
{
    // How long the window waits before shrinking, so content that comes and goes does not resize it on every render.
    private static readonly TimeSpan ShrinkDelay = TimeSpan.FromSeconds(2);

    private readonly Func<ValueTask> closeToolWindows;

    // The player's own placement and the one shown; the width is always the player's.
    private readonly OverlayPlacement overlayPlacement = new();
    private readonly DispatcherTimer shrinkLater;
    // The content height the page last reported; NaN before the first.
    private double contentHeight = double.NaN;
    // Inside the system's move or resize loop (the player dragging the header or an edge): no fitting meanwhile.
    private bool sizingOrMoving;
    // Set while Refit moves the window, so LocationChanged does not take the move for someone else's.
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
        shrinkLater = new DispatcherTimer(ShrinkDelay, DispatcherPriority.Background, (_, _) => Refit(shrinkNow: true), Dispatcher);
        shrinkLater.Stop();
        // Aero Snap would maximize the overlay when it is dragged to the top edge. A restore fits once WPF has laid
        // the window out at its restored size: StateChanged is raised inside WPF's own size handling, and a height set
        // there can be laid out at the old size again and drawn cut.
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
            else if (WindowState == WindowState.Normal) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => Refit()));
        };
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(OnMessage);
        Loaded += (_, _) =>
        {
            overlayPlacement.Start(Top, ActualHeight);
            Refit();
        };
        LocationChanged += (_, _) => FollowOutsideMove();
        Closed += (_, _) => shrinkLater.Stop();
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

    /// <summary>
    /// Whether the window, as laid out now, is shorter than content <paramref name="height"/> tall: true once it is as
    /// tall as the screen allows or while the player resizes it smaller, never for the moment before it grows (the fit
    /// grows it at once). Call on the UI thread.
    /// </summary>
    public bool CutsContent(double height) => IsLoaded && ActualHeight < Math.Ceiling(height) - 0.5;

    // The player's own placement, once known; nothing while they are dragging or resizing, whose end saves it.
    protected override Rect? PlacementBounds =>
        sizingOrMoving ? null
        : overlayPlacement.Started ? new Rect(Left, overlayPlacement.PlayerTop, ActualWidth, overlayPlacement.PlayerHeight)
        : base.PlacementBounds;

    // Takes the window to OverlayFit's placement for the last content height: at once when that grows it (so nothing
    // is cut), after ShrinkDelay when it shrinks it, unless the content grows back meanwhile. Nothing while the player
    // is dragging or resizing (that ends in WmExitSizeMove, which fits again) or the window is minimized.
    private void Refit(bool shrinkNow = false)
    {
        shrinkLater.Stop();
        if (sizingOrMoving || !IsLoaded || WindowState != WindowState.Normal || !overlayPlacement.Started || double.IsNaN(contentHeight)) return;
        var target = overlayPlacement.Target(contentHeight, ScreenPlacement.WorkAreaOf(this));
        if (!shrinkNow && overlayPlacement.Shrinks(target))
        {
            shrinkLater.Start();
            return;
        }
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
        overlayPlacement.Showing(target);
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
            overlayPlacement.PlayerPlaced(Top, ActualHeight);
            Refit();
            // Saves were skipped while the drag lasted (PlacementBounds), and the fit may not move the window.
            SavePlacementSoon();
        }
        return IntPtr.Zero;
    }

    // A move outside the player's drag and outside Refit (a display change, Windows moving the window): the player's
    // own top follows it.
    private void FollowOutsideMove()
    {
        if (applying || sizingOrMoving || WindowState != WindowState.Normal) return;
        overlayPlacement.MovedTo(Top);
    }

    protected override System.Drawing.Color WebViewBackground => System.Drawing.Color.Transparent;

    // Ctrl+wheel or Ctrl+plus would zoom the page, and the window would then grow to the wrong height.
    protected override bool WebViewZoomable => false;

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
