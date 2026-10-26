using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ScarletMeter.App.Storage;

/// <summary>How a protected file's load went (<see cref="ProtectedJsonFile{T}.Load"/>).</summary>
public enum StoredFileStatus
{
    /// <summary>No file and no backup: the caller starts from its defaults.</summary>
    Defaults,
    /// <summary>The file was read.</summary>
    Loaded,
    /// <summary>The file was missing or could not be read (and was kept aside); the backup was used.</summary>
    RecoveredFromBackup,
    /// <summary>Neither the file nor the backup could be used; whichever was damaged was kept aside; defaults.</summary>
    ResetToDefaults,
    /// <summary>The content in use must not be overwritten: it is from a newer build, or a file could not be opened or set aside.</summary>
    ReadOnly,
}

/// <summary>Which way a load went, so the caller can say it in its own words.</summary>
public enum StoredFileOutcome
{
    /// <summary>Neither the file nor its backup exists.</summary>
    NoFile,
    /// <summary>The file was read.</summary>
    Read,
    /// <summary>The file was missing; its backup was read.</summary>
    MissingBackupRead,
    /// <summary>The file was missing; its backup comes from a newer build and is used read-only.</summary>
    MissingBackupNewer,
    /// <summary>The file was missing; its backup could not be opened.</summary>
    MissingBackupInaccessible,
    /// <summary>The file was missing; its backup could not be read and was kept aside (<see cref="StoredFileLoad{T}.KeptAs"/>).</summary>
    MissingBackupUnreadable,
    /// <summary>The file was missing; its backup could not be read or set aside.</summary>
    MissingBackupNotKeptAside,
    /// <summary>The file comes from a newer build and is used read-only.</summary>
    Newer,
    /// <summary>The file could not be opened.</summary>
    Inaccessible,
    /// <summary>The file could not be read or set aside.</summary>
    UnreadableNotKeptAside,
    /// <summary>The file could not be read (kept aside); its backup was read.</summary>
    UnreadableBackupRead,
    /// <summary>The file could not be read (kept aside); its backup could not be opened.</summary>
    UnreadableBackupInaccessible,
    /// <summary>The file could not be read (kept aside); its backup could not be used or set aside.</summary>
    UnreadableBackupNotKeptAside,
    /// <summary>The file could not be read (kept aside), and there was no usable backup (a damaged or newer one was kept aside too).</summary>
    UnreadableNoBackup,
}

/// <summary>
/// A load's result: the content to use (null for the caller's defaults), what to tell the player, and whether saving
/// is safe. <see cref="Version"/> is the newer build's format number for a read-only newer file; <see cref="KeptAs"/>
/// the path a damaged file was set aside to (the main file's when it was damaged, else the backup's); <see cref="Error"/>
/// the failure behind the outcome, when there was one.
/// </summary>
public sealed record StoredFileLoad<T>(
    T? Value, StoredFileStatus Status, StoredFileOutcome Outcome, bool CanSave, int Version = 0, string? KeptAs = null, Exception? Error = null)
    where T : class;

/// <summary>
/// One JSON file in the data directory, kept the way settings.json always has been: written atomically (a temporary
/// file replaces the old one) with the previous save kept as <c>&lt;name&gt;.bak</c>; a file that cannot be parsed is
/// never thrown away but renamed to <c>&lt;name&gt;.unreadable-&lt;UTC time&gt;.json</c> before the backup or the
/// defaults are used; a file from a newer build (a format number above this build's) is used but never overwritten;
/// and when a damaged file or a backup cannot be moved out of the way, or a file cannot be opened, saving is switched
/// off, because the next save's File.Replace would overwrite it. Shared by <c>SettingsStore</c> and <c>RosterStore</c>.
/// </summary>
/// <typeparam name="T">The file's content; <paramref name="versionOf"/> reads its format number.</typeparam>
public sealed class ProtectedJsonFile<T> where T : class
{
    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    private readonly JsonSerializerOptions options;
    private readonly Func<T, int> versionOf;
    private readonly int currentVersion;
    private readonly long maxBytes;
    private readonly string contentName;
    private readonly ILogger logger;

    /// <param name="filePath">The file's absolute path.</param>
    /// <param name="options">How the content is (de)serialised.</param>
    /// <param name="versionOf">The content's format number: above <paramref name="currentVersion"/> is a newer build's, below 1 unreadable.</param>
    /// <param name="currentVersion">This build's format number.</param>
    /// <param name="maxBytes">Anything larger is not ours and is not parsed.</param>
    /// <param name="contentName">What the file holds, for the errors a notice may quote ("settings": "Unknown settings version 0.").</param>
    /// <param name="logger">Where a failure to keep a file aside is logged.</param>
    public ProtectedJsonFile(
        string filePath, JsonSerializerOptions options, Func<T, int> versionOf, int currentVersion, long maxBytes, string contentName, ILogger logger)
    {
        if (!Path.IsPathFullyQualified(filePath))
            throw new ArgumentException("The file path must be absolute.", nameof(filePath));
        FilePath = filePath;
        this.options = options;
        this.versionOf = versionOf;
        this.currentVersion = currentVersion;
        this.maxBytes = maxBytes;
        this.contentName = contentName;
        this.logger = logger;
    }

    public string FilePath { get; }

    public string BackupPath => FilePath + ".bak";

