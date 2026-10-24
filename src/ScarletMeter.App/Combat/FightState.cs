using ScarletMeter.Engine;

namespace ScarletMeter.App.Combat;

/// <summary>Where the current fight stands, for the overlay's header and its effects.</summary>
public enum FightPhase
{
    /// <summary>No target.</summary>
    None,

    /// <summary>The target's HP is moving.</summary>
    Live,

    /// <summary>The target's HP has not changed for <see cref="FightState.SettleAfter"/> (a pause, a wipe, a lull).</summary>
    Settled,

    /// <summary>The target's HP is 0.</summary>
    Defeated,
}

/// <summary>
/// Follows the current target's HP from refresh to refresh and says whether the fight is live, has settled or is
/// over. Upstream keeps its last target after the fight, so without this the header would say "Boss combat" and
/// the bar effects would keep running until the next pull. A new target, or HP that moves, is live again. Not
/// thread-safe: the overlay calls it from its refresh loop only.
/// </summary>
public sealed class FightState
{
    /// <summary>How long the target's HP must stay unchanged before the fight counts as settled.</summary>
    public static readonly TimeSpan SettleAfter = TimeSpan.FromSeconds(10);

    private int? targetId;
    private long hp;
    private DateTimeOffset changedAt;

    /// <summary>Records the target at <paramref name="now"/> and returns the fight's phase.</summary>
    public FightPhase Update(TargetInfo? target, DateTimeOffset now)
    {
        if (target is null)
        {
            targetId = null;
            return FightPhase.None;
        }
        // Another target, moving HP, or a clock that went back: the fight is live from now.
        if (target.Id != targetId || target.HpCurrent != hp || now < changedAt)
        {
            targetId = target.Id;
            hp = target.HpCurrent;
            changedAt = now;
        }
        if (target.HpCurrent <= 0) return FightPhase.Defeated;
        return now - changedAt >= SettleAfter ? FightPhase.Settled : FightPhase.Live;
    }
}
