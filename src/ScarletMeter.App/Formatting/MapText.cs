using System.Globalization;
using ScarletMeter.Engine.Maps;

namespace ScarletMeter.App.Formatting;

/// <summary>
/// The map line in Settings, About: the name of the map the game last loaded and its id ("Fire Temple (map 600021)"),
/// the id alone when no source names it, so the player can still note it. The id is written without separators, as
/// the log writes it, so the two can be matched.
/// </summary>
public static class MapText
{
    /// <summary>
    /// The line for the map last loaded, or for none known (null): none read yet, or the game changed server since and
    /// the map record that came with it was missed.
    /// </summary>
    public static string Status(int? mapId)
    {
        if (mapId is not { } id) return "Not known: shown at the next map load the meter reads (one right after changing server can be missed).";
        var number = id.ToString(CultureInfo.InvariantCulture);
        return MapNames.Of(id) is { } name ? $"{name} (map {number})" : $"Map {number} (no name known)";
    }
}
