using System.Diagnostics.CodeAnalysis;

namespace ScarletMeter.WorldEvents;

/// <summary>
/// The built-in schedule: nine entries, in display order, as two presets. <see cref="Global"/> is Scarlet's
/// default. Neither is verified against Global Asia / Nezekan; see docs/ARCHITECTURE.md ("World events") for the
/// sources and the evidence so far, and docs/SECURITY.md ("Provenance").
/// </summary>
public static class EventCatalog
{
    private static readonly IReadOnlyList<int> EveryHour = [.. Enumerable.Range(0, 24)];

    /// <summary>
    /// The fork's original defaults (its <c>defaults</c>), adapted from Byanski/aion2timeroverlay. Kept for
    /// comparison and for players who saved these rules; the boss, siege and 60-minute rift windows are estimates.
    /// </summary>
    public static IReadOnlyList<EventRule> Legacy { get; } =
    [
        new(EventIds.Kaira, "Watcher Kaira", "Abandoned Island · Chaotic Lower Reshanta", EventKind.Boss, ScheduleBasis.Utc,
            null, [1, 4, 7, 10, 13, 16, 19, 22], 0, 30),
        new(EventIds.Nahma, "Guardian Lord Nahma", "Reshanta’s Root", EventKind.Boss, ScheduleBasis.Server,
            [0, 5], [21], 0, 45),
        new(EventIds.Executors, "Executors · Tamasa & Argo", "Reshanta’s Root & Siel’s Heart", EventKind.Boss, ScheduleBasis.Server,
            [1, 4, 6], [21], 30, 30),
        new(EventIds.Rift, "Spacetime Rift", "Contested zones & enemy territory", EventKind.Rift, ScheduleBasis.Utc,
            null, [0, 3, 6, 9, 12, 15, 18, 21], 0, 10, ActiveMinutes: 60),
        new(EventIds.Siege, "Artifact Siege", "Reshanta artifact outposts", EventKind.Event, ScheduleBasis.Server,
            [1, 4, 6], [21], 0, 45),
        new(EventIds.Invasion, "Dimensional Invasion", "Borderlands", EventKind.Event, ScheduleBasis.Server,
            null, EveryHour, 30, 15),
        new(EventIds.Shugo, "Shugo Festival", "Shugo Pavilion · Jump Jump, Hidden Lugi, Goldrin", EventKind.Event, ScheduleBasis.Server,
            null, EveryHour, 0, 15),
        new(EventIds.Daily, "Daily reset", "Daily quests, missions & entries", EventKind.Reset, ScheduleBasis.Server,
            null, [9], 0, 0),
        new(EventIds.Weekly, "Weekly reset", "Wednesday · Abyss rank tally", EventKind.Reset, ScheduleBasis.Server,
            [3], [9], 0, 0),
    ];

    /// <summary>
    /// The fork's Global preset (its <c>globalSchedule()</c>): <see cref="Legacy"/> with the times and windows of
    /// taengu/A2Tools-Plugins' event-timers data. Resets at 07:00 UTC; Kaira and rifts on the server clock; rifts
    /// every three hours from 02:00 server with a ten-minute portal and no longer activity; Shugo and Invasion with
    /// a three-minute entry in a seven- and thirteen-minute window; boss and siege end times unknown (duration 0),
    /// so they are never shown as active.
    /// </summary>
    public static IReadOnlyList<EventRule> Global { get; } = [.. Legacy.Select(ToGlobal)];

    /// <summary>The preset a new installation starts from: <see cref="Global"/>.</summary>
    public static IReadOnlyList<EventRule> Default => Global;

    /// <summary>The nine ids in display order. A valid schedule lists each exactly once.</summary>
    public static IReadOnlyList<string> Ids { get; } = [.. Legacy.Select(rule => rule.Id)];

    /// <summary>The initial server offset, UTC+9: the Global Asia assumption the fork starts from, not verified.</summary>
    public static TimeSpan DefaultServerOffset { get; } = TimeSpan.FromHours(9);

    private static readonly Dictionary<string, EventRule> ById = Legacy.ToDictionary(rule => rule.Id, StringComparer.Ordinal);

    /// <summary>
    /// Validates saved rules (<see cref="ScheduleValidation.ValidateRules"/>) and, when they pass, returns them in
    /// catalog order with the catalog's id, name, location and kind, as the fork's <c>validateState</c> does: only
    /// the clock, days, hours, minute, duration and entry window are taken from the saved rule, plus the activity
    /// length for the rift (a saved activity length on any other entry is dropped). On failure the host decides
    /// whether to reject the file or repair the named entries, for example from <see cref="Default"/>.
    /// </summary>
    /// <param name="saved">Untrusted rules, for example from the host's settings file.</param>
    /// <param name="rules">The merged rules when the method returns true; otherwise null.</param>
    /// <param name="errors">Every problem found; empty when the method returns true.</param>
    public static bool TryApplyOverrides(
        IReadOnlyList<EventRule>? saved,
        [NotNullWhen(true)] out IReadOnlyList<EventRule>? rules,
        out IReadOnlyList<ScheduleError> errors)
    {
        errors = ScheduleValidation.ValidateRules(saved);
        if (errors.Count > 0 || saved is null)
        {
            rules = null;
            return false;
        }

        var savedById = saved.ToDictionary(rule => rule.Id, StringComparer.Ordinal);
        rules = [.. Legacy.Select(catalog => Merge(catalog, savedById[catalog.Id]))];
        return true;
    }

    // Catalog metadata with the saved rule's editable values (validation has passed, so the id is known).
    private static EventRule Merge(EventRule catalog, EventRule saved) => catalog with
    {
        Basis = saved.Basis,
        Days = saved.Days,
        Hours = saved.Hours,
        Minute = saved.Minute,
        DurationMinutes = saved.DurationMinutes,
        EntryMinutes = saved.EntryMinutes,
        ActiveMinutes = catalog.Kind == EventKind.Rift ? saved.ActiveMinutes : null,
    };

    // The catalog kind of a known id. Validation reads kinds from here, never from untrusted input.
    internal static bool TryGetKind(string? id, out EventKind kind)
    {
        if (id is not null && ById.TryGetValue(id, out var rule))
        {
            kind = rule.Kind;
            return true;
        }

        kind = default;
        return false;
    }

    // presets.mjs applied to one legacy rule; the A2Tools parameters at the pinned commit.
    private static EventRule ToGlobal(EventRule rule) => rule.Id switch
    {
        EventIds.Daily or EventIds.Weekly => rule with { Basis = ScheduleBasis.Utc, Hours = [7] },
        EventIds.Kaira => rule with { Basis = ScheduleBasis.Server, DurationMinutes = 0 },
        EventIds.Nahma or EventIds.Executors or EventIds.Siege => rule with { DurationMinutes = 0 },
        EventIds.Rift => rule with
        {
            Basis = ScheduleBasis.Server,
            Hours = [2, 5, 8, 11, 14, 17, 20, 23],
            DurationMinutes = 10,
            ActiveMinutes = 10,
        },
        EventIds.Shugo => rule with { DurationMinutes = 7, EntryMinutes = 3 },
        EventIds.Invasion => rule with { DurationMinutes = 13, EntryMinutes = 3 },
        _ => rule,
    };
}
