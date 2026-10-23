using ScarletMeter.WorldEvents;
using static ScarletMeter.Tests.WorldEventsTestData;

namespace ScarletMeter.Tests;

/// <summary>
/// Validation and override merging, ported from the fork's validateEvents cases (tests/companion.test.mjs) and
/// validateState cases (tests/companion-state.test.mjs). Its file-version checks belong to the host's settings
/// format, and its non-finite or string values cannot be represented here, so neither is ported.
/// </summary>
public sealed class ScheduleValidationTests
{
    // One bad value each on Kaira (legacy: 30-minute window) and the problem it must report.
    private static readonly Dictionary<string, (Func<EventRule, EventRule> Change, string Message)> KairaChanges = new()
    {
        ["no hours"] = (rule => rule with { Hours = [] }, "Hours must be one or more of 0–23."),
        ["hour 24"] = (rule => rule with { Hours = [1, 24] }, "Hours must be one or more of 0–23."),
        ["hour -1"] = (rule => rule with { Hours = [-1, 4] }, "Hours must be one or more of 0–23."),
        ["minute 60"] = (rule => rule with { Minute = 60 }, "Minutes must be 0–59."),
        ["minute -1"] = (rule => rule with { Minute = -1 }, "Minutes must be 0–59."),
        ["day 7"] = (rule => rule with { Days = [7] }, "Days must be one or more of 0–6 (Sunday–Saturday), or every day."),
        ["day -1"] = (rule => rule with { Days = [-1, 3] }, "Days must be one or more of 0–6 (Sunday–Saturday), or every day."),
        ["no days"] = (rule => rule with { Days = [] }, "Days must be one or more of 0–6 (Sunday–Saturday), or every day."),
        ["duration -1"] = (rule => rule with { DurationMinutes = -1 }, "Duration must be 0–1440 minutes."),
        ["duration 1441"] = (rule => rule with { DurationMinutes = 1441 }, "Duration must be 0–1440 minutes."),
        ["undefined basis"] = (rule => rule with { Basis = (ScheduleBasis)2 }, "Choose UTC or server time."),
        ["entry -1"] = (rule => rule with { EntryMinutes = -1 }, "Entry minutes must be between zero and the duration."),
        ["entry over duration"] = (rule => rule with { EntryMinutes = 31 }, "Entry minutes must be between zero and the duration."),
        ["active under duration"] = (rule => rule with { ActiveMinutes = 29 }, "Active minutes must be at least the duration, at most 1440."),
        ["active 1441"] = (rule => rule with { ActiveMinutes = 1441 }, "Active minutes must be at least the duration, at most 1440."),
    };

    public static TheoryData<string> KairaChangeNames => [.. KairaChanges.Keys];

    [Fact]
    public void BuiltInPresetsAreValid()
    {
        Assert.Empty(ScheduleValidation.ValidateRules(EventCatalog.Legacy));
        Assert.Empty(ScheduleValidation.ValidateRules(EventCatalog.Global));
    }

    [Theory]
    [MemberData(nameof(KairaChangeNames))]
    public void InvalidValuesAreReportedAgainstTheirEntry(string change)
    {
        var (apply, message) = KairaChanges[change];
        var errors = ScheduleValidation.ValidateRules(WithChange(EventCatalog.Legacy, EventIds.Kaira, apply));
        Assert.Equal([new ScheduleError(EventIds.Kaira, message)], errors);
    }

    [Fact]
    public void BoundaryValuesAreAccepted()
    {
        foreach (var change in new Func<EventRule, EventRule>[]
        {
            rule => rule with { Hours = [0, 23], Minute = 59, Days = [0, 6] },
            rule => rule with { Minute = 0, DurationMinutes = 0, EntryMinutes = 0, ActiveMinutes = 0 },
            rule => rule with { DurationMinutes = 1440, EntryMinutes = 1440, ActiveMinutes = 1440 },
            rule => rule with { Hours = [4, 1, 1, 23], Days = [5, 0, 0] }, // repeats and order are normalised
        })
        {
            Assert.Empty(ScheduleValidation.ValidateRules(WithChange(EventCatalog.Global, EventIds.Kaira, change)));
        }
    }

    [Fact]
    public void TheRiftNeedsAnActivityLengthOfAtLeastItsPortal()
    {
        Assert.Equal(
            [new ScheduleError(EventIds.Rift, "Active minutes must be at least the duration, at most 1440.")],
            ScheduleValidation.ValidateRules(WithChange(EventCatalog.Legacy, EventIds.Rift, rule => rule with { ActiveMinutes = 1 })));
        Assert.Equal(
            [new ScheduleError(EventIds.Rift, "The rift's active minutes are missing.")],
            ScheduleValidation.ValidateRules(WithChange(EventCatalog.Global, EventIds.Rift, rule => rule with { ActiveMinutes = null })));

        // The requirement follows the catalog's kind, not a kind claimed by the saved entry.
        Assert.Single(ScheduleValidation.ValidateRules(
            WithChange(EventCatalog.Global, EventIds.Rift, rule => rule with { Kind = EventKind.Reset, ActiveMinutes = null })));
    }

