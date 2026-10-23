namespace ScarletMeter.WorldEvents;

/// <summary>Where an event stands at a given instant, as the fork's <c>occurrence()</c> names it.</summary>
public enum EventPhase
{
    /// <summary>No window is running; the event starts at <see cref="EventOccurrence.Start"/>.</summary>
    Upcoming,

    /// <summary>A rift's portal is open: players can still enter.</summary>
    PortalOpen,

    /// <summary>Another event's entry window is open: players can still join.</summary>
    EntryOpen,

    /// <summary>The window is running but its entry window has closed.</summary>
    ActiveEntryClosed,

    /// <summary>The scheduled window is running and has no entry window. A boss may already be dead.</summary>
    ScheduledActive,
}

/// <summary>
/// One rule's state at an instant. All instants are in UTC (offset zero); convert them only for display.
/// Start, End and EntryEnd describe the same occurrence: the running one while active, otherwise the next one.
/// </summary>
/// <param name="Rule">The rule this describes.</param>
/// <param name="Phase">Where the event stands.</param>
/// <param name="Start">The running occurrence's start while active, otherwise the next start.</param>
/// <param name="Next">The first start strictly after the instant asked about.</param>
/// <param name="End">End of the occurrence's activity: Start plus the active minutes (or the duration).</param>
/// <param name="EntryEnd">When the occurrence's entry window closes, or null when the rule has none.</param>
/// <param name="TargetEnd">What the countdown runs to: the entry close while entry is open, the end while otherwise active, else the next start.</param>
/// <param name="Remaining">Time from the instant asked about to <paramref name="TargetEnd"/>; always positive.</param>
/// <param name="Progress">0–100: elapsed share of the countdown while active; while upcoming, the share of the final hour that has passed.</param>
public readonly record struct EventOccurrence(
    EventRule Rule,
    EventPhase Phase,
    DateTimeOffset Start,
    DateTimeOffset Next,
    DateTimeOffset End,
    DateTimeOffset? EntryEnd,
    DateTimeOffset TargetEnd,
    TimeSpan Remaining,
    double Progress)
{
    /// <summary>True while a scheduled window runs (any phase but <see cref="EventPhase.Upcoming"/>).</summary>
    public bool IsActive => Phase != EventPhase.Upcoming;

    /// <summary>True while players can still enter or join: <see cref="EventPhase.PortalOpen"/> or <see cref="EventPhase.EntryOpen"/>.</summary>
    public bool IsEntryOpen => Phase is EventPhase.PortalOpen or EventPhase.EntryOpen;
}

/// <summary>
/// Recurrence: the fork's <c>occurrence()</c> and <c>timeline()</c>. Pure and independent of the PC's time
/// zone: instants are compared in UTC, a server-clock rule is shifted by the fixed offset the caller passes,
/// and the current instant is a parameter, never read from the system clock.
/// </summary>
public static class EventSchedule
{
    /// <summary>
    /// The state of <paramref name="rule"/> at <paramref name="now"/>. A start at exactly <paramref name="now"/>
    /// has begun, and a window or entry window that ends at exactly <paramref name="now"/> has closed. A
    /// duration of 0 is never active (resets, unknown boss end times).
    /// </summary>
    /// <exception cref="ArgumentException">The rule fails <see cref="ScheduleValidation.ValidateRules"/>'s value checks.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The server offset fails <see cref="ScheduleValidation.ValidateServerOffset"/>.</exception>
    public static EventOccurrence Occurrence(EventRule rule, DateTimeOffset now, TimeSpan serverOffset)
    {
        ScheduleValidation.ThrowIfUnschedulable(rule, nameof(rule));
        ScheduleValidation.ThrowIfInvalidServerOffset(serverOffset, nameof(serverOffset));

        var nowTicks = now.UtcTicks;
        var (previous, next) = SurroundingStarts(rule, nowTicks, serverOffset);
        var activeTicks = (rule.ActiveMinutes ?? rule.DurationMinutes) * TimeSpan.TicksPerMinute;
        var active = nowTicks < previous + activeTicks;
        var start = active ? previous : next;

        var entryMinutes = rule.EntryMinutes ?? (rule.Kind == EventKind.Rift ? rule.DurationMinutes : null);
        long? entryEnd = entryMinutes is { } minutes ? start + minutes * TimeSpan.TicksPerMinute : null;
        var entryOpen = active && nowTicks < entryEnd;
        var end = start + activeTicks;

        var phase = !active ? EventPhase.Upcoming
            : entryEnd is null ? EventPhase.ScheduledActive
            : !entryOpen ? EventPhase.ActiveEntryClosed
            : rule.Kind == EventKind.Rift ? EventPhase.PortalOpen
            : EventPhase.EntryOpen;
        var target = !active ? next : entryOpen ? entryEnd!.Value : end;
        var progress = active
            ? Math.Min(100, (nowTicks - start) * 100.0 / (target - start))
            : Math.Clamp((1 - (double)(next - nowTicks) / TimeSpan.TicksPerHour) * 100, 0, 100);

        return new EventOccurrence(
            rule,
            phase,
            Utc(start),
            Utc(next),
            Utc(end),
            entryEnd is { } closes ? Utc(closes) : null,
            Utc(target),
            new TimeSpan(target - nowTicks),
            progress);
    }

