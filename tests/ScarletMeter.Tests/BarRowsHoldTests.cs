using ScarletMeter.App.Pages;

namespace ScarletMeter.Tests;

/// <summary>
/// The bar area's rows over time (<see cref="BarRowsHold"/>): more at once, none at once, fewer only after the pause,
/// so a fight's switches between a boss and its adds do not resize the meter.
/// </summary>
public sealed class BarRowsHoldTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 26, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MoreRowsComeAtOnce()
    {
        var hold = new BarRowsHold();
        Assert.Equal(2, hold.Update(2, T0));
        Assert.Equal(4, hold.Update(4, T0.AddSeconds(1)));
        Assert.Equal(5, hold.Update(5, T0.AddSeconds(1)));
    }

    [Fact]
    public void FewerRowsWaitForThePause()
    {
        var hold = new BarRowsHold();
        hold.Update(4, T0);
        var fewer = T0.AddSeconds(10);
        Assert.Equal(4, hold.Update(2, fewer));
        Assert.Equal(4, hold.Update(2, fewer + BarRowsHold.ShrinkAfter - TimeSpan.FromMilliseconds(1)));
        Assert.Equal(2, hold.Update(2, fewer + BarRowsHold.ShrinkAfter));
        Assert.Equal(TimeSpan.FromSeconds(15), BarRowsHold.ShrinkAfter);
    }

    // The boss and an add, back and forth: the pause starts again each time the count comes back.
    [Fact]
    public void ABackAndForthWithinThePauseKeepsTheRows()
    {
        var hold = new BarRowsHold();
        hold.Update(4, T0);
        for (var i = 0; i < 10; i++)
        {
            var add = T0.AddSeconds(20 * i);
            Assert.Equal(4, hold.Update(2, add));                         // an add only two have hit
            Assert.Equal(4, hold.Update(2, add.AddSeconds(14)));
            Assert.Equal(4, hold.Update(4, add.AddSeconds(14.5)));        // back on the boss
        }
    }

    // A count that changes again during the pause still drops when the pause from the first drop is over, to the
    // count wanted then.
    [Fact]
    public void ThePauseCountsFromTheFirstDrop()
    {
        var hold = new BarRowsHold();
        hold.Update(5, T0);
        Assert.Equal(5, hold.Update(2, T0.AddSeconds(1)));
        Assert.Equal(5, hold.Update(3, T0.AddSeconds(10)));
        Assert.Equal(3, hold.Update(3, T0.AddSeconds(16)));
    }

    // Nobody left to show (start-up, a reset) empties the bar area at once, and Clear forgets the rows.
    [Fact]
    public void NoneEmptiesAtOnceAndClearForgets()
    {
        var hold = new BarRowsHold();
        hold.Update(4, T0);
        Assert.Equal(0, hold.Update(0, T0.AddSeconds(1)));

        hold.Update(4, T0.AddSeconds(2));
        hold.Clear();
        Assert.Equal(1, hold.Update(1, T0.AddSeconds(3)));
    }

    // A clock that goes back cannot say how long fewer have been wanted, so the pause starts again.
    [Fact]
    public void AClockThatGoesBackStartsThePauseAgain()
    {
        var hold = new BarRowsHold();
        hold.Update(4, T0);
        Assert.Equal(4, hold.Update(2, T0.AddSeconds(10)));
        Assert.Equal(4, hold.Update(2, T0.AddHours(-1)));
        Assert.Equal(4, hold.Update(2, T0.AddHours(-1).AddSeconds(14)));
        Assert.Equal(2, hold.Update(2, T0.AddHours(-1) + BarRowsHold.ShrinkAfter));
    }
}
