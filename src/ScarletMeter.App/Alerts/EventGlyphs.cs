using ScarletMeter.WorldEvents;

namespace ScarletMeter.App.Alerts;

/// <summary>
/// The event row's icons: Scarlet's own line drawings, one per catalog event, on the 16-unit grid of the header's
/// icons and stroked like them (alerts.css). Each is path data only, so the markup binds it as an attribute value
/// and never as raw HTML.
/// </summary>
public static class EventGlyphs
{
    /// <summary>A plain ring, for an id with no drawing of its own.</summary>
    public const string Fallback = "M3.2 8a4.8 4.8 0 1 0 9.6 0 4.8 4.8 0 1 0-9.6 0";

    private static readonly Dictionary<string, string> ById = new(StringComparer.Ordinal)
    {
        // A cat's face with whiskers (Harry: the first one looked like a monster boss).
        [EventIds.Shugo] = "M3.3 6.6 3.9 2.5 6.6 4.6Q8 4.3 9.4 4.6L12.1 2.5 12.7 6.6Q13.6 7.8 13.6 9.2C13.6 11.9 11.1 13.6 8 13.6S2.4 11.9 2.4 9.2Q2.4 7.8 3.3 6.6Z"
            + "M5.45 8.7a.45.45 0 1 0 .9 0 .45.45 0 1 0-.9 0M9.65 8.7a.45.45 0 1 0 .9 0 .45.45 0 1 0-.9 0M6.9 10.7q.55.7 1.1 0 .55.7 1.1 0"
            + "M.7 9.3h2.6M.9 11.1l2.5-.5M15.3 9.3h-2.6M15.1 11.1l-2.5-.5",
        // A crown over a bar in a diamond, like the game's invasion icon (Harry's screenshot, 2026-10-26).
        [EventIds.Invasion] = "M8 1.4 14.6 8 8 14.6 1.4 8ZM5.4 9.6V6.4L6.8 7.7 8 5.5 9.2 7.7 10.6 6.4V9.6ZM6 11.2H10",
        // A vortex, three arms turning in: the rift as the game draws it (Harry, 2026-10-26).
        [EventIds.Rift] = "M8.9 8 9.4 8.5 9.5 9.3 9.3 10.2 8.5 11 7.4 11.5 5.9 11.6 4.4 11 3.1 9.8 2.2 8 2.1 5.8"
            + "M7.6 8.8 6.9 8.9 6.1 8.7 5.5 8 5.1 7 5.2 5.7 5.9 4.4 7.2 3.4 8.9 2.9 10.9 3 12.8 4"
            + "M7.6 7.2 7.7 6.6 8.3 6.1 9.3 5.8 10.3 6 11.4 6.8 12.1 8 12.4 9.6 12 11.4 10.9 13 9.1 14.2",
        // A catapult on wheels with its stone (Harry: not a castle, which read as the weekly calendar).
        [EventIds.Siege] = "M2.9 12.6a1.5 1.5 0 1 0 3 0 1.5 1.5 0 1 0-3 0M9.9 12.6a1.5 1.5 0 1 0 3 0 1.5 1.5 0 1 0-3 0"
            + "M2.4 10.9H13.6M7 10.9 9.2 6.6 11.4 10.9M13.2 9.3 4.6 3.4M2.3 2.6a1.2 1.2 0 1 0 2.4 0 1.2 1.2 0 1 0-2.4 0",
        // The watcher's eye.
        [EventIds.Kaira] = "M1.6 8s2.4-4.1 6.4-4.1S14.4 8 14.4 8 12 12.1 8 12.1 1.6 8 1.6 8ZM6.1 8a1.9 1.9 0 1 0 3.8 0 1.9 1.9 0 1 0-3.8 0",
        // The guardian's shield.
        [EventIds.Nahma] = "M8 1.8 13 3.7v4c0 3-2.2 5.2-5 6.5-2.8-1.3-5-3.5-5-6.5v-4ZM8 4.8v6.4M5.6 7.4h4.8",
        // Two crossed blades for the two executors.
        [EventIds.Executors] = "M3.2 12.8 12.6 3.4M12.8 12.8 3.4 3.4M2.6 10.6l2.8 2.8M13.4 10.6l-2.8 2.8",
        // A turning arrow.
        [EventIds.Daily] = "M13 8a5 5 0 1 1-1.5-3.6M13.1 2.6v2.7h-2.7",
        // A calendar page.
        [EventIds.Weekly] = "M4.1 3.4h7.8a1.6 1.6 0 0 1 1.6 1.6v6.9a1.6 1.6 0 0 1-1.6 1.6H4.1a1.6 1.6 0 0 1-1.6-1.6V5a1.6 1.6 0 0 1 1.6-1.6Z"
            + "M2.5 6.6h11M5.5 2v2.6M10.5 2v2.6",
    };

    /// <summary>The drawing for <paramref name="id"/>, or <see cref="Fallback"/> for an id the catalog does not know.</summary>
    public static string For(string id) => ById.TryGetValue(id, out var path) ? path : Fallback;

    /// <summary>Whether <paramref name="id"/> has a drawing of its own.</summary>
    public static bool Has(string id) => ById.ContainsKey(id);
}
