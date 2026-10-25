using Microsoft.Extensions.Time.Testing;
using ScarletMeter.App.BossTimers;

namespace ScarletMeter.Tests;

/// <summary>
/// One click on the boss-timers page opens at most one browser tab: the gate lets one page through per interval and
/// drops the rest, and a clock that goes back does not lock it.
/// </summary>
public sealed class BrowserLaunchGateTests
{
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void OnePagePerIntervalTheRestDropped()
    {
        var gate = new BrowserLaunchGate(clock);
        Assert.True(gate.TryPass());
        Assert.False(gate.TryPass()); // the same click's second navigation
        clock.Advance(BrowserLaunchGate.Interval - TimeSpan.FromMilliseconds(1));
        Assert.False(gate.TryPass());
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(gate.TryPass());
        Assert.False(gate.TryPass());
    }

    [Fact]
    public void ADroppedRequestDoesNotPushTheNextOneBack()
    {
        var gate = new BrowserLaunchGate(clock);
        Assert.True(gate.TryPass());
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.False(gate.TryPass());
        clock.Advance(TimeSpan.FromSeconds(1)); // three seconds after the first, one after the dropped one
        Assert.True(gate.TryPass());
    }

    [Fact]
    public void AClockThatGoesBackLetsTheNextOneThrough()
    {
        var manual = new SetClock { Now = clock.GetUtcNow() }; // FakeTimeProvider refuses to go back
        var gate = new BrowserLaunchGate(manual);
        Assert.True(gate.TryPass());
        manual.Now -= TimeSpan.FromHours(1);
        Assert.True(gate.TryPass());
        Assert.False(gate.TryPass());
    }

    private sealed class SetClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void TheIntervalIsAFewSeconds() => Assert.Equal(TimeSpan.FromSeconds(3), BrowserLaunchGate.Interval);
}
