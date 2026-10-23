using System.Windows;
using ScarletMeter.App.Windows;

namespace ScarletMeter.Tests;

/// <summary>
/// Whether a saved window placement is restored, per monitor rather than against the virtual screen's bounding
/// box (Codex review on PR #2). The layout is synthetic: a 1920 by 1080 primary and a 1280 by 1024 monitor to
/// its right, 600 pixels lower, so the bounding box has an empty corner above the second monitor.
/// </summary>
public sealed class ScreenPlacementTests
{
    private static readonly Rect[] LShaped = [new(0, 0, 1920, 1080), new(1920, 600, 1280, 1024)];

    [Fact]
    public void APlacementInTheGapBetweenMonitorsIsNotRestored()
    {
        var inTheGap = new Rect(2400, 100, 400, 300); // inside the bounding box, on no monitor
        Assert.False(ScreenPlacement.IsVisible(inTheGap, LShaped));
    }

    [Fact]
    public void APlacementMostlyOnOneMonitorIsRestored()
    {
        Assert.True(ScreenPlacement.IsVisible(new Rect(100, 100, 400, 260), LShaped));
        Assert.True(ScreenPlacement.IsVisible(new Rect(1900, 700, 400, 300), LShaped)); // 380 by 300 on the second
    }

    [Fact]
    public void SmallPiecesOnTwoMonitorsDoNotAddUp()
    {
        // 40 by 80 on the primary and 40 by 40 on the second: neither is the 80 by 40 needed on one monitor.
        Assert.False(ScreenPlacement.IsVisible(new Rect(1880, 560, 80, 80), LShaped));
    }

    // A smoke test of the Win32 calls only: it proves they run without throwing here. It cannot fail on an empty
    // list, because Monitors() falls back to the virtual screen when none can be listed.
    [Fact]
    public void ListingTheMonitorsDoesNotThrow() =>
        Assert.NotEmpty(ScreenPlacement.Monitors());
}
