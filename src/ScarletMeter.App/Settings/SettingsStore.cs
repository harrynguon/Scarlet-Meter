using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ScarletMeter.App.Storage;

namespace ScarletMeter.App.Settings;

/// <summary>How a settings load went, for the log and the settings page.</summary>
public enum SettingsLoadStatus
{
    /// <summary>No settings file yet: defaults.</summary>
    Defaults,
    /// <summary>Read as saved (values out of range may have been repaired; see the notice).</summary>
    Loaded,
    /// <summary>The file could not be read; it was kept aside and the previous save was used.</summary>
    RecoveredFromBackup,
    /// <summary>Neither the file nor the previous save could be read; whichever existed was kept aside, defaults are used.</summary>
    ResetToDefaults,
    /// <summary>The file comes from a newer build, or could not be opened: it is used but never overwritten.</summary>
    ReadOnly,
}

/// <summary>A loaded settings value with what happened while loading it.</summary>
/// <param name="CanSave">False when saving would overwrite a file this build cannot fully represent or read.</param>
/// <param name="Notice">A sentence for the settings page when something was repaired or recovered.</param>
public sealed record SettingsLoadResult(MeterSettings Settings, SettingsLoadStatus Status, bool CanSave, string? Notice);

/// <summary>
/// Reads and writes settings.json in the data directory, through <see cref="ProtectedJsonFile{T}"/>: writes are atomic
/// (a temporary file replaces the old one) and keep the previous save as settings.json.bak. A file that cannot be
/// parsed is never thrown away: it is renamed to settings.unreadable-&lt;UTC time&gt;.json, so it can be recovered by
/// hand, before the backup or the defaults are used. This class says each outcome in the settings page's words and
/// repairs what it loads (<see cref="SettingsNormalizer"/>).
/// </summary>
public sealed class SettingsStore
{
    /// <summary>Settings files are tiny; anything larger is not ours and is not parsed.</summary>
    public const long MaxFileBytes = 1024 * 1024;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ProtectedJsonFile<MeterSettings> file;
    private readonly ILogger<SettingsStore> logger;

    /// <param name="dataDirectory">The absolute data directory; settings.json lives directly inside it.</param>
    public SettingsStore(string dataDirectory, ILogger<SettingsStore> logger)
    {
        if (!Path.IsPathFullyQualified(dataDirectory))
            throw new ArgumentException("The data directory must be an absolute path.", nameof(dataDirectory));
        file = new ProtectedJsonFile<MeterSettings>(Path.Combine(dataDirectory, "settings.json"), JsonOptions,
            settings => settings.Version, MeterSettings.CurrentVersion, MaxFileBytes, "settings", logger);
        this.logger = logger;
    }

    public string FilePath => file.FilePath;

    public string BackupPath => file.BackupPath;

