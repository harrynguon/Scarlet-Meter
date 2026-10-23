using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace ScarletMeter.App.Settings;

/// <summary>
/// The overlay's background image. A chosen picture is copied into the data directory's backgrounds
/// folder under a content-derived name (so the page reloads it when it changes and the original may move),
/// and only names of that form are accepted from settings.json. The page reads the folder through a
/// WebView2 virtual host (see <see cref="WebAssetHosts"/>), never through a file path.
/// </summary>
public static partial class BackgroundImages
{
    /// <summary>Largest picture accepted; larger files make the WebView slow to paint.</summary>
    public const long MaxBytes = 8 * 1024 * 1024;

    /// <summary>File types the picker offers and the import accepts.</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"];

    /// <summary>The absolute folder inside the data directory that holds the imported picture.</summary>
    public static string Folder(string dataDirectory) => Path.Combine(dataDirectory, "backgrounds");

    /// <summary>True for names <see cref="Import"/> produces, and nothing else.</summary>
    public static bool IsStoredName(string name) => StoredName().IsMatch(name);

    /// <summary>
    /// Copies a picture into the backgrounds folder and returns its stored name. Earlier pictures are removed.
    /// Throws <see cref="InvalidDataException"/> for an unsupported, empty or oversized file.
    /// </summary>
    public static string Import(string sourcePath, string dataDirectory)
    {
        if (!Path.IsPathFullyQualified(sourcePath)) throw new ArgumentException("The picture path must be absolute.", nameof(sourcePath));
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (!Extensions.Contains(extension))
            throw new InvalidDataException($"Choose a {string.Join(", ", Extensions)} picture.");
        var info = new FileInfo(sourcePath);
        if (!info.Exists) throw new FileNotFoundException("The picture no longer exists.", sourcePath);
        if (info.Length is 0 or > MaxBytes)
            throw new InvalidDataException($"Choose a picture smaller than {MaxBytes / (1024 * 1024)} MB.");

        var bytes = File.ReadAllBytes(sourcePath);
        var name = $"background-{Convert.ToHexStringLower(SHA256.HashData(bytes))[..8]}{extension}";
        var folder = Folder(dataDirectory);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, name), bytes);
        RemoveExcept(folder, name);
        return name;
    }

    /// <summary>Deletes imported pictures other than <paramref name="keep"/> (all of them when it is null).</summary>
    public static void RemoveExcept(string folder, string? keep)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var fileName = Path.GetFileName(file);
            if (IsStoredName(fileName) && fileName != keep)
            {
                try { File.Delete(file); }
                catch (IOException) { /* in use by the WebView; removed on a later change */ }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    [GeneratedRegex(@"^background-[0-9a-f]{8}\.(png|jpg|jpeg|gif|webp|bmp)$", RegexOptions.CultureInvariant)]
    private static partial Regex StoredName();
}
