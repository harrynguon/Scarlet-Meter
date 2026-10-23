namespace ScarletMeter.App.Settings;

/// <summary>
/// The one validation boundary for settings, applied to everything loaded from disk and to every update.
/// Missing sections take their defaults, numbers are clamped to their ranges, and values that could point
/// outside the meter's own data (a background image name, an unknown window key) are dropped. Each repair
/// is reported so a load can say what it fixed.
/// </summary>
public static class SettingsNormalizer
{
    private const double MinWindowSize = 120;
    private const double MaxWindowSize = 20_000;
    private const double MaxWindowOffset = 100_000;

    public static (MeterSettings Settings, IReadOnlyList<string> Repairs) Normalize(MeterSettings? input)
    {
        var repairs = new List<string>();
        input ??= new MeterSettings();

        var fight = input.Fight ?? new FightSettings();
        var retention = Math.Clamp(fight.HistoryRetentionDays, FightSettings.MinRetentionDays, FightSettings.MaxRetentionDays);
        if (retention != fight.HistoryRetentionDays) repairs.Add($"History retention set to {retention} days.");

        var appearance = input.Appearance ?? new AppearanceSettings();
        var opacity = ClampFinite(appearance.BackgroundOpacity, AppearanceSettings.MinOpacity, AppearanceSettings.MaxOpacity, 0.9, "Background opacity", repairs);
        var scale = ClampFinite(appearance.RowScale, AppearanceSettings.MinRowScale, AppearanceSettings.MaxRowScale, 1.0, "Row size", repairs);
        var image = appearance.BackgroundImage;
        if (image is not null && !BackgroundImages.IsStoredName(image))
        {
            repairs.Add("The background image setting was not recognised and was cleared.");
            image = null;
        }

        var overlay = input.Overlay ?? new OverlaySettings();
        var hotkey = overlay.ToggleHotkey ?? string.Empty;
        if (hotkey.Length > 0)
        {
            if (HotkeyGesture.TryParse(hotkey, out var gesture, out _)) hotkey = gesture.ToString();
            else
            {
                repairs.Add($"The shortcut \"{Truncate(hotkey)}\" was not valid and was cleared.");
                hotkey = string.Empty;
            }
        }

        var windows = new Dictionary<string, WindowPlacement>(StringComparer.Ordinal);
        foreach (var (key, placement) in input.Windows ?? new Dictionary<string, WindowPlacement>())
        {
            if (key is not null && WindowKeys.All.Contains(key) && IsUsable(placement)) windows[key] = placement;
            else repairs.Add("An unusable saved window position was dropped.");
        }

        var settings = new MeterSettings
        {
            Version = MeterSettings.CurrentVersion,
            Fight = fight with { HistoryRetentionDays = retention },
            Appearance = appearance with { BackgroundOpacity = opacity, RowScale = scale, BackgroundImage = image },
            Overlay = overlay with { ToggleHotkey = hotkey },
            Windows = windows,
        };
        return (settings, repairs);
    }

    private static double ClampFinite(double value, double min, double max, double fallback, string name, List<string> repairs)
    {
        var result = double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
        if (result != value) repairs.Add($"{name} set to {result:0.##}.");
        return result;
    }

    private static bool IsUsable(WindowPlacement? p) => p is not null
        && double.IsFinite(p.Left) && double.IsFinite(p.Top) && double.IsFinite(p.Width) && double.IsFinite(p.Height)
        && Math.Abs(p.Left) <= MaxWindowOffset && Math.Abs(p.Top) <= MaxWindowOffset
        && p.Width is >= MinWindowSize and <= MaxWindowSize && p.Height is >= MinWindowSize and <= MaxWindowSize;

    private static string Truncate(string text) => text.Length <= 40 ? text : text[..40] + "...";
}
