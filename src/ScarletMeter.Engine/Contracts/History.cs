namespace ScarletMeter.Engine;

/// <summary>
/// A page of boss-fight history, newest first by end time. Times are local, as upstream records them.
/// <see cref="ICombatEngine.GetHistory"/> validates the query with <see cref="Validate"/>; callers can do the
/// same first to report bad input themselves.
/// </summary>
/// <param name="TargetNameContains">Case-insensitive part of the boss's name; blank means any boss.</param>
/// <param name="From">Only fights that ended at or after this time.</param>
/// <param name="To">Only fights that ended at or before this time (inclusive: pass the end of a day to include all of it).</param>
/// <param name="Page">The page to read, from 1. A page past the end is empty.</param>
/// <param name="PageSize">Fights per page, 1 to <see cref="MaxPageSize"/>.</param>
public sealed record HistoryQuery(
    string? TargetNameContains = null,
    DateTime? From = null,
    DateTime? To = null,
    int Page = 1,
    int PageSize = 50)
{
    /// <summary>The most fights one page holds.</summary>
    public const int MaxPageSize = 200;

    /// <summary>The longest name filter accepted, after trimming.</summary>
    public const int MaxNameLength = 100;

    /// <summary>
    /// Returns the query as the engine runs it: the name trimmed (blank becomes null) and UTC times converted to
    /// local time, the zone upstream stores; unspecified times are taken as local.
    /// </summary>
    /// <exception cref="ArgumentException">A page below 1, a page size outside 1 to 200, a name over 100
    /// characters, or <see cref="From"/> after <see cref="To"/>.</exception>
    public HistoryQuery Validate()
    {
        if (Page < 1)
            throw new ArgumentException($"History page must be 1 or more, not {Page}.", nameof(Page));
        if (PageSize is < 1 or > MaxPageSize)
            throw new ArgumentException($"History page size must be 1 to {MaxPageSize}, not {PageSize}.", nameof(PageSize));

        var name = string.IsNullOrWhiteSpace(TargetNameContains) ? null : TargetNameContains.Trim();
        if (name is { Length: > MaxNameLength })
            throw new ArgumentException(
                $"The boss name filter can be at most {MaxNameLength} characters, not {name.Length}.", nameof(TargetNameContains));

        var from = AsLocal(From);
        var to = AsLocal(To);
        if (from > to)
            throw new ArgumentException(
                $"The history range starts ({from:yyyy-MM-dd HH:mm:ss}) after it ends ({to:yyyy-MM-dd HH:mm:ss}).", nameof(From));

        return this with { TargetNameContains = name, From = from, To = to };
    }

    private static DateTime? AsLocal(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } utc => utc.ToLocalTime(),
        { } other => DateTime.SpecifyKind(other, DateTimeKind.Local),
    };
}

/// <summary>One page of history and how many fights match the query in all.</summary>
public sealed record HistoryPage(IReadOnlyList<HistoryEntry> Entries, int TotalCount, int Page, int PageSize);

/// <summary>
/// A boss fight in the history list. Upstream lists boss fights only; a fight still running is included with
/// <paramref name="Completed"/> false, ahead of the saved ones.
/// </summary>
/// <param name="FightId">Upstream's session id, the key for <see cref="ICombatEngine.GetHistoryFight"/>.</param>
/// <param name="Start">The fight's first hit, local time.</param>
/// <param name="End">The fight's last hit, local time.</param>
/// <param name="PartyDps">Total damage over <paramref name="Duration"/>, or 0 for a fight of no length.</param>
/// <param name="PlayerCount">Players the game named, or who dealt over one percent of the damage, as upstream counts them.</param>
public sealed record HistoryEntry(
    Guid FightId,
    string TargetName,
    long TargetHpTotal,
    DateTime Start,
    DateTime End,
    TimeSpan Duration,
    long TotalDamage,
    double PartyDps,
    int PlayerCount,
    bool Completed);

/// <summary>
/// A whole fight from history: every player's row and details, keyed by <see cref="PlayerRow.Id"/>. Players
/// the game had not named are included with <see cref="PlayerRow.IsIdentified"/> false, as in a snapshot.
/// </summary>
public sealed record HistoryFight(
    HistoryEntry Entry,
    IReadOnlyList<PlayerRow> Players,
    IReadOnlyDictionary<long, PlayerDetails> Details);
