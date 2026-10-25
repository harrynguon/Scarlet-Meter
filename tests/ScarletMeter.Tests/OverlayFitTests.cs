using System.Windows;
using ScarletMeter.App.Windows;

namespace ScarletMeter.Tests;

/// <summary>
/// Where the overlay window goes: as tall as its content, its top the player's own, moved up only to stay inside the
/// work area, never above it; and how a resize of the bar area becomes whole rows, at most ten.
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

    // The bar area is at most MostRows rows (Harry: "max height of 10 players worth"), a row being its 26 px and 3 px gap
    // at the row size; a resize stops at the content with no rows and that.
    [Fact]
    public void TheBarAreaIsAtMostTenRows()
    {
        Assert.Equal(10, OverlayFit.MostRows);
        Assert.Equal(ScarletMeter.App.Settings.AppearanceSettings.MaxBarRows, OverlayFit.MostRows);
        Assert.Equal(29, OverlayFit.RowStep(1));
        Assert.Equal(36.25, OverlayFit.RowStep(1.25));
        Assert.Equal(490, OverlayFit.MostHeight(200, 1));
        Assert.Equal(562.5, OverlayFit.MostHeight(200, 1.25));
    }

    // Where a resize ends becomes the bar area's rows: the nearest whole number above the content with no rows (its
    // height rounded up, as the window is), 0 to 10; none until the page has reported.
    [Theory]
    [InlineData(260, 200.4, 29, 2)]     // 2.03 rows above 201
    [InlineData(275, 200.4, 29, 3)]     // 2.55
    [InlineData(150, 200.4, 29, 0)]     // above nothing: no rows, the empty meter
    [InlineData(900, 200.4, 29, 10)]    // past ten: ten
    [InlineData(400, 200, 36.25, 6)]    // at row size 1.25: 5.52
    public void AResizeBecomesWholeRows(double height, double empty, double step, int rows) =>
        Assert.Equal(rows, OverlayFit.RowsFor(height, empty, step));

    // While an edge is dragged the window is held between the least and the most by stopping that edge (Harry: dragged
    // by the top past the most, the whole window moved up with the cursor). WMSZ_: 3 top, 4 top-left, 5 top-right,
    // 6 bottom, 7 bottom-left, 8 bottom-right; 1 and 2, left and right, leave the height alone. Device pixels; a
    // fractional least is rounded up and a most down.
    [Theory]
    [InlineData(100, 700, 3, 200, 400, 300, 700)]   // the top past the most: the top stops, the bottom stays
    [InlineData(100, 700, 4, 200, 400, 300, 700)]
    [InlineData(100, 700, 5, 200, 400, 300, 700)]
    [InlineData(600, 700, 3, 200, 400, 500, 700)]   // the top below the least
    [InlineData(100, 700, 6, 200, 400, 100, 500)]   // the bottom past the most: the bottom stops, the top stays
    [InlineData(100, 700, 8, 200, 400, 100, 500)]
    [InlineData(100, 150, 7, 200, 400, 100, 300)]   // the bottom below the least
    [InlineData(100, 400, 3, 200, 400, 100, 400)]   // within: as dragged
    [InlineData(100, 700, 1, 200, 400, 100, 700)]   // the left edge: the height is not its
    [InlineData(100, 700, 2, 200, 400, 100, 700)]
    [InlineData(100, 700, 3, 199.2, 400.8, 300, 700)]
    public void AnEdgeIsHeldWhereItIsDragged(int top, int bottom, int edge, double least, double most, int heldTop, int heldBottom) =>
        Assert.Equal((heldTop, heldBottom), OverlayFit.HoldDraggedEdge(top, bottom, edge, least, most));

    [Fact]
    public void AnEdgeIsFreeWithoutAMost()
    {
        Assert.Equal((100, 1700), OverlayFit.HoldDraggedEdge(100, 1700, 6, 96, double.PositiveInfinity));
        Assert.Equal((-500, 700), OverlayFit.HoldDraggedEdge(-500, 700, 3, 96, double.PositiveInfinity));
        Assert.Equal((100, 700), OverlayFit.HoldDraggedEdge(100, 700, 3, double.NaN, 400));
    }

    [Fact]
    public void NoRowsBeforeThePageHasReported()
    {
        Assert.Null(OverlayFit.RowsFor(300, double.NaN, 29));
        Assert.Null(OverlayFit.RowsFor(300, 200, double.NaN));
        Assert.Null(OverlayFit.RowsFor(300, 200, 0));
        Assert.Null(OverlayFit.RowsFor(double.NaN, 200, 29));
    }

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
