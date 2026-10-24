using ScarletMeter.App.Combat;
using ScarletMeter.Engine;

namespace ScarletMeter.Tests;

/// <summary>
/// The overlay's "Kill in" estimate: the target's remaining HP over how fast it fell during the last
/// <see cref="KillTimer.Window"/>, and the cases where it says nothing.
/// </summary>
public sealed class KillTimerTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static TargetInfo Boss(long hp, int id = 7) => new(id, "Test Boss", hp, 1_000_000, IsBoss: true);

    // Feeds the timer a target losing perSecond HP each second from fullHp, ten samples a second like the overlay,
    // and returns the last estimate.
    private static TimeSpan? Run(KillTimer timer, long fromHp, long perSecond, double seconds, double startAt = 0, int id = 7)
    {
        TimeSpan? last = null;
        for (var tenth = 0; tenth <= seconds * 10; tenth++)
        {
            var hp = fromHp - perSecond * tenth / 10;
            last = timer.Update(Boss(hp, id), Start.AddSeconds(startAt + tenth / 10.0));
        }
        return last;
    }

    [Fact]
    public void ASteadyFallGivesTheTimeTheRestWouldTake()
    {
        // 1,000,000 HP losing 10,000 a second: after 10 s, 900,000 left, 90 s to go.
        Assert.Equal(TimeSpan.FromSeconds(90), Run(new KillTimer(), 1_000_000, 10_000, 10));
    }

    [Fact]
    public void NothingShowsUntilTheHpHasFallenForAWhile()
    {
        var timer = new KillTimer();
        Assert.Null(Run(timer, 1_000_000, 10_000, 4.9));
        Assert.NotNull(timer.Update(Boss(949_000), Start.AddSeconds(5.1)));
    }

    [Fact]
    public void OnlyTheLastWindowCounts()
    {
        // 30 s at 1,000/s, then 30 s at 20,000/s: the estimate follows the new pace, not the average.
        var timer = new KillTimer();
        Run(timer, 1_000_000, 1_000, 30);
        var estimate = Run(timer, 970_000, 20_000, 30, startAt: 30.1);
        Assert.Equal(TimeSpan.FromSeconds(19), estimate); // 370,000 left at 20,000/s is 18.5 s, rounded up
    }

    [Fact]
    public void NoTargetNoFallOrDeadTargetGivesNothing()
    {
        var timer = new KillTimer();
        Assert.Null(timer.Update(null, Start));
        Assert.Null(Run(timer, 500_000, 0, 10)); // HP not falling
        Assert.Null(timer.Update(Boss(0), Start.AddSeconds(11)));
        Assert.Null(timer.Update(new TargetInfo(7, "Unknown HP", 0, 0, IsBoss: true), Start.AddSeconds(12)));
    }

    [Fact]
    public void AnotherTargetOrRisingHpStartsOver()
    {
        var timer = new KillTimer();
        Assert.NotNull(Run(timer, 1_000_000, 10_000, 10));
        Assert.Null(timer.Update(Boss(500_000, id: 8), Start.AddSeconds(10.1))); // a new target: no history yet
        Assert.NotNull(Run(timer, 500_000, 10_000, 10, startAt: 10.2, id: 8));
        Assert.Null(timer.Update(Boss(900_000, id: 8), Start.AddSeconds(20.3))); // reset to near full: start over
        Assert.Null(timer.Update(Boss(899_000, id: 8), Start.AddSeconds(21)));
    }

    [Fact]
    public void AHealMeasuresOnlyTheFallAfterIt()
    {
        // 10 s falling fast, a heal back to 950,000, then 6 s at 1,000/s: 944,000 left at the new pace is about
        // 944 s. Measured from before the heal it would read about 270 s.
        var timer = new KillTimer();
        Run(timer, 1_000_000, 10_000, 10);
        var estimate = Run(timer, 950_000, 1_000, 6, startAt: 10.1);
        Assert.NotNull(estimate);
        Assert.InRange(estimate.Value.TotalSeconds, 930, 960);
    }

    [Fact]
    public void SwitchingToAnAddAndBackResumesTheBossEstimate()
    {
        // The boss falls 10,000/s for 10 s; the player hits an add for 3 s while the party keeps on the boss; back on
        // the boss its estimate shows at once, from the whole fall: 870,000 left at 10,000/s is 87 s.
        var timer = new KillTimer();
        Run(timer, 1_000_000, 10_000, 10);
        Run(timer, 50_000, 1_000, 3, startAt: 10.1, id: 99);
        var back = timer.Update(Boss(870_000), Start.AddSeconds(13.1));
        Assert.NotNull(back);
        Assert.InRange(back.Value.TotalSeconds, 86, 88);
    }

    [Fact]
    public void ATargetUnseenForAWholeWindowIsForgotten()
    {
        var timer = new KillTimer();
        Run(timer, 1_000_000, 10_000, 10);
        Run(timer, 50_000, 100, 25, startAt: 10.1, id: 99); // the boss unseen for more than 20 s
        Assert.Null(timer.Update(Boss(500_000), Start.AddSeconds(35.2)));
    }

    [Fact]
    public void OnlyTheLastFewTargetsAreRemembered()
    {
        var timer = new KillTimer();
        Run(timer, 1_000_000, 10_000, 6);
        for (var add = 0; add < KillTimer.RememberedTargets; add++)
            timer.Update(Boss(50_000, id: 100 + add), Start.AddSeconds(6.1 + add * 0.1));
        Assert.Null(timer.Update(Boss(939_000), Start.AddSeconds(6.6))); // pushed out by the adds: starts again
    }

    [Fact]
    public void AClockThatGoesBackStartsOver()
    {
        var timer = new KillTimer();
        Run(timer, 1_000_000, 10_000, 10, startAt: 100);
        Assert.Null(timer.Update(Boss(899_000), Start.AddSeconds(50))); // a minute back
        var estimate = Run(timer, 899_000, 1_000, 6, startAt: 50.1);
        Assert.NotNull(estimate);
        Assert.InRange(estimate.Value.TotalSeconds, 895, 920); // 893,000 left at about 1,000/s, not the old 89 s
    }

    [Fact]
    public void ASlowFallBeyondAnHourShowsNothing()
    {
        // 1,000,000 HP losing 100 a second would take nearly three hours.
        Assert.Null(Run(new KillTimer(), 1_000_000, 100, 10));
    }

    [Fact]
    public void ClearForgetsTheFight()
    {
        var timer = new KillTimer();
        Assert.NotNull(Run(timer, 1_000_000, 10_000, 10));
        timer.Clear();
        Assert.Null(timer.Update(Boss(899_000), Start.AddSeconds(10.1)));
    }
}
