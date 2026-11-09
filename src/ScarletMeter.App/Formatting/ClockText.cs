using System.Globalization;

namespace ScarletMeter.App.Formatting;

/// <summary>
/// Every clock time a player reads, on the 12-hour clock with a lowercase am or pm straight after: "9:34pm", "9:34am",
/// "12:05am" just after midnight and "12:00pm" at noon (Harry, 2026-11-09: "change all timestamps from 24h to 12hr format
/// like 9:34pm, 9:34am"). Written the same whatever the PC's language. Each method takes a time already in the zone it is
/// shown in; converting is the caller's. The log file keeps the 24-hour clock (Logging/LogFloodGuard), which sorts and
/// reads the same everywhere.
/// </summary>
public static class ClockText
{
    /// <summary>A time of day to the minute: "9:34pm", "12:05am".</summary>
    public static string Time(DateTime time) => Time(time.Hour, time.Minute);

    /// <summary>An hour (0 to 23) and minute (0 to 59) of the day, as <see cref="Time(DateTime)"/>: for a schedule's hours.</summary>
    public static string Time(int hour, int minute)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(hour);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hour, 23);
        ArgumentOutOfRangeException.ThrowIfNegative(minute);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minute, 59);
        return string.Create(CultureInfo.InvariantCulture, $"{Hour(hour)}:{minute:00}{Half(hour)}");
    }

    /// <summary>A time of day to the millisecond, for the combat log: "9:34:05.120pm".</summary>
    public static string Precise(DateTime time) =>
        string.Create(CultureInfo.InvariantCulture, $"{Hour(time.Hour)}:{time.Minute:00}:{time.Second:00}.{time.Millisecond:000}{Half(time.Hour)}");

    // The hour on the 12-hour clock: 12 for midnight and noon.
    private static int Hour(int hour) => hour % 12 == 0 ? 12 : hour % 12;

    private static string Half(int hour) => hour < 12 ? "am" : "pm";
}