    [Fact]
    public void GlobalEntryWindowsCannotExceedTheirDuration()
    {
        foreach (var entry in new[] { -1, 8, int.MaxValue, int.MinValue })
        {
            Assert.NotEmpty(ScheduleValidation.ValidateRules(WithChange(EventCatalog.Global, EventIds.Shugo, rule => rule with { EntryMinutes = entry })));
        }
    }

    [Fact]
    public void EveryProblemIsReportedNotOnlyTheFirst()
    {
        var errors = ScheduleValidation.ValidateRules(WithChange(EventCatalog.Global, EventIds.Shugo,
            rule => rule with { Hours = [], Minute = 99, EntryMinutes = 9 }));
        Assert.Equal(3, errors.Count);
        Assert.All(errors, error => Assert.Equal(EventIds.Shugo, error.RuleId));
    }

    [Fact]
    public void TheListMustHoldEachCatalogEntryExactlyOnce()
    {
        Assert.Equal([new ScheduleError(null, "The schedule has no entries.")], ScheduleValidation.ValidateRules(null));
        Assert.Equal(
            [new ScheduleError(EventIds.Weekly, "The schedule is missing this entry.")],
            ScheduleValidation.ValidateRules(EventCatalog.Global.Take(8).ToList()));
        Assert.Equal(
            [new ScheduleError(EventIds.Rift, "The schedule lists this entry more than once.")],
            ScheduleValidation.ValidateRules([.. EventCatalog.Global, Global(EventIds.Rift)]));
        Assert.Equal(
            [new ScheduleError(null, "A schedule entry is empty.")],
            ScheduleValidation.ValidateRules([.. EventCatalog.Global, null!]));
    }

    [Fact]
    public void HostileInputGetsABoundedAnswer()
    {
        var tooMany = new ScheduleError(null, "The schedule has too many entries.");
        Assert.Equal(2 * EventCatalog.Ids.Count, ScheduleValidation.MaximumEntries);
        Assert.Equal([tooMany], ScheduleValidation.ValidateRules(Enumerable.Repeat(Global(EventIds.Shugo), 1_000_000).ToList()));
        Assert.Equal([tooMany], ScheduleValidation.ValidateRules([.. EventCatalog.Global, .. EventCatalog.Global, Global(EventIds.Rift)]));

        // Up to the limit every entry is examined: nine duplicates.
        Assert.Equal(9, ScheduleValidation.ValidateRules([.. EventCatalog.Global, .. EventCatalog.Global]).Count);

        // Every value wrong on every entry is 63 problems, cut to the limit with a final note.
        var broken = EventCatalog.Global.Select(rule => rule with
        {
            Basis = (ScheduleBasis)9, Hours = [], Days = [7], Minute = 60, DurationMinutes = -1, EntryMinutes = -1, ActiveMinutes = 2000,
        }).ToList();
        var errors = ScheduleValidation.ValidateRules(broken);
        Assert.Equal(ScheduleValidation.MaximumReportedErrors, errors.Count);
        Assert.Equal(new ScheduleError(null, "The schedule has more problems than are listed."), errors[^1]);
        Assert.All(errors.Take(errors.Count - 1), error => Assert.NotNull(error.RuleId));
    }

    [Fact]
    public void AnUnknownIdIsReportedWithoutEchoingIt()
    {
        const string untrusted = "<script>alert(1)</script>";
        var errors = ScheduleValidation.ValidateRules(WithChange(EventCatalog.Global, EventIds.Daily, rule => rule with { Id = untrusted }));
        Assert.Equal(
            [new ScheduleError(null, "A schedule entry has an unknown id."), new ScheduleError(EventIds.Daily, "The schedule is missing this entry.")],
            errors);
        Assert.DoesNotContain(errors, error => error.Message.Contains("script", StringComparison.Ordinal));
        Assert.Single(ScheduleValidation.ValidateRules(WithChange(EventCatalog.Global, EventIds.Daily, rule => rule with { Id = null! })), error => error.RuleId is null);
    }

    [Theory]
    [InlineData(-12)]
    [InlineData(-3.5)]
    [InlineData(0)]
    [InlineData(5.5)]
    [InlineData(5.75)]
    [InlineData(9)]
    [InlineData(12.75)]
    [InlineData(14)]
    public void ServerOffsetsFromMinusTwelveToPlusFourteenAreAccepted(double hours) =>
        Assert.Empty(ScheduleValidation.ValidateServerOffset(Offset(hours)));

    [Fact]
    public void ServerOffsetsOutsideTheRangeOrBetweenMinutesAreRejected()
    {
        foreach (var offset in new[] { Offset(-13), Offset(15), Offset(-12) - TimeSpan.FromMinutes(1), Offset(14) + TimeSpan.FromMinutes(1), TimeSpan.MaxValue })
        {
            Assert.Equal([new ScheduleError(null, "Server UTC offset must be −12 to +14.")], ScheduleValidation.ValidateServerOffset(offset));
        }

        foreach (var offset in new[] { new TimeSpan(5, 30, 30), TimeSpan.FromTicks(1) })
        {
            Assert.Equal([new ScheduleError(null, "Server UTC offset must be a whole number of minutes.")], ScheduleValidation.ValidateServerOffset(offset));
        }
    }

