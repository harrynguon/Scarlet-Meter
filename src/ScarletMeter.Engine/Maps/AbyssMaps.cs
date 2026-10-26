namespace ScarletMeter.Engine.Maps;

/// <summary>
/// The map ids that are the Abyss, where the meter pauses (Upstream/AbyssPause). 20 is Chaotic Lower Reshanta: the map
/// Harry's game named on entering the Abyss (2026-10-26, 17:05:57 +11:00, the test copy's log), which A2Tools, AetherDPS
/// and SkeeveAN's data also give the Abyss's lower layer. 22 is Chaotic Middle Reshanta in the same three sources, not
/// yet seen in the game. AetherDPS also lists 21, which no other source names, so it is left out until it is seen.
/// None is a dungeon (those are 600000 to 699999), so a wrong id here can never pause the meter in one. The Artifact
/// Corridor (503001), a two-minute instance entered from the Abyss, is left out on purpose: the pause is for the Abyss's
/// crowds, and Harry asked for the meter to track there (2026-10-27); going back out loads 20 again, which pauses it.
/// </summary>
internal static class AbyssMaps
{
    /// <summary>Whether the map is the Abyss.</summary>
    public static bool Contains(int mapId) => mapId is 20 or 22;
}