    /// <summary>Loads the file. Never throws for a bad or missing file; the result says what happened.</summary>
    public StoredFileLoad<T> Load()
    {
        var main = TryRead(FilePath);
        switch (main.Outcome)
        {
            case ReadOutcome.Read:
                return new(main.Value, StoredFileStatus.Loaded, StoredFileOutcome.Read, true);
            case ReadOutcome.NewerVersion:
                return new(main.Value, StoredFileStatus.ReadOnly, StoredFileOutcome.Newer, false, main.Version);
            case ReadOutcome.Inaccessible:
                return new(null, StoredFileStatus.ReadOnly, StoredFileOutcome.Inaccessible, false, Error: main.Error);
            case ReadOutcome.Missing:
                // A previous run may have set a damaged file aside without saving again: restore its backup. Any other
                // backup is protected as the main file would be, because the second save's File.Replace would
                // overwrite it: one from a newer build is used but never overwritten, one that cannot be opened
                // switches saving off, and a damaged one is kept aside (or saving is switched off).
                var previous = TryRead(BackupPath);
                switch (previous.Outcome)
                {
                    case ReadOutcome.Read:
                        return new(previous.Value, StoredFileStatus.RecoveredFromBackup, StoredFileOutcome.MissingBackupRead, true);
                    case ReadOutcome.Missing:
                        return new(null, StoredFileStatus.Defaults, StoredFileOutcome.NoFile, true);
                    case ReadOutcome.NewerVersion:
                        return new(previous.Value, StoredFileStatus.ReadOnly, StoredFileOutcome.MissingBackupNewer, false, previous.Version);
                    case ReadOutcome.Inaccessible:
                        return new(null, StoredFileStatus.ReadOnly, StoredFileOutcome.MissingBackupInaccessible, false, Error: previous.Error);
                }
                var keptBackup = KeepAside(BackupPath);
                return keptBackup is null
                    ? new(null, StoredFileStatus.ReadOnly, StoredFileOutcome.MissingBackupNotKeptAside, false, Error: previous.Error)
                    : new(null, StoredFileStatus.ResetToDefaults, StoredFileOutcome.MissingBackupUnreadable, true, KeptAs: keptBackup, Error: previous.Error);
        }

        // Unreadable: keep the file aside before anything is written, then fall back to the previous save.
        var kept = KeepAside(FilePath);
        if (kept is null) return new(null, StoredFileStatus.ReadOnly, StoredFileOutcome.UnreadableNotKeptAside, false, Error: main.Error);
        var backup = TryRead(BackupPath);
        switch (backup.Outcome)
        {
            case ReadOutcome.Read:
                return new(backup.Value, StoredFileStatus.RecoveredFromBackup, StoredFileOutcome.UnreadableBackupRead, true, KeptAs: kept, Error: main.Error);
            case ReadOutcome.Inaccessible:
                return new(null, StoredFileStatus.ReadOnly, StoredFileOutcome.UnreadableBackupInaccessible, false, KeptAs: kept, Error: main.Error);
            case ReadOutcome.Unreadable or ReadOutcome.NewerVersion when KeepAside(BackupPath) is null:
                return new(null, StoredFileStatus.ReadOnly, StoredFileOutcome.UnreadableBackupNotKeptAside, false, KeptAs: kept, Error: main.Error);
        }
        return new(null, StoredFileStatus.ResetToDefaults, StoredFileOutcome.UnreadableNoBackup, true, KeptAs: kept, Error: main.Error);
    }

    /// <summary>Writes the content atomically, keeping the previous file as the backup. Throws on I/O failure.</summary>
    public void Save(T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, options);
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        if (File.Exists(FilePath)) File.Replace(temporary, FilePath, BackupPath, ignoreMetadataErrors: true);
        else File.Move(temporary, FilePath);
    }

    private enum ReadOutcome { Missing, Read, NewerVersion, Unreadable, Inaccessible }

    private sealed record ReadResult(ReadOutcome Outcome, T? Value = null, int Version = 0, Exception? Error = null);

    private ReadResult TryRead(string path)
    {
        byte[] bytes;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return new ReadResult(ReadOutcome.Missing);
            if (info.Length > maxBytes)
                return new ReadResult(ReadOutcome.Unreadable, Error: new InvalidDataException($"The file is larger than {maxBytes} bytes."));
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ReadResult(ReadOutcome.Inaccessible, Error: ex);
        }

        try
        {
            // Editors that save "UTF-8 with BOM" (Windows PowerShell, older Notepad) prefix a byte-order mark, which the
            // UTF-8 JSON reader rejects; a hand-edited file must still load.
            var json = bytes.AsSpan();
            if (json.StartsWith(Utf8Bom)) json = json[Utf8Bom.Length..];
            var value = JsonSerializer.Deserialize<T>(json, options);
            if (value is null) return new ReadResult(ReadOutcome.Unreadable, Error: new InvalidDataException($"The file holds no {contentName}."));
            var version = versionOf(value);
            if (version > currentVersion) return new ReadResult(ReadOutcome.NewerVersion, value, version);
            if (version < 1) return new ReadResult(ReadOutcome.Unreadable, Error: new InvalidDataException($"Unknown {contentName} version {version}."));
            return new ReadResult(ReadOutcome.Read, value, version);
        }
        catch (JsonException ex)
        {
            return new ReadResult(ReadOutcome.Unreadable, Error: ex);
        }
    }

    // Renames a damaged file so nothing later overwrites it. Returns the new path, or null if it could not be moved (the
    // caller then must not save). A name already taken gets a counter rather than being replaced.
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
