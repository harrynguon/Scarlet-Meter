using System.Collections.Immutable;
using System.Text;

namespace ScarletMeter.WorldEvents;

/// <summary>What a schedule entry announces. The kind changes how a window is read (see <see cref="EventRule"/>).</summary>
public enum EventKind
{
    /// <summary>A scheduled boss spawn. The schedule cannot tell whether the boss is alive.</summary>
    Boss,

    /// <summary>A Spacetime Rift: a portal (entry) window, optionally followed by a longer activity window.</summary>
    Rift,

    /// <summary>A field event: artifact siege, dimensional invasion, Shugo Festival.</summary>
    Event,

    /// <summary>A daily or weekly reset: an instant. It never raises an Open alert.</summary>
    Reset,
}

/// <summary>The clock in which a rule's weekdays and hours are written.</summary>
public enum ScheduleBasis
{
    /// <summary>Coordinated Universal Time; the server offset does not move it.</summary>
    Utc,

    /// <summary>The game server's wall clock: UTC plus the fixed server offset the host supplies.</summary>
    Server,
}

/// <summary>
/// One recurring world event. An occurrence starts on each listed weekday (every day when <see cref="Days"/> is
/// null) at each listed hour and <see cref="Minute"/>, in the <see cref="Basis"/> clock. Its window lasts
/// <see cref="DurationMinutes"/>; <see cref="EntryMinutes"/> is the part in which players can still join (a
/// rift's defaults to its whole duration, the portal); <see cref="ActiveMinutes"/> lets the activity outlast the
/// duration (rifts only, once rules go through <see cref="EventCatalog.TryApplyOverrides"/>).
/// </summary>
/// <remarks>
/// Immutable. <see cref="Hours"/> and <see cref="Days"/> are copied, de-duplicated and sorted on construction and
/// by <c>with</c>, and compare by value, so a rule never shares a list with its caller. Ranges are deliberately
/// not enforced here: a host can build a rule from untrusted settings and ask
/// <see cref="ScheduleValidation.ValidateRules"/> what is wrong. The calculations reject a rule that fails it.
/// </remarks>
/// <param name="Id">Catalog id (see <see cref="EventIds"/>).</param>
/// <param name="Name">Display name; the catalog's always wins over a saved one.</param>
/// <param name="Location">Where the event happens; the catalog's always wins over a saved one.</param>
/// <param name="Kind">What the entry announces; the catalog's always wins over a saved one.</param>
/// <param name="Basis">The clock of <paramref name="Days"/> and <paramref name="Hours"/>.</param>
/// <param name="Days">Weekdays, 0 = Sunday to 6 = Saturday, or null for every day.</param>
/// <param name="Hours">Start hours, 0 to 23.</param>
/// <param name="Minute">Start minute within each hour, 0 to 59.</param>
/// <param name="DurationMinutes">Window length, 0 to 1440; 0 means the end is unknown or the event is an instant.</param>
/// <param name="EntryMinutes">Entry window from the start, at most the duration; null means none (rift: the duration).</param>
/// <param name="ActiveMinutes">Activity length from the start, from the duration to 1440; null means the duration.</param>
public sealed record EventRule(
    string Id,
    string Name,
    string Location,
    EventKind Kind,
    ScheduleBasis Basis,
    IReadOnlyList<int>? Days,
    IReadOnlyList<int> Hours,
    int Minute,
    int DurationMinutes,
    int? EntryMinutes = null,
    int? ActiveMinutes = null)
{
    private readonly IReadOnlyList<int>? days = Normalize(Days);
    private readonly IReadOnlyList<int> hours = Normalize(Hours) ?? ImmutableArray<int>.Empty;

    /// <summary>Weekdays in the rule's clock, distinct and ascending (0 = Sunday); null means every day.</summary>
    public IReadOnlyList<int>? Days
    {
        get => days;
        init => days = Normalize(value);
    }

    /// <summary>Start hours in the rule's clock, distinct and ascending. A null list (from untrusted input) becomes empty, which validation rejects.</summary>
    public IReadOnlyList<int> Hours
    {
        get => hours;
        init => hours = Normalize(value) ?? ImmutableArray<int>.Empty;
    }

    /// <summary>Value equality, comparing <see cref="Days"/> and <see cref="Hours"/> element by element.</summary>
    public bool Equals(EventRule? other) =>
        other is not null
        && string.Equals(Id, other.Id, StringComparison.Ordinal)
        && string.Equals(Name, other.Name, StringComparison.Ordinal)
        && string.Equals(Location, other.Location, StringComparison.Ordinal)
        && Kind == other.Kind
        && Basis == other.Basis
        && SameValues(days, other.days)
        && SameValues(hours, other.hours)
        && Minute == other.Minute
        && DurationMinutes == other.DurationMinutes
        && EntryMinutes == other.EntryMinutes
        && ActiveMinutes == other.ActiveMinutes;

    /// <summary>Hash consistent with <see cref="Equals(EventRule?)"/>.</summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(Kind);
        hash.Add(Basis);
        hash.Add(Minute);
        hash.Add(DurationMinutes);
        hash.Add(EntryMinutes);
        hash.Add(ActiveMinutes);
        hash.Add(days?.Count ?? -1);
        for (var i = 0; i < hours.Count; i++) hash.Add(hours[i]);
        return hash.ToHashCode();
    }

    // Prints the lists' values (the default would print their type name), so failing tests read clearly.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"Id = {Id}, Name = {Name}, Location = {Location}, Kind = {Kind}, Basis = {Basis}, ");
        builder.Append($"Days = {(days is null ? "every day" : $"[{string.Join(", ", days)}]")}, Hours = [{string.Join(", ", hours)}], ");
        builder.Append($"Minute = {Minute}, DurationMinutes = {DurationMinutes}, EntryMinutes = {EntryMinutes}, ActiveMinutes = {ActiveMinutes}");
        return true;
    }

    // An immutable, distinct, ascending copy, so the rule never aliases a caller's mutable list. It is boxed
    // once here, so reading the property never allocates.
    private static IReadOnlyList<int>? Normalize(IEnumerable<int>? values) =>
        values is null ? null : (IReadOnlyList<int>)ImmutableArray.CreateRange(values.Distinct().Order());

    // Element-wise equality of two normalised lists; null (every day) equals only null.
    private static bool SameValues(IReadOnlyList<int>? left, IReadOnlyList<int>? right)
    {
        if (left is null || right is null) return left is null && right is null;
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i]) return false;
        }

        return true;
    }
}
