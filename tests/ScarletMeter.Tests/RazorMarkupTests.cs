using System.IO;
using System.Text.RegularExpressions;

namespace ScarletMeter.Tests;

/// <summary>
/// Static checks over the app's Razor markup for mistakes the compiler accepts. Razor reads "x@value" as an
/// e-mail address and renders it literally; "x@(value)" is the expression. The checkout is found from the
/// test assembly's own location, never the working directory.
/// </summary>
public sealed partial class RazorMarkupTests
{
    [Fact]
    public void NoExpressionFollowsALetterOrDigitWithoutParentheses()
    {
        var app = Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App");
        var files = Directory.EnumerateFiles(app, "*.razor", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(files);

        var offences = files
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (File: f, Line: i + 1, Text: line)))
            .Where(l => EmailLikeExpression().IsMatch(l.Text))
            .Select(l => $"{Path.GetRelativePath(app, l.File)}:{l.Line}: {l.Text.Trim()}")
            .ToList();
        Assert.Empty(offences);
    }

    [GeneratedRegex(@"[A-Za-z0-9]@[A-Za-z_]")]
    private static partial Regex EmailLikeExpression();

    // The directory holding ScarletMeter.slnx, above the test assembly.
    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ScarletMeter.slnx"))) return dir.FullName;
        throw new InvalidOperationException("ScarletMeter.slnx was not found above " + AppContext.BaseDirectory);
    }
}
