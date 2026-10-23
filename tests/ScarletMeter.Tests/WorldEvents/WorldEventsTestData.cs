using System.Globalization;
using ScarletMeter.WorldEvents;

namespace ScarletMeter.Tests;

/// <summary>Shared fixtures for the world-events tests: UTC instants written as ISO 8601, offsets and catalog rules.</summary>
internal static class WorldEventsTestData
{
    /// <summary>The Global Asia server clock the fork's tests assume, UTC+9.</summary>
    public static readonly TimeSpan Asia = TimeSpan.FromHours(9);

    /// <summary>An instant from an ISO 8601 string; strings in these tests end in Z.</summary>
    public static DateTimeOffset At(string iso) =>
        DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    /// <summary>A server offset in hours, fractional ones included.</summary>
    public static TimeSpan Offset(double hours) => TimeSpan.FromHours(hours);

    public static EventRule Legacy(string id) => EventCatalog.Legacy.Single(rule => rule.Id == id);

    public static EventRule Global(string id) => EventCatalog.Global.Single(rule => rule.Id == id);

    /// <summary>A copy of a schedule with one entry changed, as a settings file might hold it.</summary>
    public static List<EventRule> WithChange(IReadOnlyList<EventRule> rules, string id, Func<EventRule, EventRule> change) =>
        [.. rules.Select(rule => rule.Id == id ? change(rule) : rule)];
}
