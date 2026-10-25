using ScarletMeter.App.Updates;
using ScarletMeter.WorldEvents;

namespace ScarletMeter.App.Settings;

/// <summary>
/// The one validation boundary for settings, applied to everything loaded from disk and to every update.
/// Missing sections take their defaults, numbers are clamped to their ranges, and values that could point
/// outside the meter's own data (a background image name, an unknown window key) are dropped. Each repair
/// is reported so a load can say what it fixed.
/// </summary>
public static class SettingsNormalizer
{
    // A size any window allows (WindowPlacement.MinSize, the overlay's minimum height), or a player's own small
    // overlay would be dropped as unusable, and its whole placement with it.
    private const double MinWindowSize = WindowPlacement.MinSize;
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
        var pictureOpacity = ClampFinite(appearance.PictureOpacity, AppearanceSettings.MinPictureOpacity, AppearanceSettings.MaxPictureOpacity, 1.0, "Picture opacity", repairs);
        var scale = ClampFinite(appearance.RowScale, AppearanceSettings.MinRowScale, AppearanceSettings.MaxRowScale, 1.0, "Row size", repairs);
        var visibleRows = Math.Clamp(appearance.VisibleRows, AppearanceSettings.MinVisibleRows, AppearanceSettings.MaxVisibleRows);
        if (visibleRows != appearance.VisibleRows) repairs.Add($"Rows before scrolling set to {visibleRows}.");
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
            Appearance = appearance with { BackgroundOpacity = opacity, PictureOpacity = pictureOpacity, RowScale = scale, VisibleRows = visibleRows, BackgroundImage = image },
            Overlay = overlay with { ToggleHotkey = hotkey },
            Alerts = NormalizeAlerts(input.Alerts, repairs),
            Updates = NormalizeUpdates(input.Updates, repairs),
            BossTimers = input.BossTimers ?? new BossTimersSettings(),
            Windows = windows,
        };
        return (settings, repairs);
    }

    // Lead time and offset are clamped (the offset to whole minutes, as the schedule module requires); event
    // ids are kept only if the catalog knows them, once each, in catalog order.
    private static AlertSettings NormalizeAlerts(AlertSettings? input, List<string> repairs)
    {
        var alerts = input ?? new AlertSettings();
        var lead = Math.Clamp(alerts.LeadMinutes, AlertSettings.MinLeadMinutes, AlertSettings.MaxLeadMinutes);
        if (lead != alerts.LeadMinutes) repairs.Add($"Alert lead time set to {lead} minutes.");
        var offset = ClampFinite(alerts.ServerOffsetHours, AlertSettings.MinServerOffsetHours, AlertSettings.MaxServerOffsetHours, 9, "Server UTC offset", repairs);
        offset = Math.Round(offset * 60) / 60;

        var saved = alerts.Events ?? AlertSettings.DefaultEvents;
        var events = EventCatalog.Ids.Where(id => saved.Contains(id, StringComparer.Ordinal)).ToList();
        if (saved.Any(id => id is null || !EventCatalog.Ids.Contains(id, StringComparer.Ordinal)))
            repairs.Add("Unknown world events were removed from the alert list.");

        return alerts with { LeadMinutes = lead, ServerOffsetHours = offset, Events = events };
    }

    // A dismissed release must be a release tag; anything else is dropped (the notice then shows again).
    private static UpdateSettings NormalizeUpdates(UpdateSettings? input, List<string> repairs)
    {
        var updates = input ?? new UpdateSettings();
        if (updates.DismissedRelease is null || UpstreamReleaseTag.TryParse(updates.DismissedRelease, out _)) return updates;
        repairs.Add("A dismissed release that was not a release tag was cleared.");
        return updates with { DismissedRelease = null };
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