    [Fact]
    public void ThePresetsSurviveMergingUnchanged()
    {
        Assert.True(EventCatalog.TryApplyOverrides(EventCatalog.Global, out var global, out var errors));
        Assert.Empty(errors);
        Assert.Equal(EventCatalog.Global, global);

        Assert.True(EventCatalog.TryApplyOverrides(EventCatalog.Legacy, out var legacy, out _));
        Assert.Equal(EventCatalog.Legacy, legacy);
    }

    [Fact]
    public void CustomScheduleValuesAreKept()
    {
        // Every editable field differs from the catalog on some entry, including days removed (Nahma) and added (Kaira).
        var saved = WithChange(EventCatalog.Legacy, EventIds.Daily, rule => rule with { Hours = [18] });
        saved = WithChange(saved, EventIds.Kaira, rule => rule with
        {
            Basis = ScheduleBasis.Server, Days = [2, 4], Hours = [5], Minute = 15, DurationMinutes = 20, EntryMinutes = 5,
        });
        saved = WithChange(saved, EventIds.Nahma, rule => rule with { Basis = ScheduleBasis.Utc, Days = null, Minute = 45 });
        saved = WithChange(saved, EventIds.Rift, rule => rule with
        {
            Basis = ScheduleBasis.Server, Days = [1], Minute = 5, DurationMinutes = 12, EntryMinutes = 8, ActiveMinutes = 45,
        });

        Assert.True(EventCatalog.TryApplyOverrides(saved, out var rules, out _));
        Assert.Equal(saved, rules);
        var byId = rules.ToDictionary(rule => rule.Id);
        Assert.Equal([18], byId[EventIds.Daily].Hours);
        var kaira = byId[EventIds.Kaira];
        Assert.Equal((ScheduleBasis.Server, 15, 20, (int?)5), (kaira.Basis, kaira.Minute, kaira.DurationMinutes, kaira.EntryMinutes));
        Assert.Equal([2, 4], kaira.Days!);
        Assert.Equal([5], kaira.Hours);
        Assert.Equal((ScheduleBasis.Utc, (IReadOnlyList<int>?)null, 45), (byId[EventIds.Nahma].Basis, byId[EventIds.Nahma].Days, byId[EventIds.Nahma].Minute));
        var rift = byId[EventIds.Rift];
        Assert.Equal((ScheduleBasis.Server, 5, 12, (int?)8, (int?)45), (rift.Basis, rift.Minute, rift.DurationMinutes, rift.EntryMinutes, rift.ActiveMinutes));
        Assert.Equal([1], rift.Days!);
    }

    [Fact]
    public void SavedValuesCannotChangeAnEntrysIdentityOrMetadata()
    {
        var saved = WithChange(EventCatalog.Global, EventIds.Kaira,
            rule => rule with { Name = "arbitrary", Location = "anywhere", Kind = EventKind.Reset, Hours = [4, 1, 1] });
        saved.Reverse();

        Assert.True(EventCatalog.TryApplyOverrides(saved, out var rules, out _));
        Assert.Equal(EventCatalog.Ids, rules.Select(rule => rule.Id)); // catalog order, whatever the file's order
        var kaira = rules[0];
        Assert.Equal(("Watcher Kaira", "Abandoned Island · Chaotic Lower Reshanta", EventKind.Boss), (kaira.Name, kaira.Location, kaira.Kind));
        Assert.Equal([1, 4], kaira.Hours);
    }

    [Fact]
    public void OnlyTheRiftKeepsASavedActivityLength()
    {
        var saved = WithChange(EventCatalog.Global, EventIds.Shugo, rule => rule with { ActiveMinutes = 20 });
        saved = WithChange(saved, EventIds.Rift, rule => rule with { ActiveMinutes = 60 });
        Assert.True(EventCatalog.TryApplyOverrides(saved, out var rules, out _));
        Assert.Null(rules.Single(rule => rule.Id == EventIds.Shugo).ActiveMinutes);
        Assert.Equal(60, rules.Single(rule => rule.Id == EventIds.Rift).ActiveMinutes);
    }

    [Fact]
    public void InvalidOverridesAreRejectedWithTheirErrors()
    {
        Assert.False(EventCatalog.TryApplyOverrides(null, out var rules, out var errors));
        Assert.Null(rules);
        Assert.NotEmpty(errors);

        Assert.False(EventCatalog.TryApplyOverrides(
            WithChange(EventCatalog.Global, EventIds.Rift, rule => rule with { ActiveMinutes = null }), out rules, out errors));
        Assert.Null(rules);
        Assert.Equal(EventIds.Rift, Assert.Single(errors).RuleId);
    }
}
