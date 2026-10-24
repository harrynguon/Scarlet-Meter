using ScarletMeter.App.Combat;
using ScarletMeter.Engine;

namespace ScarletMeter.Tests;

/// <summary>
/// When a boss fight holds the world-event notices back: from the boss's first hit, across its adds, until it is
/// down, the target goes, its HP has stood still for <see cref="BossFightHold.HoldFor"/>, or the clock goes back.
/// </summary>
public sealed class BossFightHoldTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly TargetInfo Boss = new(1, "Test Boss", 900_000, 1_000_000, IsBoss: true);
    private static readonly TargetInfo Add = new(2, "Test Add", 40_000, 50_000, IsBoss: false);

    private static DateTimeOffset At(double seconds) => Start.AddSeconds(seconds);

    [Fact]
    public void AddsDoNotInterruptABossFight()
    {
        var hold = new BossFightHold();
        Assert.True(hold.Update(Boss, At(0)));
        Assert.True(hold.Update(Add, At(5)));                             // the player hits an add
        Assert.True(hold.Update(Add with { HpCurrent = 10_000 }, At(20)));
        Assert.True(hold.Update(Boss with { HpCurrent = 800_000 }, At(30))); // and goes back
        Assert.True(hold.Update(Add, At(30 + 59)));
        Assert.False(hold.Update(Add, At(30 + 60)));                      // the boss untouched for the hold
    }

    [Fact]
    public void TrashAloneNeverHolds()
    {
        var hold = new BossFightHold();
        Assert.False(hold.Update(Add, At(0)));
        Assert.False(hold.Update(Add with { HpCurrent = 1 }, At(1)));
        Assert.False(hold.Update(null, At(2)));
    }

    [Fact]
    public void ABossLeftStillLetsGoAndHoldsAgainOnlyWhenHit()
    {
        var hold = new BossFightHold();
        Assert.True(hold.Update(Boss, At(0)));
        Assert.True(hold.Update(Boss, At(59)));        // an invulnerable phase, or a pause
        Assert.False(hold.Update(Boss, At(60)));       // a wipe: upstream keeps the target, the notices come back
        Assert.False(hold.Update(Boss, At(600)));      // and stay back while it stands still
        Assert.True(hold.Update(Boss with { HpCurrent = 899_000 }, At(601))); // the next pull
    }

    [Fact]
    public void ADefeatedBossOrNoTargetLetsGoAtOnce()
    {
        var hold = new BossFightHold();
        Assert.True(hold.Update(Boss, At(0)));
        Assert.False(hold.Update(Boss with { HpCurrent = 0 }, At(10)));
        Assert.False(hold.Update(Boss with { HpCurrent = 0 }, At(11)));
        Assert.False(hold.Update(Add, At(12)));        // trash after the boss

        Assert.True(hold.Update(Boss with { Id = 3 }, At(20)));          // the next boss
        Assert.False(hold.Update(null, At(21)));       // a reset, or a kill the engine drops
        Assert.False(hold.Update(Add, At(22)));
    }

    [Fact]
    public void ClearingForgetsTheBoss()
    {
        var hold = new BossFightHold();
        Assert.True(hold.Update(Boss, At(0)));
        hold.Clear();
        Assert.False(hold.Update(Add, At(1)));
        Assert.True(hold.Update(Boss, At(2)));         // the boss as the target again
    }

    // A clock that goes back cannot say how long the HP has been still: whatever was held lets go, the boss or an
    // add as the target alike, and the next hit on the boss holds again.
    [Fact]
    public void AClockThatGoesBackLetsGoUntilTheNextHit()
    {
        var hold = new BossFightHold();
        Assert.True(hold.Update(Boss, At(0)));
        Assert.False(hold.Update(Boss, At(-3600)));    // an hour back, the boss still the target
        Assert.False(hold.Update(Boss, At(-3599)));
        Assert.True(hold.Update(Boss with { HpCurrent = 899_000 }, At(-3598)));
        Assert.True(hold.Update(Boss with { HpCurrent = 899_000 }, At(-3598 + 59)));
        Assert.False(hold.Update(Boss with { HpCurrent = 899_000 }, At(-3598 + 60)));

        // A wipe with an add as the last target: the notices came back, and a clock stepped back keeps them back.
        var afterWipe = new BossFightHold();
        Assert.True(afterWipe.Update(Boss, At(0)));
        Assert.True(afterWipe.Update(Add, At(10)));
        Assert.False(afterWipe.Update(Add, At(70)));
        Assert.False(afterWipe.Update(Add, At(70 - 3600)));
        Assert.False(afterWipe.Update(Add, At(80 - 3600)));
    }
}