    /// <summary>Loads the settings. Never throws for a bad or missing file; the result says what happened.</summary>
    public SettingsLoadResult Load()
    {
        var load = file.Load();
        switch (load.Outcome)
        {
            case StoredFileOutcome.Read:
                return Loaded(load.Value!, SettingsLoadStatus.Loaded);
            case StoredFileOutcome.NoFile:
                return new SettingsLoadResult(SettingsNormalizer.Normalize(null).Settings, SettingsLoadStatus.Defaults, true, null);
            case StoredFileOutcome.MissingBackupRead:
                return Loaded(load.Value!, SettingsLoadStatus.RecoveredFromBackup) with
                {
                    Notice = "Your settings file was missing, so the previous save was restored.",
                };
            case StoredFileOutcome.MissingBackupNewer:
                logger.LogWarning("settings.json was missing and its backup was written by a newer Scarlet Meter (version {Version}); it is used but not overwritten.", load.Version);
                return new SettingsLoadResult(SettingsNormalizer.Normalize(load.Value).Settings, SettingsLoadStatus.ReadOnly, false,
                    "Your settings file was missing, and the previous save comes from a newer version of Scarlet Meter. It is in use, and changes here are not saved.");
            case StoredFileOutcome.MissingBackupInaccessible:
                return ReadOnlyDefaults($"Your settings file was missing, and the previous save could not be opened ({load.Error?.Message}).");
            case StoredFileOutcome.MissingBackupNotKeptAside:
                return ReadOnlyDefaults("Your settings file was missing, and the previous save could not be read or set aside.");
            case StoredFileOutcome.MissingBackupUnreadable:
                logger.LogWarning(load.Error, "settings.json was missing and its backup could not be read; the backup was kept as {Kept}.", load.KeptAs);
                return new SettingsLoadResult(SettingsNormalizer.Normalize(null).Settings, SettingsLoadStatus.ResetToDefaults, true,
                    $"Your settings file was missing and the previous save could not be read, so defaults are in use. The previous save was kept as {Path.GetFileName(load.KeptAs)}.");
            case StoredFileOutcome.Newer:
                logger.LogWarning("settings.json was written by a newer Scarlet Meter (version {Version}); it is used but not overwritten.", load.Version);
                return new SettingsLoadResult(SettingsNormalizer.Normalize(load.Value).Settings, SettingsLoadStatus.ReadOnly, false,
                    "Your settings come from a newer version of Scarlet Meter. Changes here are not saved.");
            case StoredFileOutcome.Inaccessible:
                logger.LogWarning(load.Error, "settings.json could not be opened; defaults are used and the file is left alone.");
                return new SettingsLoadResult(SettingsNormalizer.Normalize(null).Settings, SettingsLoadStatus.ReadOnly, false,
                    $"Your settings file could not be opened ({load.Error?.Message}). Defaults are in use and changes are not saved.");
            case StoredFileOutcome.UnreadableNotKeptAside:
                return ReadOnlyDefaults($"Your settings file could not be read, and it could not be set aside ({load.Error?.Message}).");
        }

        // The file could not be read and was kept aside before anything was written; then the previous save was tried.
        logger.LogWarning(load.Error, "settings.json could not be read; it was kept as {Kept}.", load.KeptAs);
        var kept = Path.GetFileName(load.KeptAs);
        return load.Outcome switch
        {
            StoredFileOutcome.UnreadableBackupRead => Loaded(load.Value!, SettingsLoadStatus.RecoveredFromBackup) with
            {
                Notice = $"Your settings file could not be read, so the previous save was restored. The unreadable file was kept as {kept}.",
            },
            StoredFileOutcome.UnreadableBackupInaccessible =>
                ReadOnlyDefaults($"Your settings file could not be read (kept as {kept}), and the previous save could not be opened."),
            StoredFileOutcome.UnreadableBackupNotKeptAside =>
                ReadOnlyDefaults($"Your settings file could not be read (kept as {kept}), and the previous save could not be set aside."),
            _ => new SettingsLoadResult(SettingsNormalizer.Normalize(null).Settings, SettingsLoadStatus.ResetToDefaults, true,
                $"Your settings file could not be read and there was no usable previous save, so defaults are in use. The unreadable file was kept as {kept}."),
        };
    }

    private SettingsLoadResult ReadOnlyDefaults(string problem)
    {
        logger.LogWarning("Settings are read-only for this run: {Problem}", problem);
        return new SettingsLoadResult(SettingsNormalizer.Normalize(null).Settings, SettingsLoadStatus.ReadOnly, false,
            problem + " Defaults are in use and changes are not saved until the meter restarts with the file free.");
    }

    /// <summary>Writes the settings atomically, keeping the previous file as the backup. Throws on I/O failure.</summary>
    public void Save(MeterSettings settings) => file.Save(settings);

    private SettingsLoadResult Loaded(MeterSettings settings, SettingsLoadStatus status)
    {
        var (normalized, repairs) = SettingsNormalizer.Normalize(settings);
        foreach (var repair in repairs) logger.LogInformation("Settings repaired: {Repair}", repair);
        var notice = repairs.Count == 0 ? null : "Some settings were out of range and were reset: " + string.Join(" ", repairs);
        return new SettingsLoadResult(normalized, status, true, notice);
    }
}
