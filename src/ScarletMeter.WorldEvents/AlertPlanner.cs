namespace ScarletMeter.WorldEvents;

/// <summary>Why an event is being announced.</summary>
public enum AlertKind
{
    /// <summary>The event starts within the lead time.</summary>
    Soon,

    /// <summary>The event's portal or entry window is open; the alert counts down to its close.</summary>
    Open,
}

/// <summary>
/// One notice for the overlay. <paramref name="Deadline"/> is what it counts down to: the start for
/// <see cref="AlertKind.Soon"/>, the portal or entry close for <see cref="AlertKind.Open"/>. Instants are UTC.
/// </summary>
/// <param name="Rule">The announced rule (its name, location and kind are for display).</param>
/// <param name="Kind">Soon or Open.</param>
/// <param name="Start">The occurrence's start: ahead for Soon, at or before the planned instant for Open.</param>
/// <param name="Deadline">The start (Soon) or the entry close (Open).</param>
/// <param name="Remaining">Time from the planned instant to <paramref name="Deadline"/>; always positive.</param>
public readonly record struct EventAlert(EventRule Rule, AlertKind Kind, DateTimeOffset Start, DateTimeOffset Deadline, TimeSpan Remaining);

/// <summary>
/// The alerts to show at one instant, most urgent first, and <paramref name="NextChange"/>: the first later
/// instant at which they differ (an alert appears, disappears or moves to another occurrence). Until then only
/// the countdowns move, so the host can show these alerts, count down to each <see cref="EventAlert.Deadline"/>
/// itself and plan again at <paramref name="NextChange"/> instead of every second. Null means no rule is
/// enabled, so nothing will change until the rules or the enabled set do.
/// </summary>
public readonly record struct AlertPlan(IReadOnlyList<EventAlert> Alerts, DateTimeOffset? NextChange);

/// <summary>
/// Chooses the overlay's world-event notices. Pure: the same rules, enabled set, instant, offset and lead time
/// always give the same plan. Per enabled rule it shows at most one alert:
/// <list type="bullet">
/// <item><see cref="AlertKind.Open"/> while the rule's portal or entry window is open (start inclusive, close
/// exclusive). Resets never get it.</item>
/// <item>Otherwise <see cref="AlertKind.Soon"/> while the next start is at most the lead time away (the instant
/// the lead time is reached inclusive, the start exclusive).</item>
/// </list>
/// An event that is active without an open entry window (for example a boss or siege window, whose end times
/// are unknown in the Global preset) gets no alert, though its next start still gets Soon. Order: Open before
/// Soon, then the earliest deadline, then id (ordinal).
/// </summary>
public static class AlertPlanner
{
    /// <summary>How long before a start the Soon alert appears unless the host chooses otherwise: five minutes.</summary>
    public static TimeSpan DefaultLeadTime { get; } = TimeSpan.FromMinutes(5);

    /// <summary>Shortest accepted lead time: one minute.</summary>
    public static TimeSpan MinimumLeadTime { get; } = TimeSpan.FromMinutes(1);

    /// <summary>Longest accepted lead time: sixty minutes.</summary>
    public static TimeSpan MaximumLeadTime { get; } = TimeSpan.FromMinutes(60);

    // Open before Soon, then the earliest deadline, then id: a total order, so the unstable sort is deterministic.
    private static readonly Comparison<EventAlert> Urgency = static (a, b) =>
    {
        var byKind = b.Kind.CompareTo(a.Kind); // Open (1) before Soon (0)
        if (byKind != 0) return byKind;
        var byDeadline = a.Deadline.CompareTo(b.Deadline);
        return byDeadline != 0 ? byDeadline : string.CompareOrdinal(a.Rule.Id, b.Rule.Id);
    };

    /// <summary>The alerts at <paramref name="now"/> with the <see cref="DefaultLeadTime"/>.</summary>
    /// <inheritdoc cref="Plan(IReadOnlyList{EventRule}, IReadOnlySet{string}, DateTimeOffset, TimeSpan, TimeSpan)"/>
    public static AlertPlan Plan(IReadOnlyList<EventRule> rules, IReadOnlySet<string> enabledIds, DateTimeOffset now, TimeSpan serverOffset) =>
        Plan(rules, enabledIds, now, serverOffset, DefaultLeadTime);

    /// <summary>
    /// The alerts at <paramref name="now"/> for the rules whose ids are in <paramref name="enabledIds"/>; other
    /// rules are skipped without being checked. Allocates nothing when there is no alert.
    /// </summary>
    /// <param name="rules">Validated rules, for example <see cref="EventCatalog.Default"/>.</param>
    /// <param name="enabledIds">Ids of the rules to announce; the set's own comparer applies.</param>
    /// <param name="now">The instant to plan for, in any offset.</param>
    /// <param name="serverOffset">The game server's fixed UTC offset.</param>
    /// <param name="leadTime">How long before a start Soon appears: <see cref="MinimumLeadTime"/> to <see cref="MaximumLeadTime"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The lead time or the server offset is out of range.</exception>
    /// <exception cref="ArgumentException">An enabled rule cannot be scheduled, or a rule is null.</exception>
    public static AlertPlan Plan(
        IReadOnlyList<EventRule> rules,
        IReadOnlySet<string> enabledIds,
        DateTimeOffset now,
        TimeSpan serverOffset,
        TimeSpan leadTime)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(enabledIds);
        if (leadTime < MinimumLeadTime || leadTime > MaximumLeadTime)
            throw new ArgumentOutOfRangeException(nameof(leadTime), leadTime, "The lead time must be 1–60 minutes.");
        ScheduleValidation.ThrowIfInvalidServerOffset(serverOffset, nameof(serverOffset));

        EventAlert[]? alerts = null;
        var count = 0;
        DateTimeOffset? nextChange = null;
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i] ?? throw new ArgumentException("A rule is null.", nameof(rules));
            if (!enabledIds.Contains(rule.Id)) continue;

            var occurrence = EventSchedule.Occurrence(rule, now, serverOffset);
            EventAlert? alert;
            DateTimeOffset change;
            if (occurrence.IsEntryOpen && rule.Kind != EventKind.Reset)
            {
                // Open hides Soon. It ends at the close, or at the next start if windows overlap.
                var closes = occurrence.EntryEnd!.Value;
                alert = new EventAlert(rule, AlertKind.Open, occurrence.Start, closes, closes - now);
                change = closes < occurrence.Next ? closes : occurrence.Next;
            }
            else
            {
                // No entry window can open before the next start, so only Soon's appearance or the start
                // (which ends Soon and may open an entry window) can change this rule's alert.
                var soonFrom = occurrence.Next - leadTime;
                alert = now >= soonFrom
                    ? new EventAlert(rule, AlertKind.Soon, occurrence.Next, occurrence.Next, occurrence.Next - now)
                    : null;
                change = now >= soonFrom ? occurrence.Next : soonFrom;
            }

            if (alert is { } shown) (alerts ??= new EventAlert[rules.Count - i])[count++] = shown;
            if (nextChange is null || change < nextChange) nextChange = change;
        }

        if (alerts is null) return new AlertPlan([], nextChange);
        if (count < alerts.Length) Array.Resize(ref alerts, count);
        Array.Sort(alerts, Urgency);
        return new AlertPlan(alerts, nextChange);
    }
}
