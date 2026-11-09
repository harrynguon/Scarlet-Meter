using ScarletMeter.WorldEvents;
using static ScarletMeter.Tests.WorldEventsTestData;

namespace ScarletMeter.Tests;

/// <summary>The built-in rules match the fork's schedule.mjs and presets.mjs, and rules are immutable values.</summary>
public sealed class EventCatalogTests
{
    // Scarlet's order and one name differ from the fork's, both the player's choice for the overlay's event row (Harry,
    // 2026-10-26): the festivals and the rift first, then the siege, the bosses and the resets (the fork starts with
    // Kaira); and the siege is the Abyss Siege (the fork's Artifact Siege).
    [Fact]
    public void TheCatalogHasNineEntriesInScarletsOrderAndDefaultsToGlobal()
    {
        Assert.Equal(
            [EventIds.Shugo, EventIds.Invasion, EventIds.Rift, EventIds.Siege, EventIds.Kaira, EventIds.Nahma, EventIds.Executors, EventIds.Daily, EventIds.Weekly],
            EventCatalog.Ids);
        Assert.Equal(EventCatalog.Ids, EventCatalog.Legacy.Select(rule => rule.Id));
        Assert.Equal(EventCatalog.Ids, EventCatalog.Global.Select(rule => rule.Id));
        Assert.Same(EventCatalog.Global, EventCatalog.Default);
        Assert.Equal(TimeSpan.FromHours(9), EventCatalog.DefaultServerOffset);
    }

    /// <summary>
    /// presets.mjs at the fork's master, value for value (A2Tools event-timers at b11f345), listed in the fork's order;
    /// Scarlet's catalog differs only in its order, the siege's name (above), and the siege's and the Executors' days since
    /// the game's update of 2026-11-09 (below).
    /// </summary>
    [Fact]
    public void GlobalPresetMatchesTheFork()
    {
        IReadOnlyList<int> everyHour = [.. Enumerable.Range(0, 24)];
        var expected = new EventRule[]
        {
            new(EventIds.Kaira, "Watcher Kaira", "Abandoned Island · Chaotic Lower Reshanta", EventKind.Boss, ScheduleBasis.Server, null, [1, 4, 7, 10, 13, 16, 19, 22], 0, 0),
            new(EventIds.Nahma, "Guardian Lord Nahma", "Reshanta’s Root", EventKind.Boss, ScheduleBasis.Server, [0, 5], [21], 0, 0),
            new(EventIds.Executors, "Executors · Tamasa & Argo", "Reshanta’s Root & Siel’s Heart", EventKind.Boss, ScheduleBasis.Server, [1, 4, 6], [21], 30, 0),
            new(EventIds.Rift, "Spacetime Rift", "Contested zones & enemy territory", EventKind.Rift, ScheduleBasis.Server, null, [2, 5, 8, 11, 14, 17, 20, 23], 0, 10, null, 10),
            new(EventIds.Siege, "Artifact Siege", "Reshanta artifact outposts", EventKind.Event, ScheduleBasis.Server, [1, 4, 6], [21], 0, 0),
            new(EventIds.Invasion, "Dimensional Invasion", "Borderlands", EventKind.Event, ScheduleBasis.Server, null, everyHour, 30, 13, 3),
            new(EventIds.Shugo, "Shugo Festival", "Shugo Pavilion · Jump Jump, Hidden Lugi, Goldrin", EventKind.Event, ScheduleBasis.Server, null, everyHour, 0, 7, 3),
            new(EventIds.Daily, "Daily reset", "Daily quests, missions & entries", EventKind.Reset, ScheduleBasis.Utc, null, [7], 0, 0),
            new(EventIds.Weekly, "Weekly reset", "Wednesday · Abyss rank tally", EventKind.Reset, ScheduleBasis.Utc, [3], [7], 0, 0),
        };
        var order = EventCatalog.Ids.ToList();
        var scarlet = expected
            .Select(rule => rule.Id == EventIds.Siege ? rule with { Name = "Abyss Siege" } : rule)
            .Select(rule => rule.Id is EventIds.Siege or EventIds.Executors ? rule with { Days = [4, 6] } : rule)
            .OrderBy(rule => order.IndexOf(rule.Id));
        Assert.Equal(scarlet, EventCatalog.Global);
    }

