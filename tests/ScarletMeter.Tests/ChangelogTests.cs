using System.IO;
using System.Text.RegularExpressions;

namespace ScarletMeter.Tests;

/// <summary>
/// CHANGELOG.md keeps the shape its readers and the release workflow rely on (AGENTS.md, "Changelog"; Harry asked for
/// each release to list what changed, and for one changelog over all of them, 2026-10-26): Unreleased first, then
/// every release newest first with its date, each with its release notes and the app's version among them, changes
/// in Keep a Changelog's groups with their pull requests, and a link for every heading.
/// </summary>
public sealed class ChangelogTests
{
    private const string PullLink = @"\(\[#\d+\]\(https://github\.com/harrynguon/Scarlet-Meter/pull/\d+\)";

    // "## [Unreleased]" or "## [1.2.3] - 2026-10-26": the second-level headings, in file order.
    private static IReadOnlyList<Match> Headings(string changelog) =>
        Regex.Matches(changelog, @"(?m)^## \[(?<name>[^\]]+)\](?: - (?<date>\S+))?\r?$").ToList();

    [Fact]
    public void UnreleasedComesFirstThenEveryReleaseNewestFirst()
    {
        var headings = Headings(Changelog());
        Assert.True(headings.Count >= 2, "Unreleased and at least one release");
        Assert.Equal("Unreleased", headings[0].Groups["name"].Value);
        Assert.False(headings[0].Groups["date"].Success, "Unreleased has no date");
        Assert.Single(headings, h => h.Groups["name"].Value == "Unreleased");

        var releases = headings.Skip(1).ToList();
        Assert.All(releases, h =>
        {
            Assert.Matches(@"^\d+\.\d+\.\d+$", h.Groups["name"].Value);
            Assert.True(DateOnly.TryParseExact(h.Groups["date"].Value, "yyyy-MM-dd", out _), $"{h.Groups["name"].Value} has a date");
        });
        var versions = releases.Select(h => Version.Parse(h.Groups["name"].Value)).ToList();
        Assert.Equal(versions.OrderDescending(), versions);
        Assert.Equal(versions.Count, versions.Distinct().Count());
        var dates = releases.Select(h => DateOnly.ParseExact(h.Groups["date"].Value, "yyyy-MM-dd")).ToList();
        Assert.Equal(dates.OrderDescending(), dates);
    }

    // Every release has notes (docs/release-notes, which the release workflow requires) and a section; the app's version
    // has its section once its notes are written, which is when it is being released.
    [Fact]
    public void EveryReleaseHasItsNotesAndItsSection()
    {
        var sections = Headings(Changelog()).Skip(1).Select(h => h.Groups["name"].Value).ToHashSet();
        var notes = Directory.GetFiles(Path.Combine(RepositoryRoot(), "docs", "release-notes"), "v*.md")
            .Select(path => Path.GetFileNameWithoutExtension(path)[1..]).ToHashSet();
        Assert.NotEmpty(notes);
        Assert.Equal(notes.Order(), sections.Order());

        var props = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Directory.Build.props"));
        var version = Regex.Match(props, @"<Version>(?<v>[^<]+)</Version>").Groups["v"].Value;
        Assert.Matches(@"^\d+\.\d+\.\d+$", version);
        if (notes.Contains(version)) Assert.Contains(version, sections);
    }

