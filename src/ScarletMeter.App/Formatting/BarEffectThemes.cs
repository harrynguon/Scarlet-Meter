namespace ScarletMeter.App.Formatting;

/// <summary>
/// The animated effect on a player's bar, chosen by class (Components/BarEffect.razor draws it; app.css,
/// ".bar-fx.theme-NAME", animates it). Loose, abstract takes on each class: Gladiator cleaving slashes, Templar a
/// shield's sheen and facets, Assassin poison and daggers, Ranger a volley of arrows, Sorcerer fire and ice,
/// Spiritmaster drifting spirits, Cleric holy rays and sparkles, Chanter mantra rings, Brawler impacts at the bar's
/// tip. An unknown class (or a summon) gets lightning.
/// </summary>
public static class BarEffectThemes
{
    /// <summary>The theme for unknown classes.</summary>
    public const string Default = "lightning";

    /// <summary>Every theme name, for the styles and the tests.</summary>
    public static IReadOnlyList<string> All { get; } =
        ["cleave", "aegis", "venom", "volley", "fire-ice", "spirits", "radiance", "mantra", "impact", Default];

    /// <summary>
    /// The pieces a theme draws, in order, each an &lt;i&gt; with this CSS class (app.css places and times them by
    /// position). Lightning draws an SVG instead and has none.
    /// </summary>
    public static IReadOnlyList<string> Pieces(string theme) => theme switch
    {
        "cleave" => Repeat("slash", 3),
        "aegis" => ["sheen", .. Repeat("facet", 3)],
        "venom" => [.. Repeat("dagger", 2), .. Repeat("drip", 6)],
        "volley" => Repeat("arrow", 4),
        "fire-ice" => [.. Repeat("ember", 6), .. Repeat("shard", 4)],
        "spirits" => Repeat("wisp", 4),
        "radiance" => [.. Repeat("ray", 3), .. Repeat("sparkle", 5)],
        "mantra" => Repeat("ring", 4),
        "impact" => ["burst", "shock", "flush"],
        _ => [],
    };

    private static string[] Repeat(string piece, int count) => Enumerable.Repeat(piece, count).ToArray();

    /// <summary>The theme name for a class id (upstream's classes.json).</summary>
    public static string For(int classId) => classId switch
    {
        11 => "cleave",   // Gladiator
        12 => "aegis",    // Templar
        13 => "venom",    // Assassin
        14 => "volley",   // Ranger
        15 => "fire-ice", // Sorcerer
        16 => "spirits",  // Spiritmaster
        17 => "radiance", // Cleric
        18 => "mantra",   // Chanter
        19 => "impact",   // Brawler
        _ => Default,
    };
}
