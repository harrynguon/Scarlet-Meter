using ScarletMeter.App.Pets;
using ScarletMeter.Engine;

namespace ScarletMeter.Tests;

/// <summary>
/// How pet counts follow the game's records (PetRules, as A2Tools-Plugins' pet-souls keeps them, with the fixes it made from
/// its players' reports), what the Pet souls window lists (PetListing), its words (PetText) and the catalog (PetCatalog).
/// Pure: no engine, no file.
/// </summary>
public sealed class PetRulesTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 8, 5, 0, 0, TimeSpan.Zero);
    private const int Sparkie = 1006;
    private const int Potcrab = 1007;

    private static Dictionary<int, PetProgress> Pets(params (int Id, int Level, int Souls)[] pets) =>
        pets.ToDictionary(p => p.Id, p => new PetProgress(p.Level, p.Souls));

    private static PetSoulsGained Souls(params (int Pet, int Amount)[] souls) => new([.. souls.Select(s => new PetSouls(s.Pet, s.Amount))], At);

    // The costs: 5, 25, 75, so 105 from nothing to level 3.
    [Fact]
    public void EachLevelHasItsCost()
    {
        Assert.Equal([5, 25, 75], PetRules.Needed);
        Assert.Equal((5, 25, 75, 0), (PetRules.NeededAt(0), PetRules.NeededAt(1), PetRules.NeededAt(2), PetRules.NeededAt(3)));
        Assert.Equal(105, PetRules.ToMax(default));
        Assert.Equal(75 - 41, PetRules.ToMax(new PetProgress(2, 41)));
        Assert.Equal(25 - 24 + 75, PetRules.ToMax(new PetProgress(1, 24)));
        Assert.Equal(0, PetRules.ToMax(new PetProgress(3, 40)));
        // A count past its level's cost (a login's word before it levels) needs no fewer than none.
        Assert.Equal(0, PetRules.ToMax(new PetProgress(2, 80)));
    }

    // The collection replaces every count: a pet with points but no level is at 0, a pet listed with a level and no points
    // has none, and a pet counted before but missing from it goes.
    [Fact]
    public void TheCollectionReplacesEveryCount()
    {
        var pets = Pets((Potcrab, 1, 3), (1099, 2, 2));
        var gains = PetRules.Apply(pets, new PetCollectionRead([new PetLevel(Sparkie, 2), new PetLevel(Potcrab, 3)], [new PetSouls(Sparkie, 41), new PetSouls(1043, 4)], At));
        Assert.Empty(gains);
        Assert.Equal(Pets((Sparkie, 2, 41), (Potcrab, 3, 0), (1043, 0, 4)), pets);
    }

    // A soul adds to a pet's count, and makes a notice's gain with the count before and after.
    [Fact]
    public void ASoulAddsToItsPet()
    {
        var pets = Pets((Sparkie, 2, 40));
        var gain = Assert.Single(PetRules.Apply(pets, Souls((Sparkie, 1))));
        Assert.Equal(new PetGain(Sparkie, 1, new PetProgress(2, 40), new PetProgress(2, 41)), gain);
        Assert.Equal(new PetProgress(2, 41), pets[Sparkie]);
    }

    // The souls that cover a level's cost level the pet at once, its count starting again from what is past the cost
    // (Karnif: 25 becomes 0; a chest's 5 on a pet at 24 of 25 left it at level 2 with 4, 2026-11-05).
    [Fact]
    public void SoulsThatCoverALevelsCostLevelThePetKeepingWhatIsPast()
    {
        var pets = Pets((Sparkie, 1, 24), (Potcrab, 1, 24));
        PetRules.Apply(pets, Souls((Sparkie, 1), (Potcrab, 5)));
        Assert.Equal(new PetProgress(2, 0), pets[Sparkie]);
        Assert.Equal(new PetProgress(2, 4), pets[Potcrab]);
    }

    // A pet never seen starts at level 0, and enough souls at once pass several levels.
    [Fact]
    public void ManySoulsAtOncePassSeveralLevels()
    {
        var pets = Pets();
        var gain = Assert.Single(PetRules.Apply(pets, Souls((Sparkie, 31))));
        Assert.Equal(new PetProgress(2, 1), pets[Sparkie]);
        Assert.Equal((default(PetProgress), new PetProgress(2, 1)), (gain.Before, gain.After));
    }

    // At level 3 a soul still counts, but it is no gain for a notice; the soul that brings a pet to level 3 is one.
    [Fact]
    public void ASoulForAPetAtLevel3IsNoGain()
    {
        var pets = Pets((Sparkie, 2, 74), (Potcrab, 3, 0));
        var gains = PetRules.Apply(pets, Souls((Sparkie, 1), (Potcrab, 1)));
        var gain = Assert.Single(gains);
        Assert.Equal((Sparkie, new PetProgress(3, 0)), (gain.PetId, gain.After));
        Assert.Equal(new PetProgress(3, 1), pets[Potcrab]);
    }

    // A count never grows past its bound, however long a pet at level 3 is given souls.
    [Fact]
    public void ACountStopsAtItsBound()
    {
        var pets = Pets((Sparkie, 3, PetRules.MaxSouls - 1));
        PetRules.Apply(pets, Souls((Sparkie, 5)));
        Assert.Equal(new PetProgress(3, PetRules.MaxSouls), pets[Sparkie]);
    }

    // A level-up the souls had not reached takes the levels' costs from the souls (25 becomes 0, 29 becomes 4); one already
    // reached by the souls changes nothing, so the souls picked up since stay, and a late one for a level the pet has passed
    // never takes it back down (the fail-first run found that case untested).
    [Fact]
    public void ALevelUpTakesItsCostUnlessTheSoulsGotThereFirst()
    {
        var pets = Pets((Sparkie, 1, 29), (Potcrab, 2, 3), (1008, 0, 2), (1009, 3, 1));
        PetRules.Apply(pets, new PetLevelReached(Sparkie, 2, false, At));
        PetRules.Apply(pets, new PetLevelReached(Potcrab, 2, false, At));
        PetRules.Apply(pets, new PetLevelReached(1008, 2, false, At));
        PetRules.Apply(pets, new PetLevelReached(1009, 2, false, At));
        Assert.Equal(new PetProgress(2, 4), pets[Sparkie]);
        Assert.Equal(new PetProgress(2, 3), pets[Potcrab]);
        Assert.Equal(new PetProgress(2, 0), pets[1008]);
        Assert.Equal(new PetProgress(3, 1), pets[1009]);
    }

    // A pet unlocked from a chest takes its level and keeps its souls (one at 2 kept them, 2026-11-05); one already there or
    // past it is left as it is.
    [Fact]
    public void AnUnlockedPetTakesItsLevelAndKeepsItsSouls()
    {
        var pets = Pets((Sparkie, 0, 4), (Potcrab, 3, 2));
        PetRules.Apply(pets, new PetLevelReached(Sparkie, 2, true, At));
        PetRules.Apply(pets, new PetLevelReached(Potcrab, 1, true, At));
        PetRules.Apply(pets, new PetLevelReached(1050, 1, true, At));
        Assert.Equal(new PetProgress(2, 4), pets[Sparkie]);
        Assert.Equal(new PetProgress(3, 2), pets[Potcrab]);
        Assert.Equal(new PetProgress(1, 0), pets[1050]);
    }

    // ---------- The catalog ----------

    // 207 pets, once each: 162 dropped by monsters, 35 from chests only, 7 Special, 3 unreleased (A2Tools' pet-souls at
    // f206c25c49); Sparkie is a Fera dropped by monsters, and a pet the catalog does not know is named by its id.
    [Fact]
    public void TheCatalogHasEveryPetOnce()
    {
        Assert.Equal(207, PetCatalog.All.Count);
        Assert.Equal(207, PetCatalog.All.Select(pet => pet.Id).Distinct().Count());
        Assert.Equal((162, 35, 7, 3), (
            PetCatalog.All.Count(pet => pet.Kind == PetKind.Farmed), PetCatalog.All.Count(pet => pet.Kind == PetKind.ChestOnly),
            PetCatalog.All.Count(pet => pet.Kind == PetKind.Special), PetCatalog.All.Count(pet => pet.Kind == PetKind.Unreleased)));
        Assert.All(PetCatalog.All, pet => Assert.True((pet.Kind == PetKind.Special) == (pet.Genus == PetGenus.Special), pet.Name));
        Assert.All(PetCatalog.All, pet => Assert.False(string.IsNullOrWhiteSpace(pet.Name)));
        Assert.Equal(new PetInfo(Sparkie, "Sparkie", PetGenus.Fera, PetKind.Farmed), PetCatalog.Find(Sparkie));
        Assert.Null(PetCatalog.Find(1500));
        Assert.Equal(new PetInfo(1500, "Pet 1500", PetGenus.Unknown, PetKind.Farmed), PetCatalog.Describe(1500));
    }

    // ---------- What the window lists ----------

    private static PetRow Row(int id, int level, int souls, int session = 0, bool counted = true) =>
        new(PetCatalog.Describe(id), new PetProgress(level, souls), counted, session);

    // Special pets never, unreleased ones only once the game has sent one, chest-only ones and level-3 ones when asked; a
    // pet the catalog does not know is listed.
    [Fact]
    public void TheWindowListsThePetsThatCanBeRaised()
    {
        var chestOnly = PetCatalog.All.First(pet => pet.Kind == PetKind.ChestOnly).Id;
        var unreleased = PetCatalog.All.First(pet => pet.Kind == PetKind.Unreleased).Id;
        IReadOnlyList<PetRow> pets =
        [
            Row(Sparkie, 2, 41), Row(Potcrab, 3, 0), Row(1001, 3, 0), Row(chestOnly, 1, 2),
            Row(unreleased, 0, 0, counted: false), Row(1500, 1, 1),
        ];
        Assert.Equal([Sparkie, 1500], PetListing.Rows(pets, false, false, null, PetSort.Name).Select(p => p.Info.Id).Order());
        Assert.Equal(new[] { chestOnly, 1500, Potcrab, Sparkie }.Order(), PetListing.Rows(pets, true, true, null, PetSort.Name).Select(p => p.Info.Id).Order());
        var sent = pets.Select(p => p.Info.Id == unreleased ? p with { Counted = true } : p).ToList();
        Assert.Contains(PetListing.Rows(sent, false, false, null, PetSort.Name), p => p.Info.Id == unreleased);

        Assert.Equal(new PetTotals(3, 1, 34 + 25 - 1 + 75), PetListing.Totals(pets, false));
        Assert.Equal(4, PetListing.Totals(pets, true).Pets);
    }

    // The search keeps the names that hold it, whatever the case, or the pet with that id.
    [Fact]
    public void TheSearchMatchesANameOrAnId()
    {
        IReadOnlyList<PetRow> pets = [Row(Sparkie, 2, 41), Row(Potcrab, 1, 0)];
        Assert.Equal([Sparkie], PetListing.Rows(pets, true, true, " spark ", PetSort.Name).Select(p => p.Info.Id));
        Assert.Equal([Potcrab], PetListing.Rows(pets, true, true, "1007", PetSort.Name).Select(p => p.Info.Id));
        Assert.Empty(PetListing.Rows(pets, true, true, "dragon", PetSort.Name));
    }

    // Each order: nearest level 3 first (level-3 pets last), most souls this run, highest level, by name.
    [Fact]
    public void EachOrderSortsAsItSays()
    {
        IReadOnlyList<PetRow> pets = [Row(Sparkie, 2, 41, session: 1), Row(Potcrab, 2, 70), Row(1008, 1, 0, session: 4), Row(1002, 3, 0)];
        Assert.Equal([Potcrab, Sparkie, 1008, 1002], PetListing.Rows(pets, true, true, null, PetSort.ToMax).Select(p => p.Info.Id));
        Assert.Equal([1008, Sparkie, 1002, Potcrab], PetListing.Rows(pets, true, true, null, PetSort.Session).Select(p => p.Info.Id));
        Assert.Equal([1002, Potcrab, Sparkie, 1008], PetListing.Rows(pets, true, true, null, PetSort.Level).Select(p => p.Info.Id));
        Assert.Equal(["Baby Odyle Spider", "Krall Laborer", "Potcrab", "Sparkie"], PetListing.Rows(pets, true, true, null, PetSort.Name).Select(p => p.Info.Name));
    }

    // ---------- Words ----------

    [Fact]
    public void TheNoticeSaysTheSoulsAndTheProgress()
    {
        var sparkie = PetCatalog.Describe(Sparkie);
        Assert.Equal("41/75", PetText.Progress(new PetProgress(2, 41)));
        Assert.Equal("max", PetText.Progress(new PetProgress(3, 0)));
        Assert.Equal("Lv 2", PetText.Level(new PetProgress(2, 41)));
        Assert.Equal("+3", PetText.Gained(3));
        Assert.Equal("Sparkie: 1 soul, level 2, 41 of 75 souls", PetText.Notice(new PetNotice(sparkie, 1, new PetProgress(2, 41), At)));
        Assert.Equal("Sparkie: 2 souls, level 3 reached", PetText.Notice(new PetNotice(sparkie, 2, new PetProgress(3, 0), At)));
        Assert.Equal(string.Empty, PetText.Genus(PetGenus.Unknown));
        Assert.Equal("genus-fera", PetText.GenusClass(PetGenus.Fera));
    }
}
