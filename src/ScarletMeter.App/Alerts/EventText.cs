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

    /// <summary>
    /// The event row's time on an icon: minutes and seconds under an hour, as the game's own timers read ("2:19"; Harry:
    /// whole minutes rounded up read "3m" with 2:01 left, which misled); then "2h 05m" from an hour, and "6d 05h" from a
    /// day (a next event can be a week away). Like <see cref="Countdown"/>, a part second is dropped.
    /// </summary>
    public static string IconTime(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        return remaining.TotalHours < 1 ? Countdown(remaining)
            : remaining.TotalDays < 1 ? $"{(int)remaining.TotalHours}h {remaining.Minutes:00}m"
            : $"{(int)remaining.TotalDays}d {remaining.Hours:00}h";
    }

    /// <summary>
    /// A rule's state for the settings list, in <paramref name="zone"/>: "next 21:05" (with the weekday when
    /// it is not today, "next Sun 21:00"), "portal open now", "entry open now", "running, entry closed", or
    /// "started 21:00" for a window with no entry (a boss may already be dead).
    /// </summary>
    public static string Status(EventOccurrence occurrence, DateTimeOffset now, TimeZoneInfo zone)
    {
        var start = TimeZoneInfo.ConvertTime(occurrence.Start, zone);
        var time = start.ToString("HH:mm", CultureInfo.InvariantCulture);
        return occurrence.Phase switch
        {
            EventPhase.PortalOpen => "portal open now",
            EventPhase.EntryOpen => "entry open now",
            EventPhase.ActiveEntryClosed => "running, entry closed",
            EventPhase.ScheduledActive => $"started {time}",
            _ when start.Date != TimeZoneInfo.ConvertTime(now, zone).Date => $"next {DayNames[(int)start.DayOfWeek]} {time}",
            _ => $"next {time}",
        };
    }

    /// <summary>An instant in the PC's local time ("21:05").</summary>
    public static string LocalTime(DateTimeOffset instant) => LocalTime(instant, TimeZoneInfo.Local);

    /// <summary>An instant in <paramref name="zone"/> ("21:05").</summary>
    public static string LocalTime(DateTimeOffset instant, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(instant, zone).ToString("HH:mm", CultureInfo.InvariantCulture);

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
