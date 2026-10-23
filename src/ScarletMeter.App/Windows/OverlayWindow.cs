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
/// the window edges through WindowChrome. Closing it closes the tool windows first, then the app.
/// </summary>
public sealed class OverlayWindow : ScarletWindow
{
    private readonly Func<ValueTask> closeToolWindows;

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
        };
        RestorePlacement();
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
