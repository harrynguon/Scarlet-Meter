using System.Globalization;
using ScarletMeter.App.Formatting;

namespace ScarletMeter.App.Pets;

/// <summary>The words the pet notices and the Pet souls window use.</summary>
public static class PetText
{
    /// <summary>A pet's souls toward its next level, "41/75", or "max" at level 3.</summary>
    public static string Progress(PetProgress pet) =>
        PetRules.IsMax(pet) ? "max" : $"{pet.Souls.ToString(CultureInfo.InvariantCulture)}/{PetRules.NeededAt(pet.Level).ToString(CultureInfo.InvariantCulture)}";

    /// <summary>A pet's level, "Lv 2".</summary>
    public static string Level(PetProgress pet) => $"Lv {pet.Level.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The souls a notice gathered, "+1".</summary>
    public static string Gained(int amount) => $"+{amount.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// What a soul notice says for screen readers and its tip: "Sparkie: 1 soul, level 2, 41 of 75 souls", or "Sparkie: 1 soul,
    /// level 3 reached".
    /// </summary>
    public static string Notice(PetNotice notice)
    {
        var souls = notice.Amount == 1 ? "1 soul" : $"{notice.Amount.ToString(CultureInfo.InvariantCulture)} souls";
        var progress = PetRules.IsMax(notice.Progress)
            ? "level 3 reached"
            : $"level {notice.Progress.Level.ToString(CultureInfo.InvariantCulture)}, {notice.Progress.Souls.ToString(CultureInfo.InvariantCulture)} of {PetRules.NeededAt(notice.Progress.Level).ToString(CultureInfo.InvariantCulture)} souls";
        return $"{notice.Pet.Name}: {souls}, {progress}";
    }

    /// <summary>
    /// Where the counts come from: the game's collection at the last login seen ("… at your login of 8 Nov 9:05pm …"), or,
    /// when none was seen, the pickups alone.
    /// </summary>
    public static string Synced(DateTimeOffset? syncedAt) => syncedAt is { } at
        ? $"from the game at your login of {at.ToLocalTime().ToString("d MMM", CultureInfo.InvariantCulture)} {ClockText.Time(at.ToLocalTime().DateTime)}, and each soul since"
        : "from the souls picked up with the meter running only: log in (or relog) with it running and the game sends every count";

    /// <summary>A genus for display ("Fera"), or empty for a pet the catalog does not know.</summary>
    public static string Genus(PetGenus genus) => genus == PetGenus.Unknown ? string.Empty : genus.ToString();

    /// <summary>The CSS class of a genus's colour.</summary>
    public static string GenusClass(PetGenus genus) => "genus-" + genus.ToString().ToLowerInvariant();
}
