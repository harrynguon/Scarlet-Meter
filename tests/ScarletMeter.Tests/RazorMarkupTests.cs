using System.IO;
using System.Text.RegularExpressions;

namespace ScarletMeter.Tests;

/// <summary>
/// Static checks over the app's Razor markup for mistakes the compiler accepts: Razor reads "x@value" as an
/// e-mail address and renders it literally ("x@(value)" is the expression), and a command shown to the player
/// must not depend on the directory their shell starts in (AGENTS.md, "Paths"). The checkout is found from the
/// test assembly's own location, never the working directory.
/// </summary>
public sealed partial class RazorMarkupTests
{
    [Fact]
    public void NoExpressionFollowsALetterOrDigitWithoutParentheses() =>
        Assert.Empty(Offending(line => EmailLikeExpression().IsMatch(line)));

    [Fact]
    public void ScriptCommandsShownToThePlayerAreRootedAtTheCheckout() =>
        // Every scripts\ path in the markup sits under the <checkout> placeholder (escaped in Razor markup).
        Assert.Empty(Offending(line => RelativeScriptPath().IsMatch(line)));

    [GeneratedRegex(@"[A-Za-z0-9]@[A-Za-z_]")]
    private static partial Regex EmailLikeExpression();

    // "scripts\" (or "scripts/") not directly preceded by "&lt;checkout&gt;\" or "<checkout>\".
    [GeneratedRegex(@"(?<!&lt;checkout&gt;[\\/])(?<!<checkout>[\\/])\bscripts[\\/]", RegexOptions.IgnoreCase)]
    private static partial Regex RelativeScriptPath();

    // Lines of the app's .razor files (outside bin and obj) that match, as "file:line: text".
    private static List<string> Offending(Func<string, bool> matches)
    {
        var app = Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App");
        var files = Directory.EnumerateFiles(app, "*.razor", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(files);
        return files
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (File: f, Line: i + 1, Text: line)))
            .Where(l => matches(l.Text))
            .Select(l => $"{Path.GetRelativePath(app, l.File)}:{l.Line}: {l.Text.Trim()}")
            .ToList();
    }

    // The directory holding ScarletMeter.slnx, above the test assembly.
    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ScarletMeter.slnx"))) return dir.FullName;
        throw new InvalidOperationException("ScarletMeter.slnx was not found above " + AppContext.BaseDirectory);
    }
}
