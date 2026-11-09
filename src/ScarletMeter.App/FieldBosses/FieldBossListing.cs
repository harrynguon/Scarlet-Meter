using ScarletMeter.WorldEvents.FieldBosses;

namespace ScarletMeter.App.FieldBosses;

/// <summary>
/// The order of the Field bosses window's rows (Harry, 2026-11-09: "make the pinned bosses be automatically arranged to the
/// top of the list"): the bosses with a bell first, then the rest, each in the order given (the catalog's). A row moves only
/// when its bell is rung or silenced, never as the times tick.
/// </summary>
public static class FieldBossListing
{
    /// <summary><paramref name="bosses"/> with those in <paramref name="chosen"/> (world-map ids) first, each group in its given order.</summary>
    public static IReadOnlyList<FieldBoss> Ordered(IEnumerable<FieldBoss> bosses, IReadOnlySet<int> chosen)
    {
        ArgumentNullException.ThrowIfNull(bosses);
        ArgumentNullException.ThrowIfNull(chosen);
        var all = bosses.ToList();
        return [.. all.Where(boss => chosen.Contains(boss.Id)), .. all.Where(boss => !chosen.Contains(boss.Id))];
    }

    /// <summary>
    /// Whether the row at <paramref name="index"/> in <paramref name="ordered"/> is the last belled boss with an unbelled one
    /// after it: a line goes under it, so the belled bosses read as a group.
    /// </summary>
    public static bool EndsChosen(IReadOnlyList<FieldBoss> ordered, int index, IReadOnlySet<int> chosen)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        ArgumentNullException.ThrowIfNull(chosen);
        return index >= 0 && index + 1 < ordered.Count && chosen.Contains(ordered[index].Id) && !chosen.Contains(ordered[index + 1].Id);
    }
}
