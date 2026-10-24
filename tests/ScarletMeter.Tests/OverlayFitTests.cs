using System.Windows;
using ScarletMeter.App.Windows;

namespace ScarletMeter.Tests;

/// <summary>
/// Where the overlay window goes so its whole content shows (nothing in the meter scrolls): the player's placement
/// when the content fits, taller when it does not, moved up only to stay inside the work area, never above it.
/// </summary>
public sealed class OverlayFitTests
{
    // A 1920x1080 monitor with a 40 px taskbar at the bottom.
    private static readonly Rect Work = new(0, 0, 1920, 1040);

    [Fact]
    public void ContentThatFitsKeepsThePlayersPlacement()
    {
        Assert.Equal((100.0, 260.0), OverlayFit.For(100, 260, 221, Work));
        Assert.Equal((100.0, 260.0), OverlayFit.For(100, 260, 260, Work));
        Assert.Equal((100.0, 260.0), OverlayFit.For(100, 260, double.NaN, Work)); // nothing reported yet
    }

    [Fact]
    public void TallerContentGrowsTheWindowDownwards()
    {
        Assert.Equal((100.0, 303.0), OverlayFit.For(100, 260, 302.4, Work)); // whole pixels, rounded up
        Assert.Equal((100.0, 900.0), OverlayFit.For(100, 260, 900, Work));
    }

    [Fact]
    public void NearTheBottomTheWindowMovesUpJustEnough()
    {
        Assert.Equal((740.0, 300.0), OverlayFit.For(760, 260, 300, Work)); // would end at 1060, the work area at 1040
        Assert.Equal((0.0, 1040.0), OverlayFit.For(760, 260, 2000, Work)); // taller than the work area: capped to it
    }

    [Fact]
    public void ThePlayersOwnPlacementIsNeverPushedFurther()
    {
        // The player put the window partly below the work area: growing stays within the player's own bottom.
        Assert.Equal((900.0, 300.0), OverlayFit.For(940, 260, 300, Work));
        // The player put it above the work area's top (another monitor above): it does not move down for it.
        Assert.Equal((-50.0, 300.0), OverlayFit.For(-50, 260, 300, Work));
        // The player made it taller than the work area: that height stands.
        Assert.Equal((0.0, 1200.0), OverlayFit.For(0, 1200, 1300, Work));
    }
}
