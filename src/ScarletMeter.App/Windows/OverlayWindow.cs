using Microsoft.Extensions.DependencyInjection;
using System.Runtime.InteropServices;
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
/// the window edges through WindowChrome. It is as tall as the page's content (<see cref="FitToContent"/>,
/// <see cref="OverlayPlacement"/>, <see cref="OverlayFit"/>): growing at once when the content grows, shrinking a
/// moment after it shrinks, its top the player's own. Dragging its bottom edge sizes only the bar area: while the
/// resize lasts the page lets the bars fill the window (<see cref="Resizing"/>) and the window is held between no
/// rows and <see cref="OverlayFit.MostRows"/>, and where it ends becomes the bar area's whole rows
/// (<see cref="AppearanceSettings.BarRows"/>), saved; the notices and the rest keep their own heights. The player's
/// top is what is saved. Its WebView does not zoom, so the page's CSS pixels are the window's. Closing it closes the
/// tool windows first, then the app.
/// </summary>
public sealed class OverlayWindow : ScarletWindow
{
    // How long the window waits before shrinking, so content that comes and goes does not resize it on every render.
    private static readonly TimeSpan ShrinkDelay = TimeSpan.FromSeconds(2);

    private readonly Func<ValueTask> closeToolWindows;
    // Where the bar area's rows are kept (AppearanceSettings.BarRows).
    private readonly SettingsService settings;

    // The player's own placement and the one shown; the width is always the player's.
    private readonly OverlayPlacement overlayPlacement = new();
    private readonly DispatcherTimer shrinkLater;
    // The heights the page last reported (FitToContent): its content, its content with no rows, and a row with its
    // gap; NaN before the first.
    private double contentHeight = double.NaN;
    private double emptyHeight = double.NaN;
    private double rowStep = double.NaN;
    // Inside the system's move or resize loop (the player dragging the header or an edge): no fitting meanwhile.
    private bool sizingOrMoving;
    // Within that loop, the top or bottom edge (or a corner) is being dragged, not the header or a side; the edge
    // (WM_SIZING's WMSZ_ value, 0 before one) and the window's height when the loop began.
    private bool resizing;
    private int dragEdge;
    private double heightAtStart;
    // Whether a saved placement was restored, so an earlier build's height can be carried over as rows.
    private readonly bool placementRestored;
    // Set while Refit moves the window, so LocationChanged does not take the move for someone else's.
    private bool applying;

    public OverlayWindow(IServiceProvider services, Func<ValueTask> closeToolWindows)
        : base(services, typeof(MeterPage), WindowKeys.Overlay)
    {
        this.closeToolWindows = closeToolWindows;
        settings = services.GetRequiredService<SettingsService>();
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
        placementRestored = RestorePlacement();
    }

    /// <summary>
    /// The page's heights in device-independent pixels (CSS pixels at the WebView's zoom of 1): its content, which the
    /// window is as tall as, its content with no rows, and a row with its gap
    /// (<see cref="IMeterWindows.FitOverlayToContent"/>). With no rows saved, the first report starts them
    /// (<see cref="OverlayFit.CarriedRows"/>: an earlier build's restored height turned into rows, so the player's size
    /// stays, or the default) and the window takes that height at once, before the page reports it again. Call on the
    /// UI thread.
    /// </summary>
    public void FitToContent(double content, double empty, double step)
    {
        contentHeight = content;
        emptyHeight = empty;
        rowStep = step;
        if (IsLoaded && OverlayFit.CarriedRows(settings.Current.Appearance.BarRows, placementRestored, ActualHeight, empty, step) is { } rows)
        {
            contentHeight = empty + rows * step;
            SetBarRows(rows);
            Refit(shrinkNow: true);
            return;
        }
        Refit();
    }

    /// <summary>Whether the top or bottom edge is being dragged: the page then lets the bars fill the window. Call on the UI thread.</summary>
    public bool Resizing => resizing;