    // The game's update of 2026-11-09 (its patch notes, Harry, 2026-11-09): the Artifact Siege from three times a week to
    // Thursdays and Saturdays at 21:00 as before, and the three Artifact Bosses (Executors Kaira, Argo and Tamasa) with it,
    // at 21:30 as before. Harry's game's countdown to the next siege, read after the update, ended at Thursday's 21:00
    // server time on the preset's UTC+9. The Legacy preset keeps the fork's three days, for comparison.
    [Fact]
    public void TheSiegeAndTheExecutorsAreOnThursdaysAndSaturdays()
    {
        foreach (var (id, minute) in new[] { (EventIds.Siege, 0), (EventIds.Executors, 30) })
        {
            Assert.Equal([4, 6], Global(id).Days);
            Assert.Equal([21], Global(id).Hours);
            Assert.Equal(minute, Global(id).Minute);
        }
        Assert.Equal([1, 4, 6], Legacy(EventIds.Siege).Days);
        Assert.Equal([1, 4, 6], Legacy(EventIds.Executors).Days);

        // From a Monday after the update, the next siege is Thursday's, 12:00 UTC, not that evening's.
        var monday = new DateTimeOffset(2026, 11, 16, 3, 0, 0, TimeSpan.Zero);
        var next = EventSchedule.Occurrence(Global(EventIds.Siege), monday, EventCatalog.DefaultServerOffset).Start;
        Assert.Equal(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void LegacyDefaultsMatchTheFork()
    {
        Assert.Equal((ScheduleBasis.Utc, 30), (Legacy(EventIds.Kaira).Basis, Legacy(EventIds.Kaira).DurationMinutes));
        Assert.Equal((ScheduleBasis.Utc, 10, (int?)60), (Legacy(EventIds.Rift).Basis, Legacy(EventIds.Rift).DurationMinutes, Legacy(EventIds.Rift).ActiveMinutes));
        Assert.Equal([0, 3, 6, 9, 12, 15, 18, 21], Legacy(EventIds.Rift).Hours);
        Assert.Equal((45, 30, 45), (Legacy(EventIds.Nahma).DurationMinutes, Legacy(EventIds.Executors).DurationMinutes, Legacy(EventIds.Siege).DurationMinutes));
        Assert.Equal((15, (int?)null, 15, (int?)null), (Legacy(EventIds.Shugo).DurationMinutes, Legacy(EventIds.Shugo).EntryMinutes, Legacy(EventIds.Invasion).DurationMinutes, Legacy(EventIds.Invasion).EntryMinutes));
        Assert.All(new[] { Legacy(EventIds.Daily), Legacy(EventIds.Weekly) }, reset => Assert.Equal((ScheduleBasis.Server, 9), (reset.Basis, reset.Hours.Single())));
    }

    [Fact]
    public void ThePresetsCannotBeChangedThroughTheirLists()
    {
        Assert.Throws<NotSupportedException>(() => ((IList<EventRule>)EventCatalog.Global)[0] = Legacy(EventIds.Kaira));
        Assert.Throws<NotSupportedException>(() => ((IList<int>)Global(EventIds.Daily).Hours)[0] = 4);
        Assert.Throws<NotSupportedException>(() => ((ICollection<int>)Global(EventIds.Daily).Hours).Add(4));
        Assert.Equal([7], Global(EventIds.Daily).Hours);
    }

    [Fact]
    public void RulesCopySortAndDeduplicateTheirLists()
    {
        var hours = new List<int> { 4, 1, 1 };
        var days = new List<int> { 5, 0, 5 };
        var rule = new EventRule("x", "X", "Somewhere", EventKind.Event, ScheduleBasis.Utc, days, hours, 0, 10);
        hours.Add(23);
        days.Clear();

        Assert.Equal([1, 4], rule.Hours);
        Assert.Equal([0, 5], rule.Days!);
        Assert.Equal([2, 5], (rule with { Hours = [5, 2, 5] }).Hours);
        Assert.Null((rule with { Days = null }).Days);
        Assert.Empty((rule with { Days = [] }).Days!);
        Assert.Empty((rule with { Hours = null! }).Hours); // untrusted null: empty, which validation rejects
    }

    [Fact]
    public void RulesCompareByValue()
    {
        var first = new EventRule("x", "X", "Somewhere", EventKind.Event, ScheduleBasis.Utc, [1], [3, 2], 0, 10);
        var second = new EventRule("x", "X", "Somewhere", EventKind.Event, ScheduleBasis.Utc, new List<int> { 1 }, new[] { 2, 3 }, 0, 10);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, second with { Hours = [2] });
        Assert.NotEqual(first, second with { Days = null });
        Assert.NotEqual(first, second with { EntryMinutes = 1 });
        Assert.Contains("Hours = [2, 3]", first.ToString(), StringComparison.Ordinal);
    }
}
