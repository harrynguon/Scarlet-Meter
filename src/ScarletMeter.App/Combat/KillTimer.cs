using ScarletMeter.Engine;

namespace ScarletMeter.App.Combat;

/// <summary>
/// Estimates how long the current target has left ("Kill in") from how fast its HP has fallen over the last
/// <see cref="Window"/>: everyone's damage counts, the party's or not, and a burst or a pause moves the estimate
/// only as much as it moves that window. Fed once per overlay refresh. No estimate until the HP has been falling
/// for <see cref="MinimumSpan"/>, while it is not falling, when the target changes, after its HP rises (a reset or
/// a heal starts over), or when the answer would be longer than <see cref="Longest"/>. Not thread-safe: the overlay
/// calls it from its refresh loop only.
/// </summary>
public sealed class KillTimer
{
    /// <summary>How far back the HP's fall is measured.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(20);

    /// <summary>How long the HP must have been tracked before an estimate shows.</summary>
    public static readonly TimeSpan MinimumSpan = TimeSpan.FromSeconds(5);

    /// <summary>Estimates beyond this say nothing useful and are not shown.</summary>
    public static readonly TimeSpan Longest = TimeSpan.FromHours(1);

    private readonly Queue<(DateTimeOffset At, long Hp)> samples = new();
    private int? targetId;
    private long lastHp;
    private DateTimeOffset lastAt;

    /// <summary>Records the target's HP at <paramref name="now"/> and returns the estimate, or null.</summary>
    public TimeSpan? Update(TargetInfo? target, DateTimeOffset now)
    {
        if (target is null || target.HpTotal <= 0)
        {
            Clear();
            return null;
        }
        // Another target, HP that rose, or a clock that went back: start over.
        if (target.Id != targetId || (samples.Count > 0 && (target.HpCurrent > lastHp || now < lastAt)))
            Clear();
        targetId = target.Id;
        lastHp = target.HpCurrent;
        lastAt = now;
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

    /// <summary>Forgets the tracked target, as when the player drops the fight.</summary>
    public void Clear()
    {
        samples.Clear();
        targetId = null;
        lastHp = 0;
    }
}
