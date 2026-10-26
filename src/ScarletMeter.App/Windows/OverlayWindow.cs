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
/// (background opacity is a setting), is dragged by its header (<see cref="BeginDrag"/>) and made wider or narrower
/// from its side edges through WindowChrome. Its height is never the player's: it is as tall as the page's content
/// (<see cref="FitToContent"/>, <see cref="OverlayPlacement"/>, <see cref="OverlayFit"/>), whose bar area has a row
/// for each player shown, up to five (Pages/BarArea), growing at once when the content grows and shrinking a moment
/// after it shrinks, its top the player's own. The player's top is what is saved. Its WebView does not zoom, so the
/// page's CSS pixels are the window's. Closing it closes the tool windows first, then the app.
/// </summary>
public sealed class OverlayWindow : ScarletWindow
{
    // How long the window waits before shrinking, so content that comes and goes does not resize it on every render.
    private static readonly TimeSpan ShrinkDelay = TimeSpan.FromSeconds(2);

    private readonly Func<ValueTask> closeToolWindows;

    // The player's own placement and the one shown; the width is always the player's.
    private readonly OverlayPlacement overlayPlacement = new();
    private readonly DispatcherTimer shrinkLater;
    // The content height the page last reported (FitToContent); NaN before the first.
    private double contentHeight = double.NaN;
    // Inside the system's move or resize loop (the player dragging the header or a side): no fitting meanwhile.
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
        MinHeight = WindowPlacement.MinSize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResize;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        // Edge resizing for a chromeless window, the sides only: the height follows the content (Harry: the player
        // should not "fiddle with resizing", 2026-10-26), so the top and bottom edges are no resize handles. No caption
        // area, because the page's header holds buttons.
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(6, 0, 6, 0),
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
            else if (WindowState == WindowState.Normal) RefitSoon();
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
    /// The page's content height in device-independent pixels (CSS pixels at the WebView's zoom of 1), which the window
    /// is as tall as (<see cref="IMeterWindows.FitOverlayToContent"/>). Call on the UI thread.
    /// </summary>
    public void FitToContent(double content)
    {
        contentHeight = content;
        Refit();
    }

    /// <summary>
    /// Whether the window, as laid out now, is shorter than content <paramref name="height"/> tall: true once it is as
    /// tall as the screen allows, never for the moment before it grows (the fit grows it at once). Call on the UI
    /// thread.
    /// </summary>
    public bool CutsContent(double height) => IsLoaded && ActualHeight < Math.Ceiling(height) - 0.5;

    // The player's own placement, once known; nothing while they are dragging or resizing, whose end saves it. Its
    // height is the window's as shown then (the content's, once the page has reported), and never below the floor the
    // settings keep a saved size to, which would drop the whole placement (audit on PR #37).
    protected override Rect? PlacementBounds =>
        sizingOrMoving ? null
        : overlayPlacement.Started ? new Rect(Left, overlayPlacement.PlayerTop, ActualWidth, Math.Max(overlayPlacement.Shown.Height, WindowPlacement.MinSize))
        : base.PlacementBounds;

    // Takes the window to OverlayFit's placement for the content's height (as both the least and the most, so the
    // window is exactly as tall): at once when that grows it (so nothing is cut), after ShrinkDelay when it shrinks it,
    // unless the content grows back meanwhile. Nothing while the player is dragging or resizing (that ends in
    // WmExitSizeMove, which fits again) or the window is minimized.
    private void Refit(bool shrinkNow = false)
    {
        shrinkLater.Stop();
        if (sizingOrMoving || !IsLoaded || WindowState != WindowState.Normal || !overlayPlacement.Started || double.IsNaN(contentHeight)) return;
        var workArea = ScreenPlacement.WorkAreaOf(this);
        var target = overlayPlacement.Target(contentHeight, workArea, contentHeight);
        if (!shrinkNow && overlayPlacement.WaitsToShrink(target, workArea))
        {
            shrinkLater.Start();
            return;
        }
        var changed = false;
        applying = true;
        try
        {
            if (Math.Abs(Top - target.Top) >= 0.5) { Top = target.Top; changed = true; }
            if (Math.Abs(ActualHeight - target.Height) >= 0.5) { Height = target.Height; changed = true; }
        }
        finally
        {
            applying = false;
        }
        overlayPlacement.Showing(target);
        if (changed) Fitted?.Invoke(this, EventArgs.Empty);
    }

    // A fit after the current message is handled and WPF has laid the window out.
    private void RefitSoon() => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => Refit()));

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
            // The height is the content's whatever the loop did (a sizing from the keyboard can still change it), so the
            // window takes it back at once.
            Refit(shrinkNow: true);
            // Saves were skipped while the drag lasted (PlacementBounds), and the fit may not move the window.
            SavePlacementSoon();
        }
        else if (msg is NativeMethods.WmDpiChanged or NativeMethods.WmDisplayChange
                 || (msg == NativeMethods.WmSettingChange && wParam == NativeMethods.SpiSetWorkArea))
        {
            // The work area or the display's scale changed: the cap and the room at the bottom may have too.
            RefitSoon();
        }
        return IntPtr.Zero;
    }

    // A move outside the player's drag and outside Refit (a display change, Windows moving the window): the player's
    // own top follows it, and the window fits again where it now is. A minimized window that Windows parked off screen
    // is minimized by the time that fit runs, so it does nothing.
    private void FollowOutsideMove()
    {
        if (applying || sizingOrMoving || WindowState != WindowState.Normal) return;
        overlayPlacement.MovedTo(Top);
        RefitSoon();
    }

    /// <summary>Raised after a fit moved or resized the window, so the page can ask again whether it cuts the content.</summary>
    public event EventHandler? Fitted;

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
