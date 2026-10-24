using ScarletMeter.Engine;

namespace ScarletMeter.App.Combat;

/// <summary>
/// Whether a boss fight is under way, for the world-event notices that stand aside during one. It follows the boss
/// rather than the current target: upstream's target is whatever the player hit last, so in a fight with adds it
/// flips between the boss and them, and the notices would jump in and out with it. The hold lasts while the boss's
/// HP moved within the last <see cref="HoldFor"/>, whichever target is current, and ends when that boss is seen at
/// 0 HP, when the target goes (a reset, or a kill the engine drops), or when its HP has stood still that long (a
/// wipe, or a boss left alive: upstream keeps its target until the next hit, so without this the notices would stay
/// hidden indefinitely). Not thread-safe: the overlay calls it from its refresh loop only.
/// </summary>
public sealed class BossFightHold
{
    /// <summary>
    /// How long after its HP last moved a boss still holds the notices back: longer than a short invulnerable phase
    /// or a run to the next add, short enough that a wipe brings them back soon. Not verified against the game.
    /// </summary>
    public static readonly TimeSpan HoldFor = TimeSpan.FromSeconds(60);

    private int? bossId;
    private long bossHp;
    private DateTimeOffset movedAt;

    /// <summary>Records the target at <paramref name="now"/> and returns whether a boss fight holds the notices back.</summary>
    public bool Update(TargetInfo? target, DateTimeOffset now)
    {
        if (target is null)
        {
            bossId = null;
            return false;
        }
        // Another boss, its HP moving, or a clock that went back: the boss fight is on from now. An add as the
        // target changes nothing, so the boss it came from keeps holding.
        if (target.IsBoss && (target.Id != bossId || target.HpCurrent != bossHp || now < movedAt))
        {
            bossId = target.Id;
            bossHp = target.HpCurrent;
            movedAt = now;
        }
        return bossId is not null && bossHp > 0 && now - movedAt < HoldFor;
    }

    /// <summary>Forgets the boss, as after a reset.</summary>
    public void Clear() => bossId = null;
}