    /// <summary>
    /// Every rule's occurrence at <paramref name="now"/>, active ones first, then by time remaining, then by id
    /// (ordinal), as the fork's <c>timeline()</c> orders them. Simultaneous events are all kept.
    /// </summary>
    /// <exception cref="ArgumentException">A rule is null or cannot be scheduled.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The server offset is invalid.</exception>
    public static IReadOnlyList<EventOccurrence> Timeline(IReadOnlyList<EventRule> rules, DateTimeOffset now, TimeSpan serverOffset)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var occurrences = new EventOccurrence[rules.Count];
        for (var i = 0; i < occurrences.Length; i++)
        {
            occurrences[i] = Occurrence(rules[i] ?? throw new ArgumentException("A rule is null.", nameof(rules)), now, serverOffset);
        }

        Array.Sort(occurrences, TimelineOrder);
        return occurrences;
    }

    // The fork's timeline() order; the id makes it total, so the unstable Array.Sort is deterministic.
    private static readonly Comparison<EventOccurrence> TimelineOrder = static (a, b) =>
    {
        var byActive = b.IsActive.CompareTo(a.IsActive);
        if (byActive != 0) return byActive;
        var byRemaining = a.Remaining.CompareTo(b.Remaining);
        return byRemaining != 0 ? byRemaining : string.CompareOrdinal(a.Rule.Id, b.Rule.Id);
    };

    // The latest start at or before nowTicks and the earliest after it, as UTC ticks. Days are evaluated in
    // the rule's own clock (UTC, or UTC plus the server offset). A valid rule starts at least once in every
    // seven consecutive days, so scanning a week either side of today always finds both; nothing is allocated.
    private static (long Previous, long Next) SurroundingStarts(EventRule rule, long nowTicks, TimeSpan serverOffset)
    {
        var shift = rule.Basis == ScheduleBasis.Server ? serverOffset.Ticks : 0;
        var today = FloorDiv(nowTicks + shift, TimeSpan.TicksPerDay);
        var minuteTicks = rule.Minute * TimeSpan.TicksPerMinute;
        var hours = rule.Hours;
        long previous = long.MinValue, next = long.MaxValue;
        for (var day = today - 7; day <= today + 7; day++)
        {
            if (rule.Days is { } days && !Contains(days, WeekdayOf(day))) continue;
            var midnight = day * TimeSpan.TicksPerDay - shift;
            for (var i = 0; i < hours.Count; i++)
            {
                var start = midnight + hours[i] * TimeSpan.TicksPerHour + minuteTicks;
                if (start <= nowTicks)
                {
                    if (start > previous) previous = start;
                }
                else if (start < next)
                {
                    next = start;
                }
            }
        }

        return (previous, next);
    }

    // Day 0 of DateTime's tick count, 1 January 0001, was a Monday; 0 = Sunday as in Days.
    private static int WeekdayOf(long day) => (int)(((day + 1) % 7 + 7) % 7);

    // Rounds toward negative infinity, so an instant before day 0 of the rule's clock still lands on its day.
    private static long FloorDiv(long value, long divisor) => value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);

    // List membership by index, which (unlike LINQ or foreach over the interface) allocates no enumerator.
    private static bool Contains(IReadOnlyList<int> values, int value)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] == value) return true;
        }

        return false;
    }

    // Throws ArgumentOutOfRangeException within a week of DateTimeOffset's limits, which no real clock reaches.
    private static DateTimeOffset Utc(long ticks) => new(ticks, TimeSpan.Zero);
}
