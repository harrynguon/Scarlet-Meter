using System.Runtime.InteropServices;
using System.Windows;

namespace ScarletMeter.App.Windows;

/// <summary>
/// Whether a saved window placement can still be reached. Each monitor is checked on its own: the virtual
/// screen's bounding box also covers the gaps of an L-shaped or offset layout, where a restored window would sit
/// off every monitor.
/// </summary>
public static class ScreenPlacement
{
    /// <summary>How much of a window must lie on one monitor for its saved placement to be used.</summary>
    public const double MinVisibleWidth = 80;

    /// <inheritdoc cref="MinVisibleWidth"/>
    public const double MinVisibleHeight = 40;

    /// <summary>
    /// True when at least <see cref="MinVisibleWidth"/> by <see cref="MinVisibleHeight"/> of the placement lies on
    /// a single monitor. Two small pieces on two monitors do not add up.
    /// </summary>
    public static bool IsVisible(Rect placement, IEnumerable<Rect> monitors) =>
        monitors.Any(monitor =>
        {
            var overlap = Rect.Intersect(monitor, placement);
            return !overlap.IsEmpty && overlap.Width >= MinVisibleWidth && overlap.Height >= MinVisibleHeight;
        });

    /// <summary>
    /// The monitors' rectangles in device-independent pixels at the system DPI, the units of
    /// <see cref="SystemParameters.VirtualScreenLeft"/> and of a window's saved position. Falls back to the
    /// virtual screen when the monitors cannot be listed, so a placement is never refused for that alone.
    /// </summary>
    public static IReadOnlyList<Rect> Monitors()
    {
        var dpi = NativeMethods.GetDpiForSystem();
        var scale = dpi > 0 ? dpi / 96.0 : 1;
        var found = new List<Rect>();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
            if (NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                var r = info.Monitor;
                found.Add(new Rect(r.Left / scale, r.Top / scale, (r.Right - r.Left) / scale, (r.Bottom - r.Top) / scale));
            }
            return true;
        }, IntPtr.Zero);
        return found.Count > 0
            ? found
            : [new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight)];
    }
}
