namespace ScarletMeter.WorldEvents.FieldBosses;

/// <summary>
/// A respawning field boss: the game's world-map boss id (its WorldMapFieldNamed id, which the world map's boss list
/// names it by: 101021 is Eternal Gartua in Verteron), the NPC code its spawn names it by (which shared timer feeds use),
/// its English name, and the id of the map it is on (<see cref="FieldBossMaps"/>).
/// </summary>
public sealed record FieldBoss(int Id, int NpcCode, string Name, int MapId);

/// <summary>The maps whose world-map boss list the game sends: Verteron, Altgard and the Abyss's lower layer.</summary>
public static class FieldBossMaps
{
    /// <summary>Verteron, the Elyos home map (World_L_A).</summary>
    public const int Verteron = 1010;

    /// <summary>Altgard, the Asmodian home map (World_D_A).</summary>
    public const int Altgard = 1110;

    /// <summary>Chaotic Lower Reshanta, the Abyss's lower layer.</summary>
    public const int AbyssLower = 20;

    /// <summary>The maps in display order.</summary>
    public static IReadOnlyList<int> All { get; } = [Verteron, Altgard, AbyssLower];

    /// <summary>A map's name for display, or null for a map that is none of them.</summary>
    public static string? NameOf(int mapId) => mapId switch
    {
        Verteron => "Verteron",
        Altgard => "Altgard",
        AbyssLower => "Abyss (lower)",
        _ => null,
    };

    /// <summary>
    /// The home map of a server's side, by the server id's first digit (1 Elyos, 2 Asmodian, as the game numbers servers),
    /// or null for an id that is neither.
    /// </summary>
    public static int? HomeOf(int serverId) => (serverId / 1000) switch
    {
        1 => Verteron,
        2 => Altgard,
        _ => null,
    };
}

/// <summary>
/// The respawning field bosses on the maps the game lists them for, from the game's tables as taengu/A2Tools-DPS-Meter
/// lists them (<c>src-tauri/src/capture/field_bosses.rs</c> at 396cfd5d4a, GPL-3.0; THIRD-PARTY-NOTICES.md): the
/// world-map id, the NPC code and the English name. The map is where the id's range puts it (101001 and up Verteron,
/// 111001 and up Altgard, 2001 to 2008 the Abyss). Pure.
/// </summary>
public static class FieldBossCatalog
{
    /// <summary>Every field boss, by map (Verteron, Altgard, the Abyss) and then by world-map id.</summary>
    public static IReadOnlyList<FieldBoss> All { get; } = Build();

    private static readonly Dictionary<int, FieldBoss> ById = All.ToDictionary(boss => boss.Id);
    private static readonly Dictionary<int, FieldBoss> ByNpcCode = All.ToDictionary(boss => boss.NpcCode);

    /// <summary>The boss with this world-map id, or null.</summary>
    public static FieldBoss? Find(int id) => ById.GetValueOrDefault(id);

    /// <summary>The boss whose spawn has this NPC code, or null.</summary>
    public static FieldBoss? FindByNpcCode(int npcCode) => ByNpcCode.GetValueOrDefault(npcCode);

    /// <summary>The bosses on one map, in the catalog's order.</summary>
    public static IReadOnlyList<FieldBoss> OnMap(int mapId) => [.. All.Where(boss => boss.MapId == mapId)];

    // (world-map id, NPC code, name), as A2Tools lists them by NPC code; sorted here by map and world-map id.
    private static IReadOnlyList<FieldBoss> Build()
    {
        (int Id, int Npc, string Name)[] verteron =
        [
            (101001, 2100003, "Neikel of the East"),
            (101002, 2100040, "Rotten Kutar"),
            (101003, 2100050, "Kernon of the West"),
            (101004, 2100076, "Kusan the Mad Gladiator"),
            (101005, 2100077, "Ritualist Garshim"),
            (101006, 2100079, "Bodyguard Teegant"),
            (101007, 2100141, "Blooming Korin"),
            (101008, 2100177, "Furious Saursus"),
            (101009, 2100178, "Bloodfang Pnyn"),
            (101010, 2100582, "Heretic Layla"),
            (101011, 2100617, "Black Tentacle Lawa"),
            (101012, 2100661, "Phantasm Kasia"),
            (101013, 2100708, "Centurion Demiros"),
            (101014, 2100718, "Divine Ansas"),
            (101015, 2100876, "Harvest Manager Moshav"),
            (101016, 2100877, "Sentinel K'nash"),
            (101017, 2100988, "Scholar Aulla"),
            (101018, 2100989, "Forest Warrior Aullamu"),
            (101019, 2100991, "Chaser Taulo"),
            (101020, 2101016, "Researcher Setram"),
            (101021, 2101074, "Eternal Gartua"),
            (101022, 2101120, "Silent Dartan"),
            (101023, 2101122, "Soul Ruler Kashapa"),
            (101024, 2101131, "High Commander Lagta"),
        ];
        (int Id, int Npc, string Name)[] altgard =
        [
            (111001, 2400017, "Melted Danar"),
            (111002, 2400074, "Black Warrior Aed"),
            (111003, 2400140, "Faithful Rajit"),
            (111004, 2400141, "Berserker Vargor"),
            (111005, 2400212, "Predator Garsan"),
            (111006, 2400223, "Blood Warrior Lannar"),
            (111007, 2400274, "Deceiver Trid"),
            (111008, 2400335, "Blue Wave Kelpina"),
            (111009, 2400353, "High Overseer Nutah"),
            (111010, 2400358, "Advisor Resana"),
            (111011, 2400419, "Special Operations Leader Linx"),
            (111012, 2400424, "Desecrator Newbold"),
            (111013, 2400425, "Specter Archon Axios"),
            (111014, 2400474, "Addicted Hardirun"),
            (111015, 2400504, "Executioner Barthien"),
            (111016, 2400593, "Drakan Battalion Weapon Guruta"),
            (111017, 2400607, "Veteran Shujakan"),
            (111018, 2400608, "Visionary Karuka"),
            (111019, 2400659, "Dark Shadow Vishwada"),
            (111020, 2400709, "Sharp Shylak"),
            (111021, 2400800, "Immortal Gartua"),
            (111022, 2400853, "High Commander Lagta"),
            (111023, 2400854, "Soul Ruler Kashapa"),
            (111024, 2400855, "Silent Dartan"),
        ];
        // The Abyss's bosses: all but Argo, the Spirit King keep a fixed schedule (the world events' Kaira, Nahma and the
        // Executors); the map lists them all the same.
        (int Id, int Npc, string Name)[] abyss =
        [
            (2001, 2600068, "Argo, the Spirit King"),
            (2002, 2600089, "Watcher Kaira"),
            (2003, 2600084, "Guardian Lord Nahma"),
            (2004, 2600093, "Guardian Lord Nahma"),
            (2005, 2600094, "Guardian Lord Nahma"),
            (2006, 2600096, "Executor Tamasa"),
            (2007, 2600097, "Executor Argo"),
            (2008, 2600098, "Executor Kaira"),
        ];
        return
        [
            .. verteron.Select(b => new FieldBoss(b.Id, b.Npc, b.Name, FieldBossMaps.Verteron)),
            .. altgard.Select(b => new FieldBoss(b.Id, b.Npc, b.Name, FieldBossMaps.Altgard)),
            .. abyss.Select(b => new FieldBoss(b.Id, b.Npc, b.Name, FieldBossMaps.AbyssLower)),
        ];
    }
}
