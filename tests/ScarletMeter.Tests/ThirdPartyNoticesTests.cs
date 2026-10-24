using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ScarletMeter.Tests;

/// <summary>
/// THIRD-PARTY-NOTICES.md lists exactly the NuGet packages that ship in the program (from the app's restore), with the
/// licence each declares and the licence and notice files it ships, whose copies in licenses/packages match the
/// package byte for byte (run scripts/Write-ThirdPartyNotices.ps1 after a package change); every SPDX licence named
/// has its official text in licenses/; LICENSE is the plain GPLv3 byte for byte; the program keeps the Windows SDK
/// projection its overlay needs, and names it; and NOTICE and the notices name what a GPLv3 release must. Paths come from the test assembly's location, never the working directory.
/// </summary>
public sealed partial class ThirdPartyNoticesTests
{
    // The asset kinds that put a package's files into the program; Write-ThirdPartyNotices.ps1 uses the same list.
    private static readonly string[] ShippedAssets = ["compile", "runtime", "native", "resource", "runtimeTargets"];

    // SHA-256 of gnu.org's gpl-3.0.txt, apache.org's LICENSE-2.0.txt and mozilla.org's MPL 2.0 text, with LF endings.
    private const string GplSha256 = "3972dc9744f6499f0f9b2dbf76696f2ae7ad8af9b23dde66d6af86c9dfb36986";
    private const string ApacheSha256 = "cfc7749b96f63bd31c3c42b5c471bf756814053e847c10f3eb003417bc523d30";
    private const string MplSha256 = "3f3d9e0024b1921b067d6f7f88deb4a60cbe7a78e76c64e3f1d7fc3b779b9d04";
    // The MIT text from "Permission is hereby granted" to its end, as .NET's and K4os.Compression.LZ4's copies have
    // it; and dotnet/wpf's THIRD-PARTY-NOTICES.TXT at v10.0.13 (the Windows Desktop runtime pack ships none).
    private const string MitPermissionSha256 = "f9c4c77baa3828004ee54b8a4f2db2e88ed44a6237a493965bf551fac0fcb62d";
    private const string WpfNoticesSha256 = "d23dba2fee20c6b59d8f7088ee9ed372459c952a319b7ca90f3eec370cc76eb9";

    [Fact]
    public void ThePackageTableIsExactlyTheShippedPackages()
    {
        var (packages, _) = ShippedPackages();
        var listed = Rows().Select(r => $"{r.Groups["id"].Value} {r.Groups["version"].Value}").ToHashSet(StringComparer.Ordinal);
        var shipped = packages.Select(p => $"{p.Id} {p.Version}").ToHashSet(StringComparer.Ordinal);
        Assert.True(listed.SetEquals(shipped),
            "Run scripts/Write-ThirdPartyNotices.ps1. Missing: " + string.Join(", ", shipped.Except(listed)) +
            "; stale: " + string.Join(", ", listed.Except(shipped)));
    }

