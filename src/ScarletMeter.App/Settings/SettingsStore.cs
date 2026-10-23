using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

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
    /// <summary>Neither the file nor the previous save could be read; both were kept aside, defaults are used.</summary>
    ResetToDefaults,
    /// <summary>The file comes from a newer build, or could not be opened: it is used but never overwritten.</summary>
    ReadOnly,
}

/// <summary>A loaded settings value with what happened while loading it.</summary>
/// <param name="CanSave">False when saving would overwrite a file this build cannot fully represent or read.</param>
/// <param name="Notice">A sentence for the settings page when something was repaired or recovered.</param>
public sealed record SettingsLoadResult(MeterSettings Settings, SettingsLoadStatus Status, bool CanSave, string? Notice);

/// <summary>
/// Reads and writes settings.json in the data directory. Writes are atomic (a temporary file replaces the
/// old one) and keep the previous save as settings.json.bak. A file that cannot be parsed is never thrown
/// away: it is renamed to settings.unreadable-&lt;UTC time&gt;.json, so it can be recovered by hand, before the
/// backup or the defaults are used.
/// </summary>
public sealed class SettingsStore
{
    /// <summary>Settings files are tiny; anything larger is not ours and is not parsed.</summary>
    public const long MaxFileBytes = 1024 * 1024;

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ILogger<SettingsStore> logger;

    /// <param name="dataDirectory">The absolute data directory; settings.json lives directly inside it.</param>
    public SettingsStore(string dataDirectory, ILogger<SettingsStore> logger)
    {
        if (!Path.IsPathFullyQualified(dataDirectory))
            throw new ArgumentException("The data directory must be an absolute path.", nameof(dataDirectory));
        FilePath = Path.Combine(dataDirectory, "settings.json");
        this.logger = logger;
    }

    public string FilePath { get; }

    public string BackupPath => FilePath + ".bak";

    /// <summary>Loads the settings. Never throws for a bad or missing file; the result says what happened.</summary>
    public SettingsLoadResult Load()
    {
        var main = TryRead(FilePath);
        switch (main.Outcome)
        {
            case ReadOutcome.Missing:
                // A previous run may have set a damaged file aside without saving again: restore its backup. A
                // backup that cannot be used is protected like a damaged main file, because the second save's
                // File.Replace would overwrite it: kept aside, or saving switched off if it cannot be moved.
                var previous = TryRead(BackupPath);
                switch (previous.Outcome)
                {
                    case ReadOutcome.Read:
                        return Loaded(previous.Settings!, SettingsLoadStatus.RecoveredFromBackup) with
                        {
                            Notice = "Your settings file was missing, so the previous save was restored.",
                        };
                    case ReadOutcome.Missing:
                        return new SettingsLoadResult(SettingsNormalizer.Normalize(null).Settings, SettingsLoadStatus.Defaults, true, null);
                    case ReadOutcome.Inaccessible:
                        return ReadOnlyDefaults($"Your settings file was missing, and the previous save could not be opened ({previous.Error?.Message}).");
                }
                var keptBackup = KeepAside(BackupPath);
                if (keptBackup is null)
                    return ReadOnlyDefaults("Your settings file was missing, and the previous save could not be used or set aside.");
                logger.LogWarning(previous.Error, "settings.json was missing and its backup could not be used; the backup was kept as {Kept}.", keptBackup);
                return new SettingsLoadResult(SettingsNormalizer.Normalize(null).Settings, SettingsLoadStatus.ResetToDefaults, true,
                    $"Your settings file was missing and the previous save could not be used, so defaults are in use. The previous save was kept as {Path.GetFileName(keptBackup)}.");
            case ReadOutcome.Read:
                return Loaded(main.Settings!, SettingsLoadStatus.Loaded);
            case ReadOutcome.NewerVersion:
                logger.LogWarning("settings.json was written by a newer Scarlet Meter (version {Version}); it is used but not overwritten.", main.Version);
                return new SettingsLoadResult(SettingsNormalizer.Normalize(main.Settings).Settings, SettingsLoadStatus.ReadOnly, false,
                    "Your settings come from a newer version of Scarlet Meter. Changes here are not saved.");
            case ReadOutcome.Inaccessible:
                logger.LogWarning(main.Error, "settings.json could not be opened; defaults are used and the file is left alone.");
                return new SettingsLoadResult(SettingsNormalizer.Normalize(null).Settings, SettingsLoadStatus.ReadOnly, false,
                    $"Your settings file could not be opened ({main.Error?.Message}). Defaults are in use and changes are not saved.");
        }

        // Unreadable: keep the file aside before anything is written, then fall back to the previous save. If
        // a damaged file or the backup cannot be moved out of the way (another program holds it), saving is
        // switched off, because the next save's File.Replace would otherwise overwrite it.
        var kept = KeepAside(FilePath);
        if (kept is null)
            return ReadOnlyDefaults($"Your settings file could not be read, and it could not be set aside ({main.Error?.Message}).");
        logger.LogWarning(main.Error, "settings.json could not be read; it was kept as {Kept}.", kept);
        var backup = TryRead(BackupPath);
        switch (backup.Outcome)
        {
            case ReadOutcome.Read:
                var recovered = Loaded(backup.Settings!, SettingsLoadStatus.RecoveredFromBackup);
                return recovered with
                {
                    Notice = $"Your settings file could not be read, so the previous save was restored. The unreadable file was kept as {Path.GetFileName(kept)}.",
                };
            case ReadOutcome.Inaccessible:
                return ReadOnlyDefaults($"Your settings file could not be read (kept as {Path.GetFileName(kept)}), and the previous save could not be opened.");
            case ReadOutcome.Unreadable or ReadOutcome.NewerVersion when KeepAside(BackupPath) is null:
                return ReadOnlyDefaults($"Your settings file could not be read (kept as {Path.GetFileName(kept)}), and the previous save could not be set aside.");
        }
        return new SettingsLoadResult(SettingsNormalizer.Normalize(null).Settings, SettingsLoadStatus.ResetToDefaults, true,
            $"Your settings file could not be read and there was no usable previous save, so defaults are in use. The unreadable file was kept as {Path.GetFileName(kept)}.");
    }

