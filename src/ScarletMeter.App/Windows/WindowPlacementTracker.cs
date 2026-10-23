using System.Windows;
using System.Windows.Threading;
using ScarletMeter.App.Settings;

namespace ScarletMeter.App.Windows;

/// <summary>
/// Remembers a window's position and size in settings. <see cref="Restore"/> runs before the window is
/// shown and ignores a saved placement that would land off every screen (a monitor was unplugged); moves
/// and resizes are saved shortly after they stop, and once more when the window closes. Only the normal
/// (not minimized or maximized) state is saved.
/// </summary>
internal sealed class WindowPlacementTracker
{
    // How much of a restored window must overlap the desktop for the saved placement to be used.
    private const double MinVisibleWidth = 80;
    private const double MinVisibleHeight = 40;

    private readonly Window window;
    private readonly string key;
    private readonly SettingsService settings;
    private readonly DispatcherTimer saveTimer;

    public WindowPlacementTracker(Window window, string key, SettingsService settings)
    {
        this.window = window;
        this.key = key;
        this.settings = settings;
        saveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) => Save(), window.Dispatcher);
        saveTimer.Stop();
        window.LocationChanged += (_, _) => Schedule();
        window.SizeChanged += (_, _) => Schedule();
        window.Closing += (_, _) => Save();
    }

    /// <summary>Applies the saved placement, if usable. Returns false when the window keeps its own default.</summary>
    public bool Restore()
    {
        if (!settings.Current.Windows.TryGetValue(key, out var saved) || !IsOnDesktop(saved)) return false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = saved.Left;
        window.Top = saved.Top;
        window.Width = Math.Max(saved.Width, window.MinWidth);
        window.Height = Math.Max(saved.Height, window.MinHeight);
        return true;
    }

    private void Schedule()
    {
        if (!window.IsLoaded) return;
        saveTimer.Stop();
        saveTimer.Start();
    }

    private void Save()
    {
        saveTimer.Stop();
        if (!window.IsLoaded || window.WindowState != WindowState.Normal) return;
        var placement = new WindowPlacement(window.Left, window.Top, window.ActualWidth, window.ActualHeight);
        settings.Update(s => s.Windows.TryGetValue(key, out var old) && old == placement
            ? s
            : s with { Windows = new Dictionary<string, WindowPlacement>(s.Windows) { [key] = placement } });
    }

    // The virtual screen spans every monitor; a placement that barely touches it is treated as lost.
    private static bool IsOnDesktop(WindowPlacement p)
    {
        var desktop = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var overlap = Rect.Intersect(desktop, new Rect(p.Left, p.Top, p.Width, p.Height));
        return !overlap.IsEmpty && overlap.Width >= MinVisibleWidth && overlap.Height >= MinVisibleHeight;
    }
}
