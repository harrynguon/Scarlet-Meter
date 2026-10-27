namespace ScarletMeter.Engine.Entries;

/// <summary>
/// The game servers that exist, by id, to tell a server id a record states from a misread one, as A2Tools' DPS Meter does
/// (<c>exists</c> in its src-tauri/src/capture/servers.rs at 5c6f02c; THIRD-PARTY-NOTICES.md). An id is faction (<c>1</c>
/// Elyos, <c>2</c> Asmodian), then region, then slot. Korea and Taiwan are region <c>0</c>, slots 1 to 58 (1001 to 1058 and
/// 2001 to 2058); Global's regions are <c>1</c> NA East to <c>5</c> Asia, with the slots NC has opened in each (NC's notice
/// "New Server and Matchmaking Information", as A2Tools read it on 2026-10-27): 106 servers, the same slots for both
/// factions. Upstream's own list (its ServerMap) has only Korea's first 21 a side, so it names no Global server. When NC
/// opens servers, add their slots here. Pure.
/// </summary>
internal static class GameServers
{
    /// <summary>Korea's and Taiwan's slots a side.</summary>
    public const int KoreaTaiwanSlots = 58;

    // The open slots of each Global region, by its digit: 1 NA East, 2 NA West, 3 Europe, 4 South America, 5 Asia.
    private static readonly int[] GlobalSlots = [0, 9, 5, 23, 6, 10];

    /// <summary>Whether <paramref name="id"/> is a game server that exists.</summary>
    public static bool Exists(int id)
    {
        if (id / 1000 is not (1 or 2)) return false;
        var region = id / 100 % 10;
        var slot = id % 100;
        if (slot == 0) return false;
        return region == 0 ? slot <= KoreaTaiwanSlots : region < GlobalSlots.Length && slot <= GlobalSlots[region];
    }
}
