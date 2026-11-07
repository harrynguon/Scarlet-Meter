using Serilog.Events;

namespace ScarletMeter.App.Logging;

/// <summary>
/// One kind of log event a <see cref="LogFloodGuard"/> limits: the events of one logger category whose message
/// template begins with a given text. A burst of them writes its first <see cref="Kept"/>; the rest are counted and
/// summarised every <see cref="Interval"/>, and the burst ends once an interval passes without one.
/// </summary>
internal sealed class LogFloodRule
{
    /// <summary>Serilog's property for the logger category (the type of an <c>ILogger&lt;T&gt;</c>).</summary>
    private const string CategoryProperty = "SourceContext";

    public LogFloodRule(string category, string prefix, int kept, TimeSpan interval)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentOutOfRangeException.ThrowIfNegative(kept);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        Category = category;
        Prefix = prefix;
        Kept = kept;
        Interval = interval;
    }

    /// <summary>The logger category the events come from, compared exactly.</summary>
    public string Category { get; }

    /// <summary>How the events' message template begins, compared exactly (case and all).</summary>
    public string Prefix { get; }

    /// <summary>How many events of a burst are written as they come before the rest are only counted.</summary>
    public int Kept { get; }

    /// <summary>How often a burst's counted events are summarised, and how long without one ends the burst.</summary>
    public TimeSpan Interval { get; }

    /// <summary>Whether <paramref name="logEvent"/> is one of this rule's events. Cheap, and safe on any thread.</summary>
    public bool Matches(LogEvent logEvent) =>
        logEvent.MessageTemplate.Text.StartsWith(Prefix, StringComparison.Ordinal)
        && logEvent.Properties.TryGetValue(CategoryProperty, out var category)
        && category is ScalarValue { Value: string name }
        && string.Equals(name, Category, StringComparison.Ordinal);
}
