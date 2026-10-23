using System.Globalization;
using ScarletMeter.Engine;

namespace ScarletMeter.App.Formatting;

/// <summary>Formatting for the details and history pages. Culture-invariant numbers; dates in the PC's local time.</summary>
public static class DetailFormat
{
    /// <summary>A rate in percent, rounded, as RATmeter's tables show it ("37%").</summary>
    public static string Rate(double percent) => Math.Clamp(percent, 0, 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    /// <summary>A whole number with thousands separators ("1,234,567").</summary>
    public static string Full(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>A combat-log timestamp with milliseconds.</summary>
    public static string LogTime(DateTime time) => time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    /// <summary>A fight's date for lists ("Fri 24 Oct 21:05").</summary>
    public static string When(DateTime time) => time.ToString("ddd d MMM HH:mm", CultureInfo.InvariantCulture);

    /// <summary>The flags of a hit as short tags, in RATmeter's order.</summary>
    public static IEnumerable<string> Tags(HitFlags flags)
    {
        if (flags.HasFlag(HitFlags.Critical)) yield return "CRIT";
        if (flags.HasFlag(HitFlags.BackAttack)) yield return "BACK";
        if (flags.HasFlag(HitFlags.FrontAttack)) yield return "FRONT";
        if (flags.HasFlag(HitFlags.Perfect)) yield return "PERFECT";
        if (flags.HasFlag(HitFlags.DoubleDamage)) yield return "x2";
        if (flags.HasFlag(HitFlags.Parry)) yield return "PARRY";
    }

    /// <summary>The chart's data from a player's damage graph.</summary>
    public static ChartData Chart(DamageGraph graph) => new(
        graph.TotalSeconds,
        graph.Points.Select(p => new ChartPoint(p.Second, p.Damage, p.CumulativeDps)).ToList(),
        ChartLanes.Build(graph.Buffs, graph.TotalSeconds, b => b.BuffId, b => b.Name, b => b.Icon, b => b.StartSecond, b => b.EndSecond, b => b.Count));
}

/// <summary>
/// RATmeter's "Copy DPS" line for a fight (upstream AionDpsMeter.UI/Utils/CopyDpsFormatter.cs): the top ten
/// players by damage as "Name : DPS DPS (damage dmg)", joined with ", ". Names go through the same masking
/// the pages use, so hidden names stay hidden when pasted.
/// </summary>
public static class CopyDps
{
    public static string Format(IEnumerable<PlayerRow> players, Func<PlayerRow, string> name) =>
        string.Join(", ", players
            .OrderByDescending(p => p.TotalDamage)
            .Take(10)
            .Select(p => $"{name(p)} : {Compact.Number(p.Dps)} DPS ({Compact.Number(p.TotalDamage)} dmg)"));
}