    private SettingsLoadResult ReadOnlyDefaults(string problem)
    {
        logger.LogWarning("Settings are read-only for this run: {Problem}", problem);
        return new SettingsLoadResult(SettingsNormalizer.Normalize(null).Settings, SettingsLoadStatus.ReadOnly, false,
            problem + " Defaults are in use and changes are not saved until the meter restarts with the file free.");
    }

    /// <summary>Writes the settings atomically, keeping the previous file as the backup. Throws on I/O failure.</summary>
    public void Save(MeterSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions);
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        if (File.Exists(FilePath)) File.Replace(temporary, FilePath, BackupPath, ignoreMetadataErrors: true);
        else File.Move(temporary, FilePath);
    }

    private SettingsLoadResult Loaded(MeterSettings settings, SettingsLoadStatus status)
    {
        var (normalized, repairs) = SettingsNormalizer.Normalize(settings);
        foreach (var repair in repairs) logger.LogInformation("Settings repaired: {Repair}", repair);
        var notice = repairs.Count == 0 ? null : "Some settings were out of range and were reset: " + string.Join(" ", repairs);
        return new SettingsLoadResult(normalized, status, true, notice);
    }

    private enum ReadOutcome { Missing, Read, NewerVersion, Unreadable, Inaccessible }

    private sealed record ReadResult(ReadOutcome Outcome, MeterSettings? Settings = null, int Version = 0, Exception? Error = null);

    private static ReadResult TryRead(string path)
    {
        byte[] bytes;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return new ReadResult(ReadOutcome.Missing);
            if (info.Length > MaxFileBytes)
                return new ReadResult(ReadOutcome.Unreadable, Error: new InvalidDataException($"The file is larger than {MaxFileBytes} bytes."));
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ReadResult(ReadOutcome.Inaccessible, Error: ex);
        }

        try
        {
            // Editors that save "UTF-8 with BOM" (Windows PowerShell, older Notepad) prefix a byte-order mark,
            // which the UTF-8 JSON reader rejects; a hand-edited file must still load.
            var json = bytes.AsSpan();
            if (json.StartsWith(Utf8Bom)) json = json[Utf8Bom.Length..];
            var settings = JsonSerializer.Deserialize<MeterSettings>(json, JsonOptions);
            if (settings is null) return new ReadResult(ReadOutcome.Unreadable, Error: new InvalidDataException("The file holds no settings."));
            if (settings.Version > MeterSettings.CurrentVersion) return new ReadResult(ReadOutcome.NewerVersion, settings, settings.Version);
            if (settings.Version < 1) return new ReadResult(ReadOutcome.Unreadable, Error: new InvalidDataException($"Unknown settings version {settings.Version}."));
            return new ReadResult(ReadOutcome.Read, settings, settings.Version);
        }
        catch (JsonException ex)
        {
            return new ReadResult(ReadOutcome.Unreadable, Error: ex);
        }
    }

    // Renames a damaged file so nothing later overwrites it. Returns the new path, or null if it could not be
    // moved (the caller then must not save). A name already taken gets a counter rather than being replaced.
    private string? KeepAside(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileName(path).Replace(".json", string.Empty, StringComparison.Ordinal);
        var stamp = $"{stem}.unreadable-{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}";
        var target = Path.Combine(directory, stamp + ".json");
        for (var n = 2; File.Exists(target) && n < 100; n++) target = Path.Combine(directory, $"{stamp}-{n}.json");
        try
        {
            File.Move(path, target, overwrite: false);
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not keep {Path} aside.", path);
            return null;
        }
    }
}
