namespace ScarletMeter.WorldEvents;

/// <summary>
/// One problem with a schedule or a server offset. <paramref name="RuleId"/> is the catalog id of the entry
/// concerned, or null for a problem with the list as a whole or with an entry whose id is not in the catalog.
/// Text from the input is never copied into an error, so errors are safe to log or show.
/// </summary>
public sealed record ScheduleError(string? RuleId, string Message);

/// <summary>
/// The checks the fork's <c>validateEvents</c> and <c>validateState</c> make, as lists of errors rather than a
/// first exception, so a host can reject or repair rules and offsets it loads from its settings file.
/// </summary>
public static class ScheduleValidation
{
    /// <summary>Longest duration or activity window a rule may have: one day.</summary>
    public const int MaximumWindowMinutes = 1440;

    /// <summary>Lowest accepted server offset, UTC−12.</summary>
    public static TimeSpan MinimumServerOffset { get; } = TimeSpan.FromHours(-12);

    /// <summary>Highest accepted server offset, UTC+14.</summary>
    public static TimeSpan MaximumServerOffset { get; } = TimeSpan.FromHours(14);

    // Constant messages, so the calculations' precondition check allocates nothing.
    private const string BasisMessage = "Choose UTC or server time.";
    private const string HoursMessage = "Hours must be one or more of 0–23.";
    private const string DaysMessage = "Days must be one or more of 0–6 (Sunday–Saturday), or every day.";
    private const string MinuteMessage = "Minutes must be 0–59.";
    private const string DurationMessage = "Duration must be 0–1440 minutes.";
    private const string EntryMessage = "Entry minutes must be between zero and the duration.";
    private const string ActiveMessage = "Active minutes must be at least the duration, at most 1440.";
    private const string RiftActivityMessage = "The rift's active minutes are missing.";
    private const string OffsetRangeMessage = "Server UTC offset must be −12 to +14.";
    private const string OffsetMinutesMessage = "Server UTC offset must be a whole number of minutes.";

    /// <summary>
    /// Every problem with a saved schedule: each of the nine catalog ids exactly once (missing, duplicate and
    /// unknown entries are reported), a defined clock, at least one hour and only 0–23, days null or at least
    /// one and only 0–6, minute 0–59, duration 0–1440, entry window 0 to the duration, activity from the duration
    /// to 1440, and an activity length for the rift. Kinds come from the catalog, not from the input. Empty
    /// means valid. Unsorted or repeated hours and days are not errors: rules normalise them.
    /// </summary>
    public static IReadOnlyList<ScheduleError> ValidateRules(IReadOnlyList<EventRule>? rules)
    {
        if (rules is null) return [new ScheduleError(null, "The schedule has no entries.")];

        var errors = new List<ScheduleError>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            if (rule is null)
            {
                errors.Add(new ScheduleError(null, "A schedule entry is empty."));
            }
            else if (!EventCatalog.TryGetKind(rule.Id, out var kind))
            {
                errors.Add(new ScheduleError(null, "A schedule entry has an unknown id."));
            }
            else if (!seen.Add(rule.Id))
            {
                errors.Add(new ScheduleError(rule.Id, "The schedule lists this entry more than once."));
            }
            else
            {
                Check(rule, requireActivity: kind == EventKind.Rift, errors);
            }
        }

        foreach (var id in EventCatalog.Ids)
        {
            if (!seen.Contains(id)) errors.Add(new ScheduleError(id, "The schedule is missing this entry."));
        }

        return errors;
    }

    /// <summary>
    /// Problems with a server offset: it must lie within UTC−12 to UTC+14 and be a whole number of minutes
    /// (fractional hours such as +5:30 or +5:45 are fine; whole minutes keep it usable as a
    /// <see cref="DateTimeOffset"/> offset). Empty means valid.
    /// </summary>
    public static IReadOnlyList<ScheduleError> ValidateServerOffset(TimeSpan offset) =>
        CheckServerOffset(offset) is { } problem ? [new ScheduleError(null, problem)] : [];

    // The calculations' guard: an unusable offset is a caller bug, reported as ArgumentOutOfRangeException.
    internal static void ThrowIfInvalidServerOffset(TimeSpan offset, string paramName)
    {
        if (CheckServerOffset(offset) is { } problem) throw new ArgumentOutOfRangeException(paramName, offset, problem);
    }

    // The calculations' guard: a rule that fails validation would give meaningless instants.
    internal static void ThrowIfUnschedulable(EventRule rule, string paramName)
    {
        ArgumentNullException.ThrowIfNull(rule, paramName);
        if (Check(rule, requireActivity: false, errors: null) is { } problem)
        {
            var id = EventCatalog.TryGetKind(rule.Id, out _) ? rule.Id : "with an unknown id";
            throw new ArgumentException($"Rule {id} cannot be scheduled: {problem}", paramName);
        }
    }

    // The problem with a server offset, or null when it is usable.
    private static string? CheckServerOffset(TimeSpan offset) =>
        offset < MinimumServerOffset || offset > MaximumServerOffset ? OffsetRangeMessage
        : offset.Ticks % TimeSpan.TicksPerMinute != 0 ? OffsetMinutesMessage
        : null;

    // Records every problem with one rule's editable values in errors (when given, under the rule's id, which
    // the caller has checked is a catalog id) and returns the first. Relies on Hours and Days being sorted.
    private static string? Check(EventRule rule, bool requireActivity, List<ScheduleError>? errors)
    {
        string? first = null;
        if (rule.Basis is not (ScheduleBasis.Utc or ScheduleBasis.Server)) Report(BasisMessage);
        if (!AllWithin(rule.Hours, 0, 23)) Report(HoursMessage);
        if (rule.Days is { } days && !AllWithin(days, 0, 6)) Report(DaysMessage);
        if (rule.Minute is < 0 or > 59) Report(MinuteMessage);
        if (rule.DurationMinutes is < 0 or > MaximumWindowMinutes) Report(DurationMessage);
        if (rule.EntryMinutes is { } entry && (entry < 0 || entry > rule.DurationMinutes)) Report(EntryMessage);
        if (rule.ActiveMinutes is { } active)
        {
            if (active < rule.DurationMinutes || active > MaximumWindowMinutes) Report(ActiveMessage);
        }
        else if (requireActivity)
        {
            Report(RiftActivityMessage);
        }

        return first;

        void Report(string message)
        {
            first ??= message;
            errors?.Add(new ScheduleError(rule.Id, message));
        }
    }

    // Non-empty and inside [min, max]; the list is ascending, so its ends decide.
    private static bool AllWithin(IReadOnlyList<int> values, int min, int max) =>
        values.Count > 0 && values[0] >= min && values[values.Count - 1] <= max;
}
