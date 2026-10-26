using ScarletMeter.App.Combat;
using ScarletMeter.WorldEvents;

namespace ScarletMeter.App.Alerts;

/// <summary>How an icon in the event row stands.</summary>
public enum EventIconState
{
    /// <summary>Not due: dim, with no time.</summary>
    Quiet,

    /// <summary>With nothing due and "show the next event" on, the next event: dim, with the time to its start.</summary>
    Next,

    /// <summary>Starts within the lead time: gold, with the time to its start.</summary>
    Soon,

    /// <summary>Its entry or portal is open: teal, with the time to its close.</summary>
    Open,
}

/// <summary>One icon in the event row.</summary>
/// <param name="Rule">The event.</param>
/// <param name="State">How it stands.</param>
/// <param name="Until">What the time on the icon counts down to, or null when it shows none.</param>
/// <param name="Alert">Its notice while it is due (Soon or Open), else null.</param>
public readonly record struct EventIcon(EventRule Rule, EventIconState State, DateTimeOffset? Until, EventAlert? Alert);

/// <summary>
/// What the overlay's event row (Components/EventRow.razor) shows: one icon per alerted event in catalog order, lit
/// while it is due with the time left (Harry asked for a row of icons in place of the list, 2026-10-26); the tip
/// that names it; the detail lines clicks open under the row; and whether the row stands aside for a boss fight.
/// Pure, so the component stays a view and the rules are tested without rendering.
/// </summary>
public static class EventRowLayout
{
    /// <summary>Notices the screen-reader summary names, most urgent first.</summary>
    public const int AnnouncedCount = 2;

    /// <summary>
    /// Whether the row stands aside for the fight under way: with "Hide during boss fights" on, while
    /// <see cref="BossFightHold"/> says a boss fight is under way (adds included, not once the boss is down, gone or
    /// long still).
    /// </summary>
    public static bool HiddenForFight(bool hideDuringBossFights, bool bossFightUnderWay) =>
        hideDuringBossFights && bossFightUnderWay;

    /// <summary>
    /// The row: every alerted event in the view's order. A due one carries its notice and counts down to its deadline
    /// (the close while open, else the start); with nothing due, the view's next event (when "show the next event" is
    /// on) counts down dimly to its start; every other icon is quiet.
    /// </summary>
    public static IReadOnlyList<EventIcon> Icons(AlertView view)
    {
        var icons = new EventIcon[view.Events.Count];
        for (var i = 0; i < icons.Length; i++)
        {
            var rule = view.Events[i];
            icons[i] = DueNotice(view.Alerts, rule.Id) is { } alert
                ? new EventIcon(rule, alert.Kind == AlertKind.Open ? EventIconState.Open : EventIconState.Soon, alert.Deadline, alert)
                : view.Next is { } next && string.Equals(next.Rule.Id, rule.Id, StringComparison.Ordinal)
                    ? new EventIcon(rule, EventIconState.Next, next.Start, null)
                    : new EventIcon(rule, EventIconState.Quiet, null, null);
        }

        return icons;
    }

    /// <summary>
    /// What an icon's event is doing, in <paramref name="zone"/>, for its tip and its accessible name: "starts 21:00"
    /// when due, "portal open until 21:10" or "entry open until 21:03" while open, and otherwise
    /// <see cref="EventText.Status"/> at <paramref name="now"/> ("next 21:00", "next Sun 21:00", "running, entry
    /// closed", "started 21:00"). Clock times rather than countdowns, which the icon itself shows.
    /// </summary>
    /// <remarks>
    /// The idle case schedules the rule again at <paramref name="now"/>, with the server offset the monitor planned
    /// with: the same rules and offset that just planned without error, so it cannot fail at a later instant.
    /// </remarks>
    public static string Status(EventIcon icon, DateTimeOffset now, TimeSpan serverOffset, TimeZoneInfo zone) => icon.Alert switch
    {
        { Kind: AlertKind.Soon } soon => $"starts {EventText.LocalTime(soon.Start, zone)}",
        { } open => $"{(open.Rule.Kind == EventKind.Rift ? "portal" : "entry")} open until {EventText.LocalTime(open.Deadline, zone)}",
        null => EventText.Status(EventSchedule.Occurrence(icon.Rule, now, serverOffset), now, zone),
    };

    /// <summary>
    /// The detail line's countdown, after the event's name: "in 54:53" before a due start; "Portal open · 6:53 left"
    /// while entry is open; and for an idle event its status with the time to its next start ("next 21:00 · in 2 h 14 m"),
    /// or the status alone while a window without open entry runs ("running, entry closed").
    /// </summary>
    public static string When(EventIcon icon, DateTimeOffset now, TimeSpan serverOffset, TimeZoneInfo zone)
    {
        if (icon.Alert is { } alert)
        {
            var remaining = alert.Deadline - now;
            return alert.Kind == AlertKind.Soon
                ? $"in {EventText.Countdown(remaining)}"
                : $"{EventText.OpenLabel(alert.Rule)} · {EventText.Countdown(remaining)} left";
        }

        var occurrence = EventSchedule.Occurrence(icon.Rule, now, serverOffset);
        var status = EventText.Status(occurrence, now, zone);
        return occurrence.IsActive ? status : $"{status} · in {EventText.Countdown(occurrence.Next - now)}";
    }

    /// <summary>
    /// The detail line's progress, 0 to 100: before a due start, how much of the lead time has passed; while open, how
    /// much of the window is left. Null for an event that is not due, whose line has none.
    /// </summary>
    public static double? Progress(EventIcon icon, DateTimeOffset now, TimeSpan lead)
    {
        if (icon.Alert is not { } alert) return null;
        var remaining = (alert.Deadline - now).TotalSeconds;
        if (alert.Kind == AlertKind.Soon)
            return lead.TotalSeconds > 0 ? Math.Clamp((1 - remaining / lead.TotalSeconds) * 100, 0, 100) : 100;
        var window = (alert.Deadline - alert.Start).TotalSeconds;
        return window > 0 ? Math.Clamp(remaining / window * 100, 0, 100) : 0;
    }

    /// <summary>
    /// The events whose detail lines are open after a click on <paramref name="clicked"/>: its line opens beside any
    /// already open, or closes when it was open (Harry asked to open several at once, 2026-10-26). A new set; the one
    /// passed in is left as it was.
    /// </summary>
    public static IReadOnlySet<string> Toggle(IReadOnlySet<string> open, string clicked)
    {
        var next = new HashSet<string>(open, StringComparer.Ordinal);
        if (!next.Remove(clicked)) next.Add(clicked);
        return next;
    }

    /// <summary>
    /// The icons whose detail lines are open, in the row's order whatever order they were clicked in; an event that
    /// has left the row (switched off) has none.
    /// </summary>
    public static IReadOnlyList<EventIcon> Opened(IReadOnlyList<EventIcon> icons, IReadOnlySet<string> open) =>
        open.Count == 0 ? [] : [.. icons.Where(icon => open.Contains(icon.Rule.Id))];

    // The planner gives each rule at most one notice.
    private static EventAlert? DueNotice(IReadOnlyList<EventAlert> alerts, string id)
    {
        foreach (var alert in alerts)
            if (string.Equals(alert.Rule.Id, id, StringComparison.Ordinal)) return alert;
        return null;
    }
}
