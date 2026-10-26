namespace ScarletMeter.App.Pages;

/// <summary>
/// How tall the overlay's bar area is: a row for each player shown, up to <see cref="MostRows"/>, so it is empty with
/// nobody to show (at start-up, after a reset) and grows by itself as a fight's players come, keeping the last fight's
/// bars until the next (Harry, 2026-10-26: "so this means the player won't need to fiddle with resizing the dps
/// meter"). More players scroll inside it, the player's own row pinned in view. It replaced the bar area the player
/// sized by dragging the meter's edge (#37, commit 3500895; docs/ARCHITECTURE.md, "Overlay"). Pure, so the rule is
/// tested without rendering.
/// </summary>
public static class BarArea
{
    /// <summary>The most rows the bar area shows before it scrolls (Harry: "up to five dps bar players worth").</summary>
    public const int MostRows = 5;

    /// <summary>The bar area's rows for <paramref name="players"/> shown: one each, up to <see cref="MostRows"/>.</summary>
    public static int Rows(int players) => Math.Clamp(players, 0, MostRows);

    /// <summary>
    /// Whether the bar area scrolls: more players than it shows, or the window as tall as the screen allows and shorter
    /// than the content (<paramref name="contentCut"/>), when the bar area gives way before the notices do.
    /// </summary>
    public static bool Scrolls(int players, bool contentCut) => players > MostRows || contentCut;
}
