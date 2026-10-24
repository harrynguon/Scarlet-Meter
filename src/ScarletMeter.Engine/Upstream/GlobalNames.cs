using System.Text.RegularExpressions;

namespace ScarletMeter.Engine.Upstream;

/// <summary>
/// AION 2 Global's names where upstream's game data names something differently: RATmeter's classes.json calls the
/// class Global calls the Spiritmaster "Elementalist" (and its summons' class "Elementalist spirit"). Applied where
/// the engine maps a player's class name (<see cref="UpstreamMapping.ToPlayerRow"/>), so the overlay, details,
/// history and saved fights all show Global's names while upstream's data stays unedited. Not applied to names of
/// players or summons, which are entities' own names (a summon's is "Player_{id}" or its owner-given name), so a
/// nickname containing the word stays as it is. Whole words only, case as written.
/// </summary>
internal static partial class GlobalNames
{
    private static readonly (Regex Upstream, string Global)[] Renames =
    [
        (ElementalistWord(), "Spiritmaster"),
    ];

    /// <summary>The name as AION 2 Global says it; empty for null.</summary>
    public static string Of(string? upstreamName)
    {
        if (string.IsNullOrEmpty(upstreamName)) return string.Empty;
        var name = upstreamName;
        foreach (var (upstream, global) in Renames) name = upstream.Replace(name, global);
        return name;
    }

    [GeneratedRegex(@"\bElementalist\b")]
    private static partial Regex ElementalistWord();
}
