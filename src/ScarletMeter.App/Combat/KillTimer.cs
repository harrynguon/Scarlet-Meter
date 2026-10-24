using ScarletMeter.Engine;

namespace ScarletMeter.App.Combat;

/// <summary>
/// Estimates how long the current target has left ("Kill in") from how fast its HP has fallen over the last
/// <see cref="Window"/>: everyone's damage counts, the party's or not, and a burst or a pause moves the estimate
/// only as much as it moves that window. Fed once per overlay refresh. No estimate until the HP has been tracked
/// for <see cref="MinimumSpan"/>, while it is not falling, after its HP rises (a reset or a heal starts that
/// target over), or when the answer would be longer than <see cref="Longest"/>. Upstream's current target is the
/// one the player hit last, so it flips between a boss and its adds: the last <see cref="RememberedTargets"/>
/// targets keep their history, and switching back to the boss resumes its estimate, from samples still within the
/// window (a target unseen for longer starts over, its old samples trimmed away). A clock that goes back starts
/// everything over. Not thread-safe: the overlay calls it from its refresh loop only.
/// </summary>
public sealed class KillTimer
{
    /// <summary>How far back the HP's fall is measured.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(20);

    /// <summary>How long the HP must have been tracked before an estimate shows.</summary>
    public static readonly TimeSpan MinimumSpan = TimeSpan.FromSeconds(5);

    /// <summary>Estimates beyond this say nothing useful and are not shown.</summary>
    public static readonly TimeSpan Longest = TimeSpan.FromHours(1);

    /// <summary>How many recent targets keep their history at once.</summary>
    public const int RememberedTargets = 4;

    // One target's HP samples, oldest first, and when it was last seen.
    private sealed class History
    {
        public Queue<(DateTimeOffset At, long Hp)> Samples { get; } = new();

        public long LastHp { get; set; }

        public DateTimeOffset LastAt { get; set; }
    }

    private readonly Dictionary<int, History> histories = new();
    private DateTimeOffset? lastNow;

    /// <summary>Records the target's HP at <paramref name="now"/> and returns the estimate, or null.</summary>
    public TimeSpan? Update(TargetInfo? target, DateTimeOffset now)
    {
        if (lastNow is { } previous && now < previous) histories.Clear();
        lastNow = now;
        if (target is null || target.HpTotal <= 0) return null;

        if (!histories.TryGetValue(target.Id, out var history) || target.HpCurrent > history.LastHp)
        {
            history = new History();
            histories[target.Id] = history;
            if (histories.Count > RememberedTargets)
                histories.Remove(histories.Where(h => h.Key != target.Id).MinBy(h => h.Value.LastAt).Key);
        }
        history.LastHp = target.HpCurrent;
        history.LastAt = now;
        var samples = history.Samples;
        samples.Enqueue((now, target.HpCurrent));
        while (samples.Count > 1 && now - samples.Peek().At > Window) samples.Dequeue();

        if (target.HpCurrent <= 0) return null;
        var (firstAt, firstHp) = samples.Peek();
        var span = now - firstAt;
        var lost = firstHp - target.HpCurrent;
        if (span < MinimumSpan || lost <= 0) return null;
        var seconds = target.HpCurrent / (lost / span.TotalSeconds);
        return seconds <= Longest.TotalSeconds ? TimeSpan.FromSeconds(Math.Ceiling(seconds)) : null;
    }

    /// <summary>Forgets every target, as when the player drops the fight.</summary>
    public void Clear()
    {
        histories.Clear();
        lastNow = null;
    }
}
