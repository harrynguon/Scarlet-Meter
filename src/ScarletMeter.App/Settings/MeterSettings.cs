namespace ScarletMeter.App.Settings;

/// <summary>
/// Everything the player can change, saved as settings.json in the data directory. Every section has
/// defaults, so a file written by an older build (or missing a section) loads with the rest at their
/// defaults; <see cref="SettingsNormalizer"/> repairs values that are out of range. Immutable: changes go
/// through <see cref="SettingsService.Update"/>.
/// </summary>
public sealed record MeterSettings
{
    /// <summary>The file format this build writes. A file with a higher version is read but never overwritten.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public FightSettings Fight { get; init; } = new();

    public AppearanceSettings Appearance { get; init; } = new();

    public OverlaySettings Overlay { get; init; } = new();

    /// <summary>Saved window positions and sizes, keyed by <see cref="WindowKeys"/>.</summary>
    public IReadOnlyDictionary<string, WindowPlacement> Windows { get; init; } = new Dictionary<string, WindowPlacement>();

    // Value equality including the window dictionary (records compare collections by reference), so an
    // update that changes nothing is recognised as such. A test checks every property takes part.
    public bool Equals(MeterSettings? other) => other is not null
        && Version == other.Version && Fight == other.Fight && Appearance == other.Appearance && Overlay == other.Overlay
        && Windows.Count == other.Windows.Count
        && Windows.All(pair => other.Windows.TryGetValue(pair.Key, out var placement) && placement == pair.Value);

    public override int GetHashCode() => HashCode.Combine(Version, Fight, Appearance, Overlay, Windows.Count);
}

/// <summary>Fight rules upstream applies to every hit; mirrored into the engine's EngineSettings.</summary>
public sealed record FightSettings
{
    public const int MinRetentionDays = 1;
    public const int MaxRetentionDays = 9999;

    /// <summary>Only count hits on bosses.</summary>
    public bool BossOnlyCapture { get; init; }

    /// <summary>Fold a summon's damage into its owner's skill breakdown.</summary>
    public bool GroupSummonDamage { get; init; } = true;

    /// <summary>Stop counting hits on a training dummy one minute after the first hit.</summary>
    public bool OneMinuteDummyMode { get; init; }

    /// <summary>Days of boss-fight history to keep.</summary>
    public int HistoryRetentionDays { get; init; } = 30;
}

/// <summary>How the overlay looks. Applied live.</summary>
public sealed record AppearanceSettings
{
    public const double MinOpacity = 0.1;
    public const double MaxOpacity = 1.0;
    public const double MinRowScale = 0.75;
    public const double MaxRowScale = 1.5;

    /// <summary>Opacity of the overlay's background (0.1 to 1); text and bars stay opaque.</summary>
    public double BackgroundOpacity { get; init; } = 0.9;

    /// <summary>
    /// File name of the chosen background image inside the data directory's backgrounds folder, or null. Only
    /// names <see cref="BackgroundImages"/> writes are accepted, so a settings file cannot point elsewhere.
    /// </summary>
    public string? BackgroundImage { get; init; }

    /// <summary>Colour each player's bar by class.</summary>
    public bool ClassColors { get; init; } = true;

    /// <summary>Replace other players' names with their class (your own name stays).</summary>
    public bool HideNicknames { get; init; }

    /// <summary>Show each player's death count.</summary>
    public bool ShowDeaths { get; init; } = true;

    /// <summary>Bars relative to the top player instead of each player's share of the damage.</summary>
    public bool RelativeBars { get; init; }

    /// <summary>Size of the meter's rows and text (0.75 to 1.5).</summary>
    public double RowScale { get; init; } = 1.0;
}

/// <summary>Overlay behaviour.</summary>
public sealed record OverlaySettings
{
    /// <summary>
    /// Global shortcut that minimizes or restores the meter, in <see cref="HotkeyGesture"/> text form; empty for
    /// none. RATmeter's default, so the habit carries over. A saved empty value stays empty.
    /// </summary>
    public string ToggleHotkey { get; init; } = "Ctrl+Shift+D";
}

/// <summary>A window's position and size in device-independent pixels.</summary>
public sealed record WindowPlacement(double Left, double Top, double Width, double Height);

/// <summary>The windows whose placement is remembered.</summary>
public static class WindowKeys
{
    public const string Overlay = "overlay";
    public const string Settings = "settings";
    public const string History = "history";
    public const string PlayerDetails = "details";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Overlay, Settings, History, PlayerDetails,
    };
}
