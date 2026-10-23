namespace ScarletMeter.App.Formatting;

/// <summary>What <c>DamageChart</c> draws: points over the fight and one lane per buff.</summary>
public sealed record ChartData(double TotalSeconds, IReadOnlyList<ChartPoint> Points, IReadOnlyList<ChartLane> Lanes);

/// <summary>One second of the fight: damage that second (smoothed upstream) and cumulative DPS so far.</summary>
public sealed record ChartPoint(double Second, double PerSecond, double Cumulative);

/// <summary>A buff's lane: merged, non-overlapping active spans in seconds, application count and uptime.</summary>
public sealed record ChartLane(string Name, string? Icon, IReadOnlyList<(double Start, double End)> Spans, int Count, double UptimePercent);

/// <summary>Builds lanes from individual buff spans: grouped per buff, overlaps merged, longest uptime first.</summary>
public static class ChartLanes
{
    public static IReadOnlyList<ChartLane> Build<T>(
        IEnumerable<T> spans, double totalSeconds,
        Func<T, int> id, Func<T, string> name, Func<T, string?> icon, Func<T, double> start, Func<T, double> end, Func<T, int> count)
    {
        var lanes = new List<ChartLane>();
        foreach (var group in spans.GroupBy(id))
        {
            var merged = new List<(double Start, double End)>();
            foreach (var span in group.Select(s => (Start: Math.Max(0, start(s)), End: Math.Min(totalSeconds, end(s))))
                         .Where(s => s.End > s.Start).OrderBy(s => s.Start))
            {
                if (merged.Count > 0 && span.Start <= merged[^1].End)
                    merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, span.End));
                else
                    merged.Add(span);
            }
            if (merged.Count == 0) continue;
            var first = group.First();
            var uptime = totalSeconds > 0 ? merged.Sum(s => s.End - s.Start) / totalSeconds * 100 : 0;
            lanes.Add(new ChartLane(name(first), icon(first), merged, group.Sum(count), Math.Clamp(uptime, 0, 100)));
        }
        return lanes.OrderByDescending(l => l.UptimePercent).ThenBy(l => l.Name, StringComparer.Ordinal).ToList();
    }
}
