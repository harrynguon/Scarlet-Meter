namespace ScarletMeter.App.Storage;

/// <summary>
/// The sentence a page shows about a file kept by <see cref="ProtectedJsonFile{T}"/> when its load was not plain: restored
/// from the backup, from a newer build, locked, or unreadable and started afresh. Shared by the files of the player's
/// progress (the character roster, the pet souls), each naming itself.
/// </summary>
internal static class StoredFileNotice
{
    /// <summary>
    /// The sentence for a load's outcome, or null when the file read as it was or was not there.
    /// </summary>
    /// <param name="outcome">How the load went.</param>
    /// <param name="kept">The file name an unreadable file was kept as, or null when none was.</param>
    /// <param name="what">What the file holds, as the sentence names it, a singular noun ("character roster", "pet list"):
    /// "Your {what} comes from a newer version", "Your {what} file was missing". The roster's sentences are pinned word for
    /// word (RosterTests.TheRostersFileSentencesAreAsTheyWere).</param>
    /// <param name="startedAfresh">What happens with nothing to restore, as a clause ("a new roster was started").</param>
    public static string? For(StoredFileOutcome outcome, string? kept, string what, string startedAfresh) => outcome switch
    {
        StoredFileOutcome.Read or StoredFileOutcome.NoFile => null,
        StoredFileOutcome.MissingBackupRead => $"Your {what} file was missing, so the previous save was restored.",
        StoredFileOutcome.Newer or StoredFileOutcome.MissingBackupNewer =>
            $"Your {what} comes from a newer version of Scarlet Meter. It is shown, and changes are not saved.",
        StoredFileOutcome.Inaccessible or StoredFileOutcome.MissingBackupInaccessible or StoredFileOutcome.UnreadableBackupInaccessible =>
            $"Your {what} file could not be opened{KeptNote(kept)}. Changes are not saved until the meter restarts with the file free.",
        StoredFileOutcome.MissingBackupNotKeptAside or StoredFileOutcome.UnreadableNotKeptAside or StoredFileOutcome.UnreadableBackupNotKeptAside =>
            $"Your {what} file could not be read or set aside{KeptNote(kept)}. Changes are not saved until the meter restarts with the file free.",
        StoredFileOutcome.MissingBackupUnreadable =>
            $"The previous save of your {what} could not be read, so {startedAfresh}. It was kept as {kept}.",
        StoredFileOutcome.UnreadableBackupRead =>
            $"Your {what} file could not be read, so the previous save was restored. The unreadable file was kept as {kept}.",
        _ => $"Your {what} file could not be read and there was no usable previous save, so {startedAfresh}. The unreadable file was kept as {kept}.",
    };

    private static string KeptNote(string? kept) => kept is null ? string.Empty : $" (an unreadable one was kept as {kept})";
}
