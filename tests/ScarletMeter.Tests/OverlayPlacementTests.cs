using System.Windows;
using ScarletMeter.App.Windows;

namespace ScarletMeter.Tests;

/// <summary>
/// The overlay's own placement beside the grown one it shows: what the player's drags and resizes make theirs, what
/// the system's moves carry along, and when the window waits before shrinking.
/// </summary>
public sealed class OverlayPlacementTests
{
    // A 1920x1080 monitor with a 40 px taskbar at the bottom.
    private static readonly Rect Work = new(0, 0, 1920, 1040);

    private static OverlayPlacement Placement(double top, double height)
    {
        var placement = new OverlayPlacement();
        placement.Start(top, height);
        return placement;
    }

    private static (double Top, double Height) Player(OverlayPlacement p) => (p.PlayerTop, p.PlayerHeight);

    [Fact]
    public void NothingIsKnownBeforeTheWindowIsLaidOut()
    {
        var placement = new OverlayPlacement();
        Assert.False(placement.Started);
        placement.PlayerPlaced(100, 300);
        placement.MovedTo(200);
        Assert.False(placement.Started);
    }

    [Fact]
    public void MovingAGrownWindowKeepsThePlayersHeight()
    {
        var placement = Placement(100, 260);
        placement.Showing(placement.Target(300, Work)); // grown to (100, 300)
        placement.PlayerPlaced(200, 300);
        Assert.Equal((200.0, 260.0), Player(placement));
        Assert.Equal((200.0, 300.0), placement.Target(300, Work));
        Assert.Equal((200.0, 260.0), placement.Target(250, Work)); // and it returns to the player's own height there
    }

    [Fact]
    public void AGrownWindowMovedUpForTheBottomStaysWhereItIsDropped()
    {
        var placement = Placement(760, 260);
        var grown = placement.Target(300, Work);
        Assert.Equal((740.0, 300.0), grown);
        placement.Showing(grown);
        // Dragged up 100 px: it stays at 640, with no snap after the drag, and returns to 640 at the player's height.
        placement.PlayerPlaced(640, 300);
        Assert.Equal((640.0, 300.0), placement.Target(300, Work));
        Assert.Equal((640.0, 260.0), placement.Target(250, Work));

        // Dragged up only 10 px: it stays at 730 too, rather than snapping back to 740.
        var nudged = Placement(760, 260);
        nudged.Showing(nudged.Target(300, Work));
        nudged.PlayerPlaced(730, 300);
        Assert.Equal((730.0, 300.0), nudged.Target(300, Work));
    }

    [Fact]
    public void AClickOnTheHeaderChangesNothing()
    {
        var placement = Placement(760, 260);
        placement.Showing(placement.Target(300, Work)); // shown at (740, 300)
        placement.PlayerPlaced(740, 300);
        Assert.Equal((760.0, 260.0), Player(placement));
    }

    [Fact]
    public void AResizeMakesWhatThePlayerSeesTheirOwn()
    {
        var placement = Placement(760, 260);
        placement.Showing(placement.Target(300, Work)); // shown at (740, 300)
        placement.PlayerPlaced(700, 340); // the top edge dragged up
        Assert.Equal((700.0, 340.0), Player(placement));
        Assert.Equal((700.0, 340.0), placement.Target(300, Work));
        // Made shorter than the content, it grows back to fit once the resize ends.
        placement.PlayerPlaced(700, 280);
        Assert.Equal((700.0, 280.0), Player(placement));
        Assert.Equal((700.0, 300.0), placement.Target(300, Work));
    }

    [Fact]
    public void AMoveBySomethingElseCarriesThePlayersTopAndComesBackExactly()
    {
        var placement = Placement(760, 260);
        placement.Showing(placement.Target(300, Work)); // shown at (740, 300)
        placement.MovedTo(-32000); // Windows parks a minimized window off screen
        Assert.Equal((-32000.0, 300.0), placement.Shown);
        placement.MovedTo(740); // and puts it back on restore
        Assert.Equal((760.0, 260.0), Player(placement));
        Assert.Equal((740.0, 300.0), placement.Shown);
    }

    [Fact]
    public void OnlyAShorterTargetWaitsBeforeShrinking()
    {
        var placement = Placement(100, 260);
        Assert.False(placement.WaitsToShrink(placement.Target(300, Work), Work)); // growing: at once
        placement.Showing(placement.Target(300, Work));
        Assert.False(placement.WaitsToShrink(placement.Target(300, Work), Work));
        Assert.False(placement.WaitsToShrink((100, 299.6), Work)); // within half a pixel: the same
        Assert.True(placement.WaitsToShrink(placement.Target(250, Work), Work)); // back to the player's height: after a moment
    }

    [Fact]
    public void AWindowThatNoLongerFitsTheScreenShrinksAtOnce()
    {
        // As tall as a 1080p work area, then on a shorter one (a monitor with less room, or a larger display scale).
        var placement = Placement(0, 260);
        placement.Showing(placement.Target(2000, Work));
        Assert.Equal((0.0, 1040.0), placement.Shown);
        var shorter = new Rect(0, 0, 1280, 688);
        Assert.Equal((0.0, 688.0), placement.Target(2000, shorter));
        Assert.False(placement.WaitsToShrink(placement.Target(2000, shorter), shorter));
    }
}
