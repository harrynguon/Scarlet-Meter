using System.Globalization;

namespace ScarletMeter.App.Formatting;

/// <summary>Compact, culture-invariant number and duration formatting for the meter (1.23M, 12.3K, 03:45).</summary>
public static class Compact
{
    public static string Number(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) value = 0;
        return Number((long)Math.Round(value));
    }

    public static string Number(long value) => value switch
    {
        >= 1_000_000_000 => (value / 1e9).ToString("F2", CultureInfo.InvariantCulture) + "B",
        >= 1_000_000 => (value / 1e6).ToString("F2", CultureInfo.InvariantCulture) + "M",
        >= 1_000 => (value / 1e3).ToString("F1", CultureInfo.InvariantCulture) + "K",
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    public static string Duration(TimeSpan duration) => duration.TotalHours >= 1
        ? $"{(int)duration.TotalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}"
        : $"{duration.Minutes:D2}:{duration.Seconds:D2}";

    /// <summary>A percentage clamped to 0 to 100 with one decimal, safe for CSS widths in any culture.</summary>
    public static string Percent(double value) =>
        Math.Clamp(value, 0, 100).ToString("F1", CultureInfo.InvariantCulture) + "%";
}
