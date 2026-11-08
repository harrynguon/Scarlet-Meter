namespace ScarletMeter.App.Pets;

/// <summary>How the Pet souls window orders its pets.</summary>
public enum PetSort
{
    /// <summary>Fewest souls still needed to reach level 3 first (the default: the pets nearest done).</summary>
    ToMax,
    /// <summary>Most souls picked up this run first: the pets being farmed.</summary>
    Session,
    /// <summary>Highest level first, then most souls.</summary>
    Level,
    /// <summary>By name.</summary>
    Name,
}

/// <summary>
/// The window's totals over the pets that can be raised (not Special, not unreleased; chest-only ones only when listed):
/// how many there are, how many are at level 3, and the souls the rest still need.
/// </summary>
public sealed record PetTotals(int Pets, int AtMax, int SoulsToGo);

/// <summary>
/// Which pets the Pet souls window lists and in what order. Special pets come at level 3 and are never listed, nor are
/// unreleased ones the game has never sent; level-3 pets and chest-only ones are listed when the player asks
/// (<see cref="Settings.PetSettings"/>). A search keeps the pets whose name holds it, or whose id it is. Pure.
/// </summary>
public static class PetListing
{
    /// <summary>The pets to list, filtered and in order.</summary>
    public static IReadOnlyList<PetRow> Rows(IReadOnlyList<PetRow> pets, bool showMaxed, bool showChestOnly, string? search, PetSort sort)
    {
        var term = search?.Trim() ?? string.Empty;
        var listed = pets.Where(pet => IsRaisable(pet, showChestOnly) && (showMaxed || !PetRules.IsMax(pet.Progress)) && Matches(pet, term));
        return [.. sort switch
        {
            PetSort.Session => listed.OrderByDescending(pet => pet.Session).ThenBy(pet => PetRules.ToMax(pet.Progress)).ThenBy(pet => pet.Info.Name, StringComparer.CurrentCultureIgnoreCase),
            PetSort.Level => listed.OrderByDescending(pet => pet.Progress.Level).ThenByDescending(pet => pet.Progress.Souls).ThenBy(pet => pet.Info.Name, StringComparer.CurrentCultureIgnoreCase),
            PetSort.Name => listed.OrderBy(pet => pet.Info.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(pet => pet.Info.Id),
            _ => listed.OrderBy(pet => PetRules.IsMax(pet.Progress)).ThenBy(pet => PetRules.ToMax(pet.Progress)).ThenBy(pet => pet.Info.Name, StringComparer.CurrentCultureIgnoreCase),
        }];
    }

    /// <summary>The totals over the pets that can be raised, whatever the search and the level-3 filter.</summary>
    public static PetTotals Totals(IReadOnlyList<PetRow> pets, bool showChestOnly)
    {
        var raisable = pets.Where(pet => IsRaisable(pet, showChestOnly)).ToList();
        return new PetTotals(raisable.Count, raisable.Count(pet => PetRules.IsMax(pet.Progress)), raisable.Sum(pet => PetRules.ToMax(pet.Progress)));
    }

    // A pet with souls to gather: not Special; not unreleased unless the game has sent it; chest-only only when asked.
    private static bool IsRaisable(PetRow pet, bool showChestOnly) => pet.Info.Kind switch
    {
        PetKind.Special => false,
        PetKind.Unreleased => pet.Counted,
        PetKind.ChestOnly => showChestOnly,
        _ => true,
    };

    private static bool Matches(PetRow pet, string term) =>
        term.Length == 0
        || pet.Info.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)
        || (int.TryParse(term, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) && id == pet.Info.Id);
}
