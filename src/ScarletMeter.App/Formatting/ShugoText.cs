using System.Globalization;
using ScarletMeter.Engine;

namespace ScarletMeter.App.Formatting;

/// <summary>The Shugo Festival keys as the overlay writes them, the way the game does: keys left, then any extra in brackets ("12 (+3)").</summary>
public static class ShugoText
{
    /// <summary>The keys left, with a thousands separator.</summary>
    public static string Keys(ShugoKeys keys) => keys.Keys.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>The extra from recharge items in brackets, signed ("(+3)"), or empty when there is none, to keep the line short.</summary>
    public static string Extra(ShugoKeys keys) =>
        keys.Extra > 0 ? "(+" + keys.Extra.ToString("N0", CultureInfo.InvariantCulture) + ")" : string.Empty;
}
