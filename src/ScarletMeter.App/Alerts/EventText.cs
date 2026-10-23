using System.Globalization;
using ScarletMeter.WorldEvents;

namespace ScarletMeter.App.Alerts;

/// <summary>Wording for world events: notice text, countdowns and a one-line schedule summary.</summary>
public static class EventText
{
    private static readonly string[] DayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    /// <summary>What an open entry window is called: a portal for the rift, entry for other events.</summary>
    public static string OpenLabel(EventRule rule) => rule.Kind == EventKind.Rift ? "Portal open" : "Entry open";

    /// <summary>A short countdown: "4:59" under an hour, "1 h 05 m" beyond.</summary>
    public static string Countdown(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        return remaining.TotalHours >= 1
            ? $"{(int)remaining.TotalHours} h {remaining.Minutes:00} m"
            : $"{(int)remaining.TotalMinutes}:{remaining.Seconds:00}";
    }

    /// <summary>An instant in the PC's local time ("21:05").</summary>
    public static string LocalTime(DateTimeOffset instant) =>
        instant.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>When a rule recurs, for the settings list: "Every 3 h from 02:00 server time · portal 10 min".</summary>
    public static string Schedule(EventRule rule)
    {
        var clock = rule.Basis == ScheduleBasis.Utc ? "UTC" : "server time";
        var hours = rule.Hours;
        string when;
        if (hours.Count == 24)
            when = $"Every hour at :{rule.Minute:00}";
        else if (hours.Count > 2 && Step(hours) is { } step)
            when = $"Every {step} h from {hours[0]:00}:{rule.Minute:00}";
        else
            when = string.Join(", ", hours.Select(h => $"{h:00}:{rule.Minute:00}"));

        var days = rule.Days is { Count: > 0 and < 7 } list ? string.Join(", ", list.Select(d => DayNames[d])) + " " : string.Empty;
        var window = rule.Kind == EventKind.Rift && (rule.EntryMinutes ?? rule.DurationMinutes) > 0
            ? $" · portal {rule.EntryMinutes ?? rule.DurationMinutes} min"
            : rule.EntryMinutes is > 0 ? $" · entry {rule.EntryMinutes} min" : string.Empty;
        return $"{days}{when} {clock}{window}";
    }

    // The common gap when hours are evenly spaced around the clock, else null.
    private static int? Step(IReadOnlyList<int> hours)
    {
        var step = hours[1] - hours[0];
        for (var i = 2; i < hours.Count; i++)
            if (hours[i] - hours[i - 1] != step) return null;
        return step > 0 && 24 % step == 0 && hours.Count == 24 / step ? step : null;
    }
}