    // Each row's licence is what the package's .nuspec declares, its notices are every licence and notice file the
    // package ships, each copied byte for byte into licenses/packages, and that folder holds nothing else.
    [Fact]
    public void EachRowNamesItsLicenceAndTheCopiesMatchThePackages()
    {
        var (packages, cache) = ShippedPackages();
        var copies = Path.Combine(RepositoryRoot(), "licenses", "packages");
        var rows = Rows().ToDictionary(r => r.Groups["id"].Value, StringComparer.Ordinal);
        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, version) in packages)
        {
            var row = rows[id];
            var folder = Path.Combine(cache, id.ToLowerInvariant(), version);
            Assert.Equal(DeclaredLicence(Directory.GetFiles(folder, "*.nuspec").Single()), row.Groups["licence"].Value);

            var files = row.Groups["notices"].Value.Split(", ", StringSplitOptions.RemoveEmptyEntries);
            var shipped = Directory.GetFiles(folder).Where(f => LicenceFile().IsMatch(Path.GetFileName(f))).ToList();
            Assert.True(shipped.Count == files.Length, $"{id}: ships {shipped.Count} licence or notice files, the table names {files.Length}");
            foreach (var file in shipped)
            {
                var bytes = File.ReadAllBytes(file);
                Assert.True(files.Any(f => File.Exists(Path.Combine(copies, f)) && File.ReadAllBytes(Path.Combine(copies, f)).AsSpan().SequenceEqual(bytes)),
                    $"{id}: no byte-identical copy of {Path.GetFileName(file)} in licenses/packages");
            }
            named.UnionWith(files);
        }
        var present = Directory.GetFiles(copies).Select(f => Path.GetFileName(f)!).ToHashSet(StringComparer.Ordinal);
        Assert.True(present.SetEquals(named), "Stale files in licenses/packages: " + string.Join(", ", present.Except(named)));
    }

    // Every SPDX licence the table names has its text in licenses/, and the texts are the official ones.
    [Fact]
    public void EveryNamedLicenceHasItsOfficialText()
    {
        var licences = Path.Combine(RepositoryRoot(), "licenses");
        var expressions = Rows().Select(r => r.Groups["licence"].Value)
            .Where(l => !l.StartsWith('<') && !l.StartsWith("file: ", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.Contains("MPL-2.0", expressions);
        foreach (var expression in expressions)
            Assert.True(File.Exists(Path.Combine(licences, expression + ".txt")), $"licenses/{expression}.txt is missing");
        Assert.Equal(ApacheSha256, Sha256WithLf(Path.Combine(licences, "Apache-2.0.txt")));
        Assert.Equal(MplSha256, Sha256WithLf(Path.Combine(licences, "MPL-2.0.txt")));
        // K4os.Compression.LZ4 links its licence rather than naming it: MIT, so its holder is in MIT.txt.
        var mit = File.ReadAllText(Path.Combine(licences, "MIT.txt")).Replace("\r\n", "\n");
        foreach (var holder in new[] { ".NET Foundation and Contributors", "Milosz Krajewski", "Tamir Gal, Chris Morgan and others" })
            Assert.Contains(holder, mit, StringComparison.Ordinal);
        var permission = mit.IndexOf("Permission is hereby granted", StringComparison.Ordinal);
        Assert.True(permission >= 0, "licenses/MIT.txt has no MIT permission text");
        Assert.Equal(MitPermissionSha256, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(mit[permission..]))));
        Assert.Equal(WpfNoticesSha256, Sha256WithLf(Path.Combine(licences, "dotnet-wpf", "THIRD-PARTY-NOTICES.TXT")));
    }

    // BlazorWebView draws the see-through overlay with WebView2's composition control, which loads the Windows SDK
    // projection (Microsoft.Windows.SDK.NET) at run time: a plain net10.0-windows build left it out and the published
    // exe crashed on start. The app must keep a Windows 10 SDK target, and THIRD-PARTY-NOTICES.md must name the
    // projection and its licence.
    [Fact]
    public void TheProgramKeepsTheWindowsSdkProjectionAndNamesIt()
    {
        using var assets = AppAssets();
        var frameworks = assets.RootElement.GetProperty("project").GetProperty("frameworks").EnumerateObject().ToList();
        Assert.NotEmpty(frameworks);
        foreach (var framework in frameworks)
        {
            Assert.Contains("windows10.0.", framework.Name, StringComparison.OrdinalIgnoreCase);
            Assert.True(framework.Value.TryGetProperty("frameworkReferences", out var references)
                && references.EnumerateObject().Any(r => r.Name.StartsWith("Microsoft.Windows.SDK.NET.Ref", StringComparison.OrdinalIgnoreCase)),
                $"{framework.Name} does not bring in the Windows SDK projection WebView2's composition control needs.");
        }
        var notices = File.ReadAllText(Path.Combine(RepositoryRoot(), "THIRD-PARTY-NOTICES.md"));
        Assert.Contains("Microsoft.Windows.SDK.NET", notices, StringComparison.Ordinal);
        Assert.Contains("https://aka.ms/WinSDKLicenseURL", notices, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLicenceIsThePlainGplAndTheNoticesCreditUpstream()
    {
        var root = RepositoryRoot();
        Assert.Equal(GplSha256, Sha256WithLf(Path.Combine(root, "LICENSE")));

        var notice = File.ReadAllText(Path.Combine(root, "NOTICE"));
        var thirdParty = File.ReadAllText(Path.Combine(root, "THIRD-PARTY-NOTICES.md"));
        foreach (var text in new[] { notice, thirdParty })
        {
            Assert.Contains("https://github.com/Kuroukihime/AIon2-Dps-Meter", text, StringComparison.Ordinal);
            Assert.Contains("https://github.com/harrynguon/Scarlet-Meter", text, StringComparison.Ordinal);
            Assert.Contains("-source.zip", text, StringComparison.Ordinal);
        }
        Assert.Contains("NCSOFT", notice, StringComparison.Ordinal);
    }

    // The packages of the app restore's first target that ship something, and the NuGet folder they are in.
    private static (List<(string Id, string Version)> Packages, string Cache) ShippedPackages()
    {
        using var assets = AppAssets();
        var cache = assets.RootElement.GetProperty("project").GetProperty("restore").GetProperty("packagesPath").GetString()!;
        var target = assets.RootElement.GetProperty("targets").EnumerateObject().First();
        var packages = target.Value.EnumerateObject()
            .Where(p => p.Value.TryGetProperty("type", out var type) && type.GetString() == "package")
            .Where(p => ShippedAssets.Any(kind => p.Value.TryGetProperty(kind, out _)))
            .Select(p => p.Name.Split('/'))
            .Select(p => (p[0], p[1]))
            .ToList();
        Assert.NotEmpty(packages);
        return (packages, cache);
    }

    // The app's restore (src/ScarletMeter.App/obj/project.assets.json), written by any restore or build.
    private static JsonDocument AppAssets()
    {
        var assetsPath = Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App", "obj", "project.assets.json");
        Assert.True(File.Exists(assetsPath), $"No restore of the app at {assetsPath}.");
        return JsonDocument.Parse(File.ReadAllText(assetsPath));
    }

    // What Write-ThirdPartyNotices.ps1 writes for a package's licence: the expression, the shipped file, or the URL.
    private static string DeclaredLicence(string nuspec)
    {
        var metadata = XDocument.Load(nuspec).Root!.Elements().First(e => e.Name.LocalName == "metadata");
        var licence = metadata.Elements().FirstOrDefault(e => e.Name.LocalName == "license");
        if (licence is not null) return (string?)licence.Attribute("type") == "file" ? $"file: {licence.Value}" : licence.Value;
        var url = metadata.Elements().FirstOrDefault(e => e.Name.LocalName == "licenseUrl");
        return url is null ? "none declared" : $"<{url.Value}>";
    }

    // The package table's rows, between its markers.
    private static IEnumerable<Match> Rows()
    {
        var notices = File.ReadAllText(Path.Combine(RepositoryRoot(), "THIRD-PARTY-NOTICES.md"));
        var start = notices.IndexOf("<!-- packages:begin -->", StringComparison.Ordinal);
        var end = notices.IndexOf("<!-- packages:end -->", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "THIRD-PARTY-NOTICES.md has no package table markers.");
        return PackageRow().Matches(notices[start..end]);
    }

    private static string Sha256WithLf(string path) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(File.ReadAllText(path).Replace("\r\n", "\n"))));

    [GeneratedRegex(@"^\| (?<id>[^ |]+) \| (?<version>\d[^ |]*) \| (?<licence>[^|]*?) \| (?<notices>[^|]*?) \| (?<project>[^|]*?) \|\r?$", RegexOptions.Multiline)]
    private static partial Regex PackageRow();

    // The file names Write-ThirdPartyNotices.ps1 copies from a package's folder.
    [GeneratedRegex(@"^(LICEN[CS]E|NOTICE|THIRD-PARTY-NOTICES)", RegexOptions.IgnoreCase)]
    private static partial Regex LicenceFile();

    // The directory holding ScarletMeter.slnx, above the test assembly (never the working directory).
    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ScarletMeter.slnx"))) return dir.FullName;
        throw new InvalidOperationException("ScarletMeter.slnx was not found above " + AppContext.BaseDirectory);
    }
}
