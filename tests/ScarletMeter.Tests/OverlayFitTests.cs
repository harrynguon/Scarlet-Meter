using System.Windows;
using ScarletMeter.App.Pages;
using ScarletMeter.App.Windows;

namespace ScarletMeter.Tests;

/// <summary>
/// Where the overlay window goes: as tall as its content, its top the player's own, moved up only to stay inside the
/// work area, never above it; and the bar area's rows, which follow the players shown, at most five.
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

    // With a most, a taller height is shown at it, its top kept; within, the given height stands; below the least the
    // window grows as before. The overlay passes its content's height as both, so the window is exactly as tall as its
    // content, shrinking to it as well as growing.
    [Fact]
    public void AHeightIsShownUpToTheMost()
    {
        Assert.Equal((100.0, 400.0), OverlayFit.For(100, 400, 200, Work, 490));
        Assert.Equal((100.0, 490.0), OverlayFit.For(100, 700, 200, Work, 489.2)); // whole pixels, rounded up
        Assert.Equal((100.0, 250.0), OverlayFit.For(100, 180, 250, Work, 540));
        Assert.Equal((100.0, 260.0), OverlayFit.For(100, 260, 200, Work)); // no most: as before
        Assert.Equal((100.0, 300.0), OverlayFit.For(100, 260, 300, Work, 250)); // a most below the least is none
        Assert.Equal((100.0, 251.0), OverlayFit.For(100, 400, 250.3, Work, 250.3)); // the content's height: shrinks to it
        Assert.Equal((740.0, 300.0), OverlayFit.For(760, 260, 300, Work, 300)); // and grows to it, moving up at the bottom
    }

    // The bar area follows the players shown (Harry, 2026-10-26: the player "won't need to fiddle with resizing"): a row
    // each, none with nobody to show, at most five; past five it scrolls, as it does whenever the window is as tall as
    // the screen allows.
    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(1, 1, false)]
    [InlineData(4, 4, false)]  // a dungeon party
    [InlineData(5, 5, false)]
    [InlineData(6, 5, true)]
    [InlineData(20, 5, true)]  // a raid: the top five in view, the rest scroll
    public void TheBarAreaFollowsThePlayersUpToFive(int players, int rows, bool scrolls)
    {
        Assert.Equal(5, BarArea.MostRows);
        Assert.Equal(rows, BarArea.Rows(players));
        Assert.Equal(scrolls, BarArea.Scrolls(players, contentCut: false));
        Assert.True(BarArea.Scrolls(players, contentCut: true));
    }
    // The overlay's floor, which a saved size is kept to, is below the smallest empty meter (77 px with no notice due,
    // 75 px at row size 0.75; measured in headless Edge): at 96 px a saved overlay at no rows was dropped as unusable
    // and its place lost (audit on PR #37). The window saves no height below the floor.
    [Fact]
    public void TheEmptyMeterIsAboveTheOverlaysFloor() =>
        Assert.True(ScarletMeter.App.Settings.WindowPlacement.MinSize <= 72, "the floor must stay below the smallest empty meter");

    // The page passes its content height to the window only when it is new: always the first (Codex review on
    // PR #28: a comparison with the initial NaN is false, so the window never grew), then on half a pixel or more.
    [Fact]
    public void TheFirstHeightAndRealChangesReachTheWindow()
    {
        Assert.True(OverlayFit.IsNewHeight(double.NaN, 300));
        Assert.False(OverlayFit.IsNewHeight(300, 300.4));
        Assert.False(OverlayFit.IsNewHeight(300, 299.6));
        Assert.True(OverlayFit.IsNewHeight(300, 300.5));
        Assert.True(OverlayFit.IsNewHeight(300, 250));
    }
}
