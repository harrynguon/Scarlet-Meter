using ScarletMeter.Engine;

namespace ScarletMeter.App.Pets;

/// <summary>One pet's progress: its level (0 to <see cref="PetRules.MaxLevel"/>) and its souls toward the next level.</summary>
public readonly record struct PetProgress(int Level, int Souls);

/// <summary>
/// Souls one pickup gave a pet that was still below level 3, for the overlay's notice: how many, and the pet's progress after
/// them (a level reached shows there).
/// </summary>
public sealed record PetGain(int PetId, int Amount, PetProgress Before, PetProgress After);

/// <summary>
/// How a server's pet counts follow the game's pet records, as A2Tools-Plugins' pet-souls keeps them (plugin.js at f206c25c49,
/// GPL-3.0; THIRD-PARTY-NOTICES.md), with the fixes it made from its players' reports:
/// <list type="bullet">
/// <item>The collection at login replaces every count: it is the game's own word.</item>
/// <item>Souls add to a pet's count, and a pet whose souls reach what its level needs goes up a level itself, the souls past
/// the cost staying with it (a chest's 5 on a pet at 24 of 25 left it at level 2 with 4): the game does not send a level-up
/// for every step (a pet at level 0 showed 24 souls).</item>
/// <item>A level-up the pet has not reached by its souls takes that level's cost from its souls (25 becomes 0, 29 becomes
/// 4); one it has already reached changes nothing.</item>
/// <item>A pet unlocked from a chest takes that level and keeps the souls it had.</item>
/// </list>
/// Whatever drifts is put right by the next login's collection. Pure: each method changes only the counts it is given.
/// </summary>
public static class PetRules
{
    /// <summary>A pet's highest level.</summary>
    public const int MaxLevel = 3;

    /// <summary>
    /// The most souls a pet's count may hold (the engine reads no more from the game), so a count never overflows however
    /// long a pet at level 3 is given souls.
    /// </summary>
    public const int MaxSouls = 1_000_000;

    /// <summary>The souls each level takes: 5 for level 1 (A2Tools inferred it), 25 for level 2, 75 for level 3.</summary>
    public static IReadOnlyList<int> Needed { get; } = [5, 25, 75];

    /// <summary>The souls a pet at this level needs for its next, or 0 at level 3 (or past it).</summary>
    public static int NeededAt(int level) => level is >= 0 and < MaxLevel ? Needed[level] : 0;

    /// <summary>The souls a pet still needs to reach level 3: 105 from nothing, 0 at level 3.</summary>
    public static int ToMax(PetProgress pet)
    {
        if (pet.Level >= MaxLevel) return 0;
        var left = 0;
        for (var level = Math.Max(0, pet.Level); level < MaxLevel; level++) left += Needed[level];
        return Math.Max(0, left - pet.Souls);
    }

    /// <summary>Whether a pet is at its highest level.</summary>
    public static bool IsMax(PetProgress pet) => pet.Level >= MaxLevel;

    /// <summary>
    /// Applies one record to a server's counts and returns the souls it gave pets below level 3 (only a pickup gives any).
    /// </summary>
    public static IReadOnlyList<PetGain> Apply(IDictionary<int, PetProgress> pets, PetRecord record)
    {
        ArgumentNullException.ThrowIfNull(pets);
        switch (record)
        {
            case PetCollectionRead collection:
                Replace(pets, collection);
                return [];
            case PetSoulsGained souls:
                return Gain(pets, souls);
            case PetLevelReached { Unlocked: true } unlocked:
                Unlock(pets, unlocked.PetId, unlocked.Level);
                return [];
            case PetLevelReached reached:
                Reach(pets, reached.PetId, reached.Level);
                return [];
            default:
                return [];
        }
    }

    // The collection replaces every count: each listed pet at its level, its points from the second list (a pet with points
    // and no level is at level 0).
    private static void Replace(IDictionary<int, PetProgress> pets, PetCollectionRead collection)
    {
        pets.Clear();
        foreach (var (id, level) in collection.Levels) pets[id] = new PetProgress(level, 0);
        foreach (var (id, points) in collection.Points) pets[id] = (pets.TryGetValue(id, out var had) ? had : default) with { Souls = points };
    }

    // Each pet's souls in a pickup, levelling it as its souls allow.
    private static List<PetGain> Gain(IDictionary<int, PetProgress> pets, PetSoulsGained souls)
    {
        var gains = new List<PetGain>();
        foreach (var (id, amount) in souls.Souls)
        {
            var before = pets.TryGetValue(id, out var had) ? had : default;
            var after = LevelUp(before with { Souls = (int)Math.Min((long)before.Souls + amount, MaxSouls) });
            pets[id] = after;
            if (!IsMax(before)) gains.Add(new PetGain(id, amount, before, after));
        }
        return gains;
    }

    // A pet goes up a level for each level's cost its souls cover, keeping what is past the cost.
    private static PetProgress LevelUp(PetProgress pet)
    {
        while (pet.Level is >= 0 and < MaxLevel && pet.Souls >= Needed[pet.Level])
            pet = new PetProgress(pet.Level + 1, pet.Souls - Needed[pet.Level]);
        return pet;
    }

    // A level-up the pet has not reached by its souls: the costs of the levels passed come off its souls.
    private static void Reach(IDictionary<int, PetProgress> pets, int id, int level)
    {
        var was = pets.TryGetValue(id, out var had) ? had : default;
        if (was.Level >= level) return;
        var cost = 0;
        for (var step = Math.Max(0, was.Level); step < Math.Min(level, MaxLevel); step++) cost += Needed[step];
        pets[id] = new PetProgress(level, Math.Max(0, was.Souls - cost));
    }

    // A pet from a chest at a level, with the souls it had; one already at that level or past it is left as it is.
    private static void Unlock(IDictionary<int, PetProgress> pets, int id, int level)
    {
        var was = pets.TryGetValue(id, out var had) ? had : default;
        if (was.Level >= level) return;
        pets[id] = was with { Level = level };
    }
}
