using ScarletMeter.WorldEvents;

namespace ScarletMeter.App.Alerts;

/// <summary>
/// What the overlay's alert strip (Components/AlertStrip.razor) shows: the first <see cref="CollapsedCount"/>
/// notices, or every due one once the player expands it, and the hover text listing the ones it hides. Pure, so
/// the component stays a view and the rules are tested without rendering.
/// </summary>
public static class AlertStripLayout
{
    /// <summary>Notices the strip shows before it is expanded.</summary>
    public const int CollapsedCount = 2;

    /// <summary>The notices to draw, most urgent first: all of them when expanded, else the first few.</summary>
    public static IEnumerable<EventAlert> Shown(IReadOnlyList<EventAlert> alerts, bool expanded) =>
        expanded ? alerts : alerts.Take(CollapsedCount);

    /// <summary>How many notices the collapsed strip hides; 0 when they all fit, and then there is nothing to expand.</summary>
    public static int HiddenCount(IReadOnlyList<EventAlert> alerts) => Math.Max(0, alerts.Count - CollapsedCount);

    /// <summary>
    /// Whether an expanded strip stays expanded: only while some notices would otherwise be hidden, so once the
    /// extra events have passed it starts collapsed again the next time more are due.
    /// </summary>
    public static bool StaysExpanded(bool expanded, IReadOnlyList<EventAlert> alerts) => expanded && HiddenCount(alerts) > 0;

    /// <summary>
    /// The "+N more" tooltip: one line per hidden notice, as <see cref="Describe"/> words it. Clock times rather
    /// than countdowns, so it does not go stale while the tooltip is open.
    /// </summary>
    public static string HiddenSummary(IReadOnlyList<EventAlert> alerts) =>
        string.Join("\n", alerts.Skip(CollapsedCount).Select(Describe));

    /// <summary>The expanded strip's "Show less" tooltip.</summary>
    public static string CollapseHint => $"Show only the first {CollapsedCount}";

    /// <summary>A notice in one line, in local time: "&lt;name&gt; · &lt;location&gt; · starts 21:00" (or "closes" while open).</summary>
    public static string Describe(EventAlert alert) =>
        $"{alert.Rule.Name} · {alert.Rule.Location} · {(alert.Kind == AlertKind.Soon ? "starts" : "closes")} {EventText.LocalTime(alert.Deadline)}";
}
