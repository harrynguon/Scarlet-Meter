using System.Text.RegularExpressions;

namespace ScarletMeter.Engine.Upstream;

/// <summary>
/// AION 2 Global's names where upstream's game data names something differently: RATmeter's classes.json calls the
/// class Global calls the Spiritmaster "Elementalist" (and its summons "Elementalist spirit"). Applied where the
/// engine maps upstream's class and summon names (<see cref="UpstreamMapping"/>), so the overlay, details, history
/// and saved fights all show Global's names while upstream's data stays unedited. Whole words only, case as written.
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
