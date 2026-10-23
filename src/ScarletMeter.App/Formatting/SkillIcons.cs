namespace ScarletMeter.App.Formatting;

/// <summary>
/// Skill and buff icons. Upstream resolves them to images on NC's game-data CDN
/// (AionDpsMeter.Core/GameData/Services/SkillIconResolver.cs); the strings come from upstream's data, so only
/// HTTPS URLs on that one host are passed to the page, which the Content-Security-Policy also enforces.
/// </summary>
public static class SkillIcons
{
    public const string CdnPrefix = "https://assets.playnccdn.com/static-aion2-gamedata/resources/";

    /// <summary>The icon URL when it is on NC's CDN and icons are enabled, otherwise null. No default: every caller passes the player's "Skill icons" setting.</summary>
    public static string? Url(string? icon, bool enabled) =>
        enabled && icon is not null && icon.StartsWith(CdnPrefix, StringComparison.Ordinal)
            && Uri.TryCreate(icon, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "assets.playnccdn.com"
            && !icon.Contains("..", StringComparison.Ordinal)
            ? icon
            : null;
}
