using ScarletMeter.App.Roster;
using ScarletMeter.Engine;

namespace ScarletMeter.App.Pets;

/// <summary>
/// Checks loaded pet counts, which are untrusted (a hand edit, a damaged write, another build): drops what cannot be used and
/// repairs what can, saying what it did in sentences that name no pet and no count, since they are logged. Pure: the current
/// instant is a parameter.
/// </summary>
public static class PetNormalizer
{
    /// <summary>The most servers kept; past it, the ones changed longest ago go (the game has 42).</summary>
    public const int MaxServers = 64;

    /// <summary>The most pets kept on a server: the engine's bound on the game's list (the game has about two hundred).</summary>
    public const int MaxPets = 4096;

    /// <summary>The counts to use, and a sentence for each repair (none when they were usable as they were).</summary>
    public static (PetFile File, IReadOnlyList<string> Repairs) Normalize(PetFile? file, DateTimeOffset now)
    {
        var repairs = new List<string>();
        var dropped = 0;
        var petsDropped = 0;
        var datesReset = 0;
        List<PetServer> usable = [];
        foreach (var server in file?.Servers ?? [])
        {
            if (server is null || AbyssArtifacts.SideOf(server.ServerId) == ArtifactHolder.Unknown)
            {
                dropped++;
                continue;
            }
            var seen = new HashSet<int>();
            List<PetEntry> pets = [];
            foreach (var pet in server.Pets ?? [])
            {
                if (pet is null || pet.Id <= 0 || pet.Level is < 0 or > PetRules.MaxLevel || pet.Souls is < 0 or > PetRules.MaxSouls
                    || !seen.Add(pet.Id) || pets.Count == MaxPets)
                {
                    petsDropped++;
                    continue;
                }
                pets.Add(pet);
            }
            var synced = server.SyncedAt is { } at && IsUsableDate(at, now) ? server.SyncedAt : null;
            var updated = IsUsableDate(server.UpdatedAt, now) ? server.UpdatedAt : RosterNormalizer.Earliest;
            if (synced != server.SyncedAt || updated != server.UpdatedAt) datesReset++;
            usable.Add(server with { Pets = [.. pets.OrderBy(pet => pet.Id)], SyncedAt = synced, UpdatedAt = updated });
        }
        var unique = usable.GroupBy(server => server.ServerId).Select(g => g.OrderByDescending(server => server.UpdatedAt).First()).ToList();
        var kept = unique.OrderByDescending(server => server.UpdatedAt).Take(MaxServers).OrderBy(server => server.ServerId).ToList();
        var trimmed = usable.Count - kept.Count;

        if (dropped > 0) repairs.Add($"{Servers(dropped)} with no usable server dropped.");
        if (trimmed > 0) repairs.Add($"{Servers(trimmed)} listed twice or past the {MaxServers} kept dropped.");
        if (petsDropped > 0) repairs.Add($"{petsDropped} pet {(petsDropped == 1 ? "count" : "counts")} out of range, listed twice or past the {MaxPets} kept dropped.");
        if (datesReset > 0) repairs.Add($"{datesReset} {(datesReset == 1 ? "date" : "dates")} out of range reset.");
        return (new PetFile { Version = PetFile.CurrentVersion, Servers = kept }, repairs);
    }

    // A date the meter can have written: after AION 2's release and not past now by more than a clock set back allows.
    private static bool IsUsableDate(DateTimeOffset at, DateTimeOffset now) =>
        at >= RosterNormalizer.Earliest && at <= now + RosterNormalizer.FutureSlack;

    private static string Servers(int count) => count == 1 ? "1 server's pets" : $"{count} servers' pets";
}
