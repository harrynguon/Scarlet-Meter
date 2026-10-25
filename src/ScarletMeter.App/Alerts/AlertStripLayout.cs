using ScarletMeter.App.Combat;
using ScarletMeter.WorldEvents;

namespace ScarletMeter.App.Alerts;

/// <summary>
/// What the overlay's alert strip (Components/AlertStrip.razor) shows: closed, one bar with the most urgent notice
/// and how many more are due; open, every due notice under it; the bar's hover text; and whether the strip stands
/// aside for a boss fight. Pure, so the component stays a view and the rules are tested without rendering.
/// </summary>
public static class AlertStripLayout
{
    /// <summary>Notices the screen-reader summary names, most urgent first.</summary>
    public const int AnnouncedCount = 2;

    /// <summary>
    /// Whether the strip stands aside for the fight under way: with "Hide during boss fights" on, while
    /// <see cref="BossFightHold"/> says a boss fight is under way (adds included, not once the boss is down, gone or
    /// long still).
    /// </summary>
    public static bool HiddenForFight(bool hideDuringBossFights, bool bossFightUnderWay) =>
        hideDuringBossFights && bossFightUnderWay;

    /// <summary>
    /// The notice rows under the bar, most urgent first: every one while the strip is open, none while it is closed
    /// (the bar itself shows the most urgent).
    /// </summary>
    public static IEnumerable<EventAlert> Shown(IReadOnlyList<EventAlert> alerts, bool expanded) =>
        expanded ? alerts : [];

    /// <summary>How many more notices are due than the closed bar names ("+2"); 0 when it names the only one.</summary>
    public static int MoreCount(IReadOnlyList<EventAlert> alerts) => Math.Max(0, alerts.Count - 1);

    /// <summary>
    /// The closed bar's tooltip: one line per due notice, as <see cref="Describe"/> words it. Clock times rather than
    /// countdowns, so it does not go stale while the tooltip is open.
    /// </summary>
    public static string List(IReadOnlyList<EventAlert> alerts) => string.Join("\n", alerts.Select(Describe));

    /// <summary>The open bar's tooltip.</summary>
    public static string CollapseHint => "Fold the world events into one line";

    /// <summary>A notice in one line, in local time: "&lt;name&gt; · &lt;location&gt; · starts 21:00" (or "closes" while open).</summary>
    public static string Describe(EventAlert alert) =>
        $"{alert.Rule.Name} · {alert.Rule.Location} · {(alert.Kind == AlertKind.Soon ? "starts" : "closes")} {EventText.LocalTime(alert.Deadline)}";
}