    /// <summary>
    /// Whether the window, as laid out now, is shorter than content <paramref name="height"/> tall: true once it is as
    /// tall as the screen allows or while the player resizes it smaller, never for the moment before it grows (the fit
    /// grows it at once). Call on the UI thread.
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

    // Holds the rectangle being dragged (WM_SIZING's, device pixels) between the content with no rows and with
    // OverlayFit.MostRows rows (OverlayFit.DragLimits, from the page's last heights) by stopping the edge being dragged
    // (OverlayFit.HoldDraggedEdge), before Windows moves the window, so a resize stops there rather than springing back
    // when it ends. Not with WPF's MinHeight and MaxHeight: WPF holds those by keeping the top and moving the bottom,
    // so the top edge dragged past the most carried the whole window up with the cursor (Harry), and set for the whole
    // loop they also shrank a window waiting to shrink when a move of the header began (audit on PR #37). Nothing
    // until the page has reported.
    private void HoldDraggedEdge(int edge, IntPtr rectPointer)
    {
        if (rectPointer == IntPtr.Zero || !double.IsFinite(emptyHeight) || !double.IsFinite(rowStep)) return;
        var rect = Marshal.PtrToStructure<NativeMethods.RectInt>(rectPointer);
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        var (least, most) = OverlayFit.DragLimits(emptyHeight, rowStep, ScreenPlacement.WorkAreaOf(this).Height);
        var (top, bottom) = OverlayFit.HoldDraggedEdge(rect.Top, rect.Bottom, edge, least * scale, most * scale);
        if (top == rect.Top && bottom == rect.Bottom) return;
        rect.Top = top;
        rect.Bottom = bottom;
        Marshal.StructureToPtr(rect, rectPointer, fDeleteOld: false);
    }

    // Keeps the bar area's rows, which the page shows once Settings.Changed reaches it.
    private void SetBarRows(int rows)
    {
        if (settings.Current.Appearance.BarRows != rows)
            settings.Update(s => s with { Appearance = s.Appearance with { BarRows = rows } });
    }

    private IntPtr OnMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmEnterSizeMove)
        {
            sizingOrMoving = true;
            dragEdge = 0;
            heightAtStart = ActualHeight;
        }
        else if (msg == NativeMethods.WmSizing && sizingOrMoving)
        {
            dragEdge = wParam.ToInt32();
            if (!resizing && OverlayFit.IsHeightEdge(dragEdge))
            {
                // The top or bottom edge is being dragged: the page lets the bars follow it.
                resizing = true;
                Fitted?.Invoke(this, EventArgs.Empty);
            }
            HoldDraggedEdge(dragEdge, lParam);
            handled = true;
            return 1;
        }
        else if (msg == NativeMethods.WmExitSizeMove)
        {
            sizingOrMoving = false;
            var wasResizing = resizing;
            resizing = false;
            if (wasResizing && OverlayFit.ResizedRows(dragEdge, heightAtStart, ActualHeight, emptyHeight, rowStep) is { } rows)
            {
                // Where the edge was let go becomes the bar area's whole rows; the window takes that height at once,
                // before the page reports it, so it settles there rather than waiting to shrink, and dragged by the top
                // it keeps its bottom where it was.
                contentHeight = emptyHeight + rows * rowStep;
                var settled = Math.Ceiling(contentHeight);
                overlayPlacement.PlayerPlaced(OverlayFit.TopAfterResize(dragEdge, Top, ActualHeight, settled), settled);
                SetBarRows(rows);
            }
            else
            {
                // A move, a drag of a side only, or a resize cancelled where it began: the rows stay.
                overlayPlacement.PlayerPlaced(Top, ActualHeight);
            }
            Refit(shrinkNow: wasResizing);
            if (wasResizing) Fitted?.Invoke(this, EventArgs.Empty);
            dragEdge = 0;
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

    /// <summary>
    /// Raised after a fit moved or resized the window, or a resize of an edge started or ended, so the page can ask
    /// again whether it cuts the content and whether it is being resized.
    /// </summary>
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
