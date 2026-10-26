namespace ScarletMeter.App.Pages;

/// <summary>
/// The bar area's rows over time. They follow <see cref="BarArea.Rows"/> at once when more are wanted, or none (at
/// start-up, after a reset), but drop to fewer only once fewer have been wanted for <see cref="ShrinkAfter"/>.
/// Upstream's current target is the one the player hit last, so in a fight with adds the players shown change with every
/// switch between the boss and an add, and without the pause the meter would shrink and grow each time (the deep audit
/// on #40; Harry chose a pause over following the target, 2026-10-26). A clock that goes back starts the pause again.
/// Not thread-safe: the overlay calls it from its refresh loop only.
/// </summary>
public sealed class BarRowsHold
{
    /// <summary>How long fewer rows must be wanted before the bar area drops to them.</summary>
    public static readonly TimeSpan ShrinkAfter = TimeSpan.FromSeconds(15);

    private int rows;
    private DateTimeOffset? fewerSince;

    /// <summary>The rows to show at <paramref name="now"/> while <paramref name="wanted"/> are wanted.</summary>
    public int Update(int wanted, DateTimeOffset now)
    {
        if (wanted >= rows || wanted == 0)
        {
            rows = wanted;
            fewerSince = null;
        }
        else if (fewerSince is not { } since || now < since)
        {
            fewerSince = now;
        }
        else if (now - since >= ShrinkAfter)
        {
            rows = wanted;
            fewerSince = null;
        }
        return rows;
    }

    /// <summary>Forgets the rows, as a reset does, so the next update takes what is wanted at once.</summary>
    public void Clear()
    {
        rows = 0;
        fewerSince = null;
    }
}
