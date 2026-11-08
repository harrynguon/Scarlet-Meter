namespace ScarletMeter.WorldEvents.FieldBosses;

/// <summary>What a field-boss notice says.</summary>
public enum FieldBossAlertKind
{
    /// <summary>The boss is up (since <see cref="FieldBossAlert.At"/>), for <see cref="FieldBossAlertPlanner.UpNoticeFor"/> after.</summary>
    Up,

    /// <summary>The boss comes back at <see cref="FieldBossAlert.At"/>, within the lead time.</summary>
    Soon,
}

/// <summary>
/// One field-boss notice: the boss, what it says, the instant it is about (the respawn for Soon; since when the boss is up
/// for Up, or when it was seen up when the source did not say since when) and the observation behind it.
/// </summary>
public sealed record FieldBossAlert(FieldBoss Boss, FieldBossAlertKind Kind, DateTimeOffset At, FieldBossTimer Timer);

/// <summary>The notices due at an instant, and the first instant after it at which they differ (null when none can).</summary>
public sealed record FieldBossAlertPlan(IReadOnlyList<FieldBossAlert> Alerts, DateTimeOffset? NextChange)
{
    public static FieldBossAlertPlan None { get; } = new([], null);
}

/// <summary>
/// Which of the chosen field bosses to show a notice for: Soon from the lead time before a respawn until it, and Up for
/// <see cref="UpNoticeFor"/> after a boss is up. A boss down with no respawn time gets none. Pure; the current instant is a
/// parameter.
/// </summary>
public static class FieldBossAlertPlanner
{
    public const int MinLeadMinutes = 1;
    public const int MaxLeadMinutes = 60;

    /// <summary>How long a boss's Up notice shows after it is up (or was seen up).</summary>
    public static TimeSpan UpNoticeFor { get; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The notices at <paramref name="now"/> for the bosses in <paramref name="chosen"/> (world-map ids), from
    /// <paramref name="board"/> (<see cref="FieldBossBoard.Merge"/>): Up first, then Soon, each by its instant, then by id.
    /// A lead time outside <see cref="MinLeadMinutes"/> to <see cref="MaxLeadMinutes"/> minutes throws.
    /// </summary>
    public static FieldBossAlertPlan Plan(
        IReadOnlyDictionary<int, FieldBossTimer> board, IReadOnlySet<int> chosen, DateTimeOffset now, TimeSpan lead)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(chosen);
        if (lead < TimeSpan.FromMinutes(MinLeadMinutes) || lead > TimeSpan.FromMinutes(MaxLeadMinutes))
            throw new ArgumentOutOfRangeException(nameof(lead), $"The lead time must be {MinLeadMinutes} to {MaxLeadMinutes} minutes.");

        List<FieldBossAlert>? alerts = null;
        DateTimeOffset? next = null;
        foreach (var id in chosen)
        {
            if (!board.TryGetValue(id, out var timer) || FieldBossCatalog.Find(id) is not { } boss) continue;
            var state = timer.StateAt(now);
            switch (state.Status)
            {
                case FieldBossStatus.Respawning when state.At is { } back:
                    var from = back - lead;
                    if (from <= now) (alerts ??= []).Add(new(boss, FieldBossAlertKind.Soon, back, timer));
                    // Before the lead time the notice appears at it; within it, it turns to Up at the respawn.
                    next = Earliest(next, from > now ? from : back);
                    break;
                case FieldBossStatus.Up:
                    var since = state.At ?? timer.ObservedAt;
                    var until = since + UpNoticeFor;
                    if (until > now)
                    {
                        (alerts ??= []).Add(new(boss, FieldBossAlertKind.Up, since, timer));
                        next = Earliest(next, until);
                    }
                    break;
            }
        }
        if (alerts is null) return next is null ? FieldBossAlertPlan.None : new([], next);
        alerts.Sort(static (a, b) =>
        {
            var kind = a.Kind.CompareTo(b.Kind);
            if (kind != 0) return kind;
            var at = a.At.CompareTo(b.At);
            return at != 0 ? at : a.Boss.Id.CompareTo(b.Boss.Id);
        });
        return new(alerts, next);
    }

    private static DateTimeOffset? Earliest(DateTimeOffset? a, DateTimeOffset b) => a is { } held && held <= b ? held : b;
}
