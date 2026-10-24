using System.IO;
using System.Text.RegularExpressions;

namespace ScarletMeter.Tests;

/// <summary>
/// Static checks over the app's Razor markup for mistakes the compiler accepts: Razor reads "x@value" as an
/// e-mail address and renders it literally ("x@(value)" is the expression), and a command shown to the player
/// must not depend on the directory their shell starts in (AGENTS.md, "Paths"); and the pages' scripts stay the
/// app's own. The checkout is found from the test assembly's own location, never the working directory.
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

    // Every <script> in index.html (whatever its attributes' order) loads one of the app's own files, which exists,
    // and none is inline; the overlay's effects script reaches nothing outside the page: no requests, no .NET
    // interop, no eval.
    [Fact]
    public void ThePagesLoadOnlyTheAppsOwnScripts()
    {
        var wwwroot = Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App", "wwwroot");
        var tags = ScriptTag().Matches(File.ReadAllText(Path.Combine(wwwroot, "index.html")));
        Assert.NotEmpty(tags);
        var sources = new List<string>();
        foreach (Match tag in tags)
        {
            Assert.True(string.IsNullOrWhiteSpace(tag.Groups["body"].Value), $"inline script: {tag.Value}");
            var src = ScriptSrc().Match(tag.Groups["attributes"].Value);
            Assert.True(src.Success, $"script without src: {tag.Value}");
            sources.Add(src.Groups[1].Value);
        }
        Assert.Contains("js/overlay-fx.js", sources);
        foreach (var source in sources)
        {
            Assert.DoesNotContain(":", source);
            Assert.False(source.StartsWith('/'), source);
            if (!source.StartsWith("_framework/", StringComparison.Ordinal))
                Assert.True(File.Exists(Path.Combine(wwwroot, source.Replace('/', Path.DirectorySeparatorChar))), source);
        }
        var effects = File.ReadAllText(Path.Combine(wwwroot, "js", "overlay-fx.js"));
        foreach (var reach in new[] { "fetch(", "XMLHttpRequest", "WebSocket", "sendBeacon", "DotNet.", "eval(", "Function(" })
            Assert.DoesNotContain(reach, effects);
    }

    // A player's row reads DPS, then total damage, then share (Harry reads the DPS first), each in its own
    // column; the CSS gives DPS and total fixed widths so the columns line up.
    [Fact]
    public void APlayersRowShowsDpsThenTotalDamageThenShare()
    {
        var page = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App", "Pages", "MeterPage.razor"));
        var columns = RowNumberColumn().Matches(page).Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(["num dps", "num total", "num pct"], columns);
        Assert.True(page.IndexOf("player.Dps", StringComparison.Ordinal) < page.IndexOf("player.TotalDamage", StringComparison.Ordinal));
        var css = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App", "wwwroot", "css", "app.css"));
        foreach (var column in new[] { "dps", "total", "pct" })
            Assert.Matches($@"\.row \.{column} \{{[^}}]*min-width:", css);
    }

    // The overlay holds its notices back by the player's "Hide during boss fights" switch, keeps the strip mounted
    // and silent while they are held, and tells the window layer, whose restore for an event goes through the gate.
    // Source-text guards: the strip's rendering and the WPF window layer have no test host here.
    [Fact]
    public void TheOverlayHoldsItsNoticesByThePlayersSwitch()
    {
        var pages = Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App", "Pages");
        var code = File.ReadAllText(Path.Combine(pages, "MeterPage.razor.cs"));
        var markup = File.ReadAllText(Path.Combine(pages, "MeterPage.razor"));
        Assert.Contains("AlertStripLayout.HiddenForFight(alertSettings.HideDuringBossFights, bossFightUnderWay)", code);
        Assert.Contains("bossFightUnderWay = bossFight.Update(snapshot.Target, now);", code);
        Assert.Contains("Windows.HoldAlerts(NoticesHeld);", code);
        Assert.Matches(@"(?m)^\s*<AlertStrip [^>]*Held=""NoticesHeld"" />", markup);

        // The strip shows nothing and announces nothing while held, before either of its other branches.
        var strip = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App", "Components", "AlertStrip.razor"));
        Assert.Contains(@"<span class=""sr-only"" role=""status"">@(Held ? string.Empty : Announcement)</span>", strip);
        Assert.Matches(@"(?s)@if \(Held\)\s*\{\s*@\*[^*]*\*@\s*\}\s*else if \(View\.Alerts\.Count > 0\)", strip);

        // The window layer asks the gate before every restore for an event, and acts on the one that waited.
        var windows = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App", "Windows", "MeterWindows.cs"));
        Assert.Contains("if (restoreGate.Approaching()) RestoreForAlert();", windows);
        Assert.Contains("if (restoreGate.Hold(held, alertMonitor?.Current.Alerts.Count > 0)) RestoreForAlert();", windows);
        Assert.Equal(3, Regex.Matches(windows, @"\bRestoreForAlert\(\)").Count); // the two calls and its definition
    }

    // The overlay's panel is only as tall as its content, up to the window, so the room the notices leave in a boss
    // fight is see-through rather than empty panel; the rows take only their own height and scroll when the window is
    // too short; and the picture is laid out against the window, so it does not rescale as the panel changes height.
    [Fact]
    public void TheOverlayPanelFitsItsContent()
    {
        var styles = Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App", "wwwroot", "css");
        var css = File.ReadAllText(Path.Combine(styles, "app.css"));
        var alerts = File.ReadAllText(Path.Combine(styles, "alerts.css"));
        static string Block(string source, string selector) =>
            Regex.Match(source, @"(?m)^" + Regex.Escape(selector) + @" \{(?<body>[^}]*)\}").Groups["body"].Value;
        var overlay = Block(css, ".overlay");
        Assert.Matches(@"(?m)^\s*max-height:\s*100%;", overlay);
        Assert.DoesNotMatch(@"(?m)^\s*height:", overlay);
        // The panel's max-height is a percentage of #app, which must itself be the window's height.
        Assert.Matches(@"(?m)^\s*height:\s*100%;", Block(css, "#app"));
        Assert.Matches(@"(?m)^\s*height:\s*100%;", Block(css, "html, body"));
        // No other rule for the panel itself (.overlay.fx, .overlay[...]; not its descendants) gives it a height.
        Assert.DoesNotMatch(@"(?m)^\.overlay[.:\[][^\s{,]*\s*\{[^}]*(?<![-\w])height\s*:", css.Replace(".overlay::before {", ".before-layer {"));
        var rows = Block(css, ".rows");
        Assert.Matches(@"(?m)^\s*flex:\s*0 1 auto;", rows);
        Assert.Matches(@"(?m)^\s*min-height:\s*0;", rows);
        Assert.Matches(@"var\(--panel-image, none\) center / cover no-repeat fixed;", Block(css, ".overlay::before"));
        // The expanded strip keeps its share however many rows there are (the rows no longer give up their space).
        Assert.Matches(@"(?m)^\s*flex-shrink:\s*0;", Block(alerts, ".alerts.expanded"));
    }

    [GeneratedRegex(@"<span class=""(num[^""]*)""")]
    private static partial Regex RowNumberColumn();

    [GeneratedRegex(@"<script\b(?<attributes>[^>]*)>(?<body>.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ScriptTag();

    [GeneratedRegex(@"\bsrc\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptSrc();

    [GeneratedRegex(@"[A-Za-z0-9]@[A-Za-z_]")]
    private static partial Regex EmailLikeExpression();

    // A script command ("scripts\Name.ps1", either slash) not directly preceded by "&lt;checkout&gt;\" or
    // "<checkout>\". A web asset such as "scripts/app.js" is not a command and does not match.
    [GeneratedRegex(@"(?<!&lt;checkout&gt;[\\/])(?<!<checkout>[\\/])\bscripts[\\/][\w-]+\.ps1\b", RegexOptions.IgnoreCase)]
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
