using System.Globalization;
using ScarletMeter.App.Roster;
using ScarletMeter.Engine;

namespace ScarletMeter.App.Formatting;

/// <summary>
/// The Dimensional Invasion reward keys as the overlay writes them, the way the game's dialog does: keys left out of 7
/// ("3/7"), any extra from recharge items in brackets before the cap ("3 (+2)/7").
/// </summary>
public static class InvasionText
{
    /// <summary>The keys left, with a thousands separator.</summary>
    public static string Keys(InvasionKeys keys) => keys.Keys.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>The extra in brackets after a space (" (+2)"), or empty when there is none, to keep the line short.</summary>
    public static string Extra(InvasionKeys keys) => keys.Extra > 0 ? " " + RosterText.Extra(keys.Extra, always: false) : string.Empty;

    /// <summary>The cap after the count, straight after it ("/7"), from the rules.</summary>
    public static string OutOf { get; } = RosterText.OutOf(RosterRules.Invasion.Cap);
}