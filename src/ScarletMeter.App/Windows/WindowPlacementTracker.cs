using System.Windows;
using System.Windows.Threading;
using ScarletMeter.App.Settings;

namespace ScarletMeter.App.Windows;

/// <summary>
/// Remembers a window's position and size in settings. <see cref="Restore"/> runs before the window is
/// shown and ignores a saved placement that would land off every monitor (one was unplugged or the layout
/// changed; see <see cref="ScreenPlacement"/>); moves and resizes are saved shortly after they stop, and once
/// more when the window closes. Only the normal (not minimized or maximized) state is saved.
/// </summary>
internal sealed class WindowPlacementTracker
{
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

    // A placement that barely touches any single monitor is treated as lost (Codex review on PR #2: the
    // virtual screen's bounding box also covers the gaps between monitors).
    private static bool IsOnDesktop(WindowPlacement p) =>
        ScreenPlacement.IsVisible(new Rect(p.Left, p.Top, p.Width, p.Height), ScreenPlacement.Monitors());
}