    // Changes sit under Keep a Changelog's groups, each a bullet naming its pull requests; every released section lists
    // at least one. Unreleased may be empty right after a release.
    [Fact]
    public void ChangesSitInTheirGroupsWithTheirPullRequests()
    {
        var changelog = Changelog();
        var groups = Regex.Matches(changelog, @"(?m)^### (?<group>.+?)\r?$").Select(m => m.Groups["group"].Value).ToList();
        Assert.NotEmpty(groups);
        Assert.All(groups, group => Assert.Contains(group, new[] { "Added", "Changed", "Deprecated", "Removed", "Fixed", "Security" }));

        var headings = Headings(changelog);
        var end = changelog.IndexOf("\n[Unreleased]:", StringComparison.Ordinal);
        Assert.True(end > 0, "the links come last");
        for (var i = 0; i < headings.Count; i++)
        {
            var name = headings[i].Groups["name"].Value;
            var to = i + 1 < headings.Count ? headings[i + 1].Index : end;
            var body = changelog[(headings[i].Index + headings[i].Length)..to];
            // A change is a bullet with its continuation lines, up to the next bullet, group, blank line or the end.
            var changes = Regex.Matches(body, @"(?ms)^- .*?(?=^- |^###|^\s*$|\z)").Select(m => m.Value).ToList();
            if (name != "Unreleased") Assert.NotEmpty(changes);
            Assert.All(changes, change =>
            {
                Assert.Matches(PullLink, change);
                // Each link's number is its pull request's: "[#39](.../pull/39)", never "[#39](.../pull/38)".
                Assert.DoesNotMatch(@"\[#(\d+)\]\(https://github\.com/harrynguon/Scarlet-Meter/pull/(?!\1\))\d+\)", change);
            });
            // A section's first line is a group, so no change sits outside one (audit on PR #41: a bullet before the
            // first group passed while any group followed it).
            if (body.Trim().Length > 0) Assert.StartsWith("### ", body.TrimStart());
        }
    }

    // Each heading links to its changes: Unreleased from the latest release to HEAD, a release from the one before it,
    // the first to its release page.
    [Fact]
    public void EveryHeadingHasItsLink()
    {
        var changelog = Changelog();
        var releases = Headings(changelog).Skip(1).Select(h => h.Groups["name"].Value).ToList();
        const string Repo = "https://github.com/harrynguon/Scarlet-Meter";
        Assert.Contains($"[Unreleased]: {Repo}/compare/v{releases[0]}...HEAD", changelog);
        for (var i = 0; i < releases.Count - 1; i++)
            Assert.Contains($"[{releases[i]}]: {Repo}/compare/v{releases[i + 1]}...v{releases[i]}", changelog);
        Assert.Contains($"[{releases[^1]}]: {Repo}/releases/tag/v{releases[^1]}", changelog);
    }

    // The release workflow refuses a tag whose version has no section and adds the section to the release's notes;
    // CI runs the same script for the app's version, and its cases on throwaway changelogs (Test-ChangelogSection.ps1).
    [Fact]
    public void TheWorkflowsReadTheChangelog()
    {
        var workflows = Path.Combine(RepositoryRoot(), ".github", "workflows");
        var release = File.ReadAllText(Path.Combine(workflows, "release.yml"));
        Assert.Contains("$null = & (Join-Path $env:GITHUB_WORKSPACE 'scripts/Get-ChangelogSection.ps1') -Version $version", release);
        Assert.Contains("$changes = & (Join-Path $env:GITHUB_WORKSPACE 'scripts/Get-ChangelogSection.ps1') -Version $env:SCARLET_VERSION", release);
        Assert.Matches(@"WriteAllText\(\(Join-Path \$out 'notes\.md'\),\s*""\$notesText`n`n## What's changed in \$env:SCARLET_VERSION`n`n\$changes", release);
        Assert.DoesNotContain("Copy-Item -LiteralPath $env:SCARLET_NOTES", release);
        var ci = File.ReadAllText(Path.Combine(workflows, "ci.yml"));
        Assert.Contains("& \"${{ github.workspace }}/scripts/Get-ChangelogSection.ps1\" -Version $version", ci);
        Assert.Contains("run: '& \"${{ github.workspace }}/scripts/Test-ChangelogSection.ps1\"'", ci);
        // The refusal runs in the tag's first check, before anything is built.
        Assert.True(release.IndexOf("Get-ChangelogSection.ps1') -Version $version", StringComparison.Ordinal)
            < release.IndexOf("scripts/Build.ps1", StringComparison.Ordinal), "the changelog is checked before the build");
    }

    private static string Changelog() => File.ReadAllText(Path.Combine(RepositoryRoot(), "CHANGELOG.md")).Replace("\r\n", "\n");

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ScarletMeter.slnx"))) return dir.FullName;
        throw new InvalidOperationException("ScarletMeter.slnx was not found above " + AppContext.BaseDirectory);
    }
}
