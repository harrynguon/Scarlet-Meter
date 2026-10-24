using System.IO;

namespace ScarletMeter.App;

/// <summary>
/// The portable layout, the way RATmeter is used: a <see cref="MarkerFileName"/> file beside the executable
/// makes the meter keep everything it writes (settings, history, logs, the WebView2 profile) in a
/// <see cref="DataFolderName"/> folder beside it, so the folder can be unzipped anywhere and run, and moved or
/// deleted as one. scripts\Publish-Portable.ps1 writes the marker; an installed build has none and uses the
/// per-user data directory. <c>--data-dir</c> overrides both.
/// </summary>
public static class PortableLayout
{
    /// <summary>The marker's file name. scripts\PortableFolders.ps1 names the same file (a test keeps the two in step).</summary>
    public const string MarkerFileName = "ScarletMeter.portable";

    /// <summary>The data folder's name, beside the executable. scripts\PortableFolders.ps1 carries a folder of this
    /// name into each new build and never deletes one (a test keeps the names in step).</summary>
    public const string DataFolderName = "data";

    /// <summary>
    /// The data directory for a meter whose executable is in <paramref name="baseDirectory"/>, an absolute path
    /// (the application's base directory, never the working directory), or null when it is not portable.
    /// </summary>
    public static string? DataDirectory(string baseDirectory)
    {
        if (!Path.IsPathFullyQualified(baseDirectory))
            throw new ArgumentException("The base directory must be an absolute path.", nameof(baseDirectory));
        return File.Exists(Path.Combine(baseDirectory, MarkerFileName)) ? Path.Combine(baseDirectory, DataFolderName) : null;
    }

    /// <summary>True when the running meter is a portable copy.</summary>
    public static bool IsCurrent => DataDirectory(AppContext.BaseDirectory) is not null;
}
