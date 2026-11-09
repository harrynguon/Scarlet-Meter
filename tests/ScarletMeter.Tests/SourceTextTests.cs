using System.IO;

namespace ScarletMeter.Tests;

/// <summary>
/// The repository's own C# and Razor text, read as files: checks a build cannot make. The upstream submodule is not ours
/// and is not read.
/// </summary>
public sealed class SourceTextTests
{
    // No line of Scarlet's code or tests ends in a space or tab. A comment block replaced in parts by a script left a
    // whitespace-only line, a "/// " line and a sentence cut after its comma, all three ending in a space, and the first
    // reached a commit (the audit on #80; LESSONS). A cut comment, like a stray space, now fails here.
    [Fact]
    public void NoLineEndsInWhitespace()
    {
        var root = RepositoryRoot();
        var found = new List<string>();
        foreach (var top in new[] { "src", "tests" })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, top), "*.*", SearchOption.AllDirectories))
            {
                if (!(file.EndsWith(".cs", StringComparison.Ordinal) || file.EndsWith(".razor", StringComparison.Ordinal))) continue;
                var parts = Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar);
                if (parts.Contains("bin") || parts.Contains("obj")) continue;
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Length > 0 && char.IsWhiteSpace(lines[i][^1]))
                        found.Add($"{Path.GetRelativePath(root, file)}:{i + 1}");
                }
            }
        }
        Assert.True(found.Count == 0, "lines ending in whitespace:\n" + string.Join("\n", found));
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ScarletMeter.slnx"))) return dir.FullName;
        throw new InvalidOperationException("ScarletMeter.slnx was not found above " + AppContext.BaseDirectory);
    }
}
