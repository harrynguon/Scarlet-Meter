using ScarletMeter.App.Combat;
using ScarletMeter.Engine;

namespace ScarletMeter.Tests;

/// <summary>The overlay's fight phase: live while the target's HP moves, settled after a still spell, over at 0 HP.</summary>
public sealed class FightStateTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static TargetInfo Boss(long hp, int id = 7) => new(id, "Test Boss", hp, 1_000_000, IsBoss: true);

    [Fact]
    public void NoTargetIsNone() =>
        Assert.Equal(FightPhase.None, new FightState().Update(null, Start));

    [Fact]
    public void MovingHpIsLiveAndStillHpSettles()
    {
        var state = new FightState();
        Assert.Equal(FightPhase.Live, state.Update(Boss(900_000), Start));
        Assert.Equal(FightPhase.Live, state.Update(Boss(900_000), Start.AddSeconds(9.9)));
        Assert.Equal(FightPhase.Settled, state.Update(Boss(900_000), Start.AddSeconds(10)));
        Assert.Equal(FightPhase.Live, state.Update(Boss(899_000), Start.AddSeconds(11))); // hits again
    }

    [Fact]
    public void ZeroHpIsDefeated()
    {
        var state = new FightState();
        state.Update(Boss(10_000), Start);
        Assert.Equal(FightPhase.Defeated, state.Update(Boss(0), Start.AddSeconds(1)));
        Assert.Equal(FightPhase.Defeated, state.Update(Boss(0), Start.AddSeconds(30)));
    }

    [Fact]
    public void ANewTargetOrAClockGoingBackIsLiveAgain()
    {
        var state = new FightState();
        state.Update(Boss(900_000), Start);
        Assert.Equal(FightPhase.Settled, state.Update(Boss(900_000), Start.AddSeconds(12)));
        Assert.Equal(FightPhase.Live, state.Update(Boss(900_000, id: 8), Start.AddSeconds(13)));
        Assert.Equal(FightPhase.Settled, state.Update(Boss(900_000, id: 8), Start.AddSeconds(25)));
        // An hour back: live from there, and settled ten seconds later, not an hour later.
        Assert.Equal(FightPhase.Live, state.Update(Boss(900_000, id: 8), Start.AddHours(-1)));
        Assert.Equal(FightPhase.Settled, state.Update(Boss(900_000, id: 8), Start.AddHours(-1).AddSeconds(11)));
    }
}
