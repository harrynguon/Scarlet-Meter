using System.Collections.Frozen;

namespace ScarletMeter.Engine.Maps;

/// <summary>
/// English names of the game's maps by the id its map-loaded record sends (<see cref="ICombatEngine.MapId"/>), for the
/// player to read; nothing acts on them. Two parts from other projects' published data (THIRD-PARTY-NOTICES.md,
/// "Data"), and one map seen in the game; AION 2's names belong to NCSOFT:
/// <list type="bullet">
/// <item>The open world and the Abyss: the names SkeeveAN/Aion-DPS-Meter gives each map of the game's field-boss list
/// (by its internal name, World_L_A and so on), with the ids yykk62/AetherDPS gives those internal names. The
/// field-boss list's id for Verteron, 1010, is also what the map-loaded record sends there (A2Tools' capture), and
/// A2Tools' own records put the Abyss's bosses and artifacts on 20 and 22; the map-loaded record sent 20 on entering
/// the Abyss in Harry's game (2026-10-26), and 22 is not yet seen there.</item>
/// <item>Dungeons: taengu/A2Tools-DPS-Meter's list (an instance's map id is its dungeon id), with its difficulty in
/// brackets where the list gives one.</item>
/// <item>The Artifact Corridor, 503001: a two-minute instance entered from the Abyss, where mobs give Abyss points. Its
/// id is the one the map-loaded record sent in Harry's game (2026-10-26, 23:47:26 +11:00, between two loads of the
/// Abyss's 20 on the same connection), its name Harry's for it; no source lists it.</item>
/// </list>
/// Cities and most of the open world are in none of them, so they have no name here.
/// </summary>
public static class MapNames
{
    /// <summary>
    /// The Artifact Corridor's map id, 503001 (seen in Harry's game, see above): the character roster counts the times a
    /// character enters it.
    /// </summary>
    public const int ArtifactCorridor = 503001;

    private static readonly FrozenDictionary<int, string> Names = new Dictionary<int, string>
    {
        // The open world and the Abyss (SkeeveAN's names, AetherDPS's ids).
        [1010] = "Verteron",
        [1011] = "Eltnen",
        [1110] = "Altgard",
        [1111] = "Morheim",
        [20] = "Chaotic Lower Reshanta",
        [22] = "Chaotic Middle Reshanta",
        // Seen in Harry's game, named by Harry: entered from the Abyss, and not the Abyss for its pause (AbyssMaps).
        [ArtifactCorridor] = "Artifact Corridor",
        // Dungeons (A2Tools' list; "the Hall of Illusion" capitalised).
        [600001] = "Krao Cave (Normal)",
        [600002] = "Krao Cave (Exploration)",
        [600003] = "Krao Cave",
        [600004] = "Krao Cave (Hard)",
        [600011] = "Urugugu Canyon",
        [600012] = "Urugugu Canyon",
        [600013] = "Urugugu Canyon",
        [600014] = "Urugugu Canyon",
        [600021] = "Fire Temple",
        [600022] = "Fire Temple",
        [600023] = "Fire Temple",
        [600024] = "Fire Temple",
        [600025] = "Fire Temple (Normal)",
        [600031] = "Draupnir",
        [600032] = "Draupnir",
        [600033] = "Draupnir",
        [600034] = "Draupnir",
        [600041] = "Dying Dramata's Nest",
        [600042] = "Dying Dramata's Nest",
        [600051] = "Deus Research Base",
        [600052] = "Deus Research Base",
        [600053] = "Deus Research Base",
        [600054] = "Deus Research Base",
        [600055] = "Deus Research Base",
        [600056] = "Deus Research Base",
        [600057] = "Deus Research Base",
        [600058] = "Deus Research Base",
        [600059] = "Deus Research Base",
        [600061] = "Shattered Arkanis",
        [600062] = "Shattered Arkanis",
        [600063] = "Shattered Arkanis",
        [600064] = "Shattered Arkanis",
        [600065] = "Shattered Arkanis",
        [600066] = "Shattered Arkanis",
        [600067] = "Shattered Arkanis",
        [600068] = "Shattered Arkanis",
        [600069] = "Shattered Arkanis",
        [600071] = "Vakron Sky Island",
        [600072] = "Vakron Sky Island",
        [600073] = "Vakron Sky Island",
        [600074] = "Vakron Sky Island (Normal)",
        [600091] = "Ferocious Horn Den (Exploration)",
        [600092] = "Ferocious Horn Den (Normal)",
        [600093] = "Ferocious Horn Den (Hard)",
        [600121] = "Cradle of Nihility (Exploration)",
        [600122] = "Cradle of Nihility (Normal)",
        [600123] = "Cradle of Nihility (Hard)",
        [600131] = "The Hall of Illusion (Exploration)",
        [600132] = "The Hall of Illusion (Normal)",
        [600133] = "The Hall of Illusion (Hard)",
        [600141] = "Azure Breath Island (Exploration)",
        [600142] = "Azure Breath Island (Normal)",
        [600143] = "Azure Breath Island (Hard)",
        [600151] = "Citadel of the Fallen Daeva (Exploration)",
        [600152] = "Citadel of the Fallen Daeva (Normal)",
        [600153] = "Citadel of the Fallen Daeva (Hard)",
        [600161] = "Consumed Deus Research Base (Exploration)",
        [600162] = "Consumed Deus Research Base (Normal)",
        [600163] = "Consumed Deus Research Base (Hard)",
        [610044] = "Dying Dramata's Nest (Hard)",
    }.ToFrozenDictionary();

    /// <summary>The map's name, or null when no source names it (a city, most of the open world, a new map).</summary>
    public static string? Of(int mapId) => Names.GetValueOrDefault(mapId);

    /// <summary>Every named map id, for tests.</summary>
    internal static IEnumerable<int> Ids => Names.Keys;
}
