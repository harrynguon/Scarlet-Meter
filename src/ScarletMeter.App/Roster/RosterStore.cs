using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ScarletMeter.App.Storage;
using ScarletMeter.Engine;

namespace ScarletMeter.App.Roster;

/// <summary>A loaded roster with what happened while loading it.</summary>
/// <param name="CanSave">False when saving would overwrite a file this build cannot fully represent or read.</param>
/// <param name="Notice">A sentence for the roster page when something was repaired, recovered or cannot be saved.</param>
public sealed record RosterLoadResult(RosterFile File, StoredFileStatus Status, bool CanSave, string? Notice);

/// <summary>
/// Reads and writes the character roster in the data directory: roster.json, or demo-roster.json in demo mode so made-up
/// characters never mix with the player's. It is kept like settings.json (<see cref="ProtectedJsonFile{T}"/>: atomic
/// writes, a backup, a damaged file kept aside, a newer one used but never overwritten) and checked on load
/// (<see cref="RosterNormalizer"/>). It holds the player's progress, so nothing here logs a name or a count.
/// </summary>
public sealed class RosterStore
{
    /// <summary>A roster is a few kilobytes; anything larger is not ours and is not parsed.</summary>
    public const long MaxFileBytes = 4 * 1024 * 1024;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // An unknown cap or artifact holder (a later build's) reads as unknown and is reset by the normalizer, not taken as
        // a damaged file.
        Converters = { new LenientEnumConverter<OdyleCapChoice>(), new LenientEnumConverter<ArtifactHolder>() },
    };

    private readonly ProtectedJsonFile<RosterFile> file;
    private readonly ILogger<RosterStore> logger;

    /// <param name="dataDirectory">The absolute data directory.</param>
    /// <param name="demo">Whether this is the demo's roster (demo-roster.json).</param>
    public RosterStore(string dataDirectory, bool demo, ILogger<RosterStore> logger)
    {
        if (!Path.IsPathFullyQualified(dataDirectory))
            throw new ArgumentException("The data directory must be an absolute path.", nameof(dataDirectory));
        file = new ProtectedJsonFile<RosterFile>(Path.Combine(dataDirectory, demo ? "demo-roster.json" : "roster.json"), JsonOptions,
            roster => roster.Version, RosterFile.CurrentVersion, MaxFileBytes, "roster", logger);
        this.logger = logger;
    }

    public string FilePath => file.FilePath;

    public string BackupPath => file.BackupPath;

    /// <summary>Loads the roster as it can be used at <paramref name="now"/>. Never throws for a bad or missing file.</summary>
    public RosterLoadResult Load(DateTimeOffset now)
    {
        var load = file.Load();
        var kept = load.KeptAs is null ? null : Path.GetFileName(load.KeptAs);
        if (load.Error is not null && load.Outcome != StoredFileOutcome.Read)
            logger.LogWarning(load.Error, "The character roster could not be fully loaded ({Outcome}).", load.Outcome);
        else if (load.Status == StoredFileStatus.ReadOnly)
            logger.LogWarning("The character roster comes from a newer Scarlet Meter (version {Version}); it is shown but not overwritten.", load.Version);
        var problem = load.Outcome switch
        {
            StoredFileOutcome.Read or StoredFileOutcome.NoFile => null,
            StoredFileOutcome.MissingBackupRead => "Your character roster file was missing, so the previous save was restored.",
            StoredFileOutcome.Newer or StoredFileOutcome.MissingBackupNewer =>
                "Your character roster comes from a newer version of Scarlet Meter. It is shown, and changes are not saved.",
            StoredFileOutcome.Inaccessible or StoredFileOutcome.MissingBackupInaccessible or StoredFileOutcome.UnreadableBackupInaccessible =>
                $"Your character roster file could not be opened{KeptNote(kept)}. Changes are not saved until the meter restarts with the file free.",
            StoredFileOutcome.MissingBackupNotKeptAside or StoredFileOutcome.UnreadableNotKeptAside or StoredFileOutcome.UnreadableBackupNotKeptAside =>
                $"Your character roster file could not be read or set aside{KeptNote(kept)}. Changes are not saved until the meter restarts with the file free.",
            StoredFileOutcome.MissingBackupUnreadable =>
                $"The previous save of your character roster could not be read, so a new roster was started. It was kept as {kept}.",
            StoredFileOutcome.UnreadableBackupRead =>
                $"Your character roster file could not be read, so the previous save was restored. The unreadable file was kept as {kept}.",
            _ => $"Your character roster file could not be read and there was no usable previous save, so a new roster was started. The unreadable file was kept as {kept}.",
        };

        var (roster, repairs) = RosterNormalizer.Normalize(load.Value, now);
        foreach (var repair in repairs) logger.LogInformation("Character roster repaired: {Repair}", repair);
        var repaired = repairs.Count == 0 ? null : "Some saved characters were out of range and were repaired: " + string.Join(" ", repairs);
        var notice = problem is null ? repaired : repaired is null ? problem : problem + " " + repaired;
        return new RosterLoadResult(roster, load.Status, load.CanSave, notice);
    }

    /// <summary>Writes the roster atomically, keeping the previous file as the backup. Throws on I/O failure.</summary>
    public void Save(RosterFile roster) => file.Save(roster);

    private static string KeptNote(string? kept) => kept is null ? string.Empty : $" (an unreadable one was kept as {kept})";
}
