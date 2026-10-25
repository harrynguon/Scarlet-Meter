using System.Windows;
using System.Windows.Interop;

namespace ScarletMeter.App.Windows;

/// <summary>Gives a window with the standard Windows frame a dark title bar, to match the meter's dark pages.</summary>
internal static class DarkTitleBar
{
    /// <summary>Call once the window has a handle (SourceInitialized).</summary>
    public static void Apply(Window window)
    {
        var enabled = 1;
        _ = NativeMethods.DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, NativeMethods.DwmUseImmersiveDarkMode, ref enabled, sizeof(int));
    }
}
