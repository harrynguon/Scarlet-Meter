using System.IO;
using System.Text.Json;

namespace ScarletMeter.Tests;

/// <summary>
/// THIRD-PARTY-NOTICES.md lists every NuGet package of the app's restore (the published program's packages), so a
/// release never ships a package its notices leave out (run scripts/Write-ThirdPartyNotices.ps1 after a package
/// change), and LICENSE, NOTICE and the notices name what a GPLv3 release must. Paths come from the test assembly's
/// location, never the working directory.
/// </summary>
public sealed class ThirdPartyNoticesTests
{
    [Fact]
    public void EveryPackageOfTheAppIsListed()
    {
        var root = RepositoryRoot();
        var assetsPath = Path.Combine(root, "src", "ScarletMeter.App", "obj", "project.assets.json");
        Assert.True(File.Exists(assetsPath), $"No restore of the app at {assetsPath}.");
        using var assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
        var target = assets.RootElement.GetProperty("targets").EnumerateObject().First();
        var packages = target.Value.EnumerateObject()
            .Where(p => p.Value.TryGetProperty("type", out var type) && type.GetString() == "package")
            .Select(p => p.Name.Split('/'))
            .ToList();
        Assert.NotEmpty(packages);

        var notices = File.ReadAllText(Path.Combine(root, "THIRD-PARTY-NOTICES.md"));
        var missing = packages.Where(p => !notices.Contains($"| {p[0]} | {p[1]} |", StringComparison.Ordinal)).Select(p => $"{p[0]} {p[1]}").ToList();
        Assert.True(missing.Count == 0, "Not in THIRD-PARTY-NOTICES.md (run scripts/Write-ThirdPartyNotices.ps1): " + string.Join(", ", missing));
    }

    [Fact]
    public void TheLicenceIsThePlainGplAndTheNoticesCreditUpstream()
    {
        var root = RepositoryRoot();
        var licence = File.ReadAllText(Path.Combine(root, "LICENSE"));
        Assert.StartsWith("                    GNU GENERAL PUBLIC LICENSE", licence, StringComparison.Ordinal);
        Assert.EndsWith("<https://www.gnu.org/licenses/why-not-lgpl.html>.", licence.TrimEnd(), StringComparison.Ordinal);

        var notice = File.ReadAllText(Path.Combine(root, "NOTICE"));
        var thirdParty = File.ReadAllText(Path.Combine(root, "THIRD-PARTY-NOTICES.md"));
        foreach (var text in new[] { notice, thirdParty })
        {
            Assert.Contains("https://github.com/Kuroukihime/AIon2-Dps-Meter", text, StringComparison.Ordinal);
            Assert.Contains("https://github.com/harrynguon/Scarlet-Meter", text, StringComparison.Ordinal);
        }
        Assert.Contains("NCSOFT", notice, StringComparison.Ordinal);
    }

    // The directory holding ScarletMeter.slnx, above the test assembly (never the working directory).
    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ScarletMeter.slnx"))) return dir.FullName;
        throw new InvalidOperationException("ScarletMeter.slnx was not found above " + AppContext.BaseDirectory);
    }
}
