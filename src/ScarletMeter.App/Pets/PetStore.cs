using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ScarletMeter.App.Storage;

namespace ScarletMeter.App.Pets;

/// <summary>Loaded pet counts with what happened while loading them.</summary>
/// <param name="CanSave">False when saving would overwrite a file this build cannot fully represent or read.</param>
/// <param name="Notice">A sentence for the Pet souls window when something was repaired, recovered or cannot be saved.</param>
public sealed record PetLoadResult(PetFile File, StoredFileStatus Status, bool CanSave, string? Notice);

/// <summary>
/// Reads and writes the pet counts in the data directory: pets.json, or demo-pets.json in demo mode. Kept like the roster
/// (<see cref="ProtectedJsonFile{T}"/>: atomic writes, a backup, a damaged file kept aside, a newer one used but never
/// overwritten) and checked on load (<see cref="PetNormalizer"/>). It holds the player's progress, so nothing here logs a pet
/// or a count.
/// </summary>
public sealed class PetStore
{
    /// <summary>The counts are a few kilobytes per server; anything larger is not ours and is not parsed.</summary>
    public const long MaxFileBytes = 4 * 1024 * 1024;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ProtectedJsonFile<PetFile> file;
    private readonly ILogger<PetStore> logger;

    /// <param name="dataDirectory">The absolute data directory.</param>
    /// <param name="demo">Whether these are the demo's counts (demo-pets.json).</param>
    public PetStore(string dataDirectory, bool demo, ILogger<PetStore> logger)
    {
        if (!Path.IsPathFullyQualified(dataDirectory))
            throw new ArgumentException("The data directory must be an absolute path.", nameof(dataDirectory));
        file = new ProtectedJsonFile<PetFile>(Path.Combine(dataDirectory, demo ? "demo-pets.json" : "pets.json"), JsonOptions,
            pets => pets.Version, PetFile.CurrentVersion, MaxFileBytes, "pet souls", logger);
        this.logger = logger;
    }

    public string FilePath => file.FilePath;

    public string BackupPath => file.BackupPath;

    /// <summary>Loads the counts as they can be used at <paramref name="now"/>. Never throws for a bad or missing file.</summary>
    public PetLoadResult Load(DateTimeOffset now)
    {
        var load = file.Load();
        var kept = load.KeptAs is null ? null : Path.GetFileName(load.KeptAs);
        if (load.Error is not null && load.Outcome != StoredFileOutcome.Read)
            logger.LogWarning(load.Error, "The pet souls could not be fully loaded ({Outcome}).", load.Outcome);
        else if (load.Status == StoredFileStatus.ReadOnly)
            logger.LogWarning("The pet souls come from a newer Scarlet Meter (version {Version}); they are shown but not overwritten.", load.Version);
        var problem = StoredFileNotice.For(load.Outcome, kept, "pet souls", "the counts start again from your next login");

        var (pets, repairs) = PetNormalizer.Normalize(load.Value, now);
        foreach (var repair in repairs) logger.LogInformation("Pet souls repaired: {Repair}", repair);
        var repaired = repairs.Count == 0 ? null : "Some saved pet counts were out of range and were repaired: " + string.Join(" ", repairs);
        var notice = problem is null ? repaired : repaired is null ? problem : problem + " " + repaired;
        return new PetLoadResult(pets, load.Status, load.CanSave, notice);
    }

    /// <summary>Writes the counts atomically, keeping the previous file as the backup. Throws on I/O failure.</summary>
    public void Save(PetFile pets) => file.Save(pets);
}
