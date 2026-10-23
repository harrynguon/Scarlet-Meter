using System.Reflection;
using AionDpsMeter.Core.Models;
using AionDpsMeter.Services.Services.Session;
using ScarletMeter.App.Updates;
using ScarletMeter.Engine;

namespace ScarletMeter.Tests;

/// <summary>
/// The build stamp (src/ScarletMeter.App/Updates/BuildStamp.targets), its reader <see cref="BuildInfo"/> and the
/// release-tag rules the update check compares with.
/// </summary>
public sealed class BuildInfoTests
{
    // Set by CI after it fetches the release tag naming the pinned upstream commit (.github/workflows/ci.yml).
    private const string ExpectedReleaseVariable = "SCARLET_EXPECTED_UPSTREAM_RELEASE";

    [Fact]
    public void CurrentCarriesTheBuildStamp()
    {
        var build = BuildInfo.Current;

        // A clone with the submodule's tags stamps the release tag; a shallow one without them, the short commit.
        Assert.Matches(@"^(v[0-9]+(\.[0-9]+){3}|[0-9a-f]{7,40})\z", build.UpstreamRelease);
        Assert.Matches(@"^[0-9a-f]{7,40}\z", build.ScarletCommit);

        // CI names the tag it fetched, so there the stamp must be that release tag, not the commit fallback.
        var expected = Environment.GetEnvironmentVariable(ExpectedReleaseVariable);
        if (!string.IsNullOrEmpty(expected))
        {
            Assert.Equal(expected, build.UpstreamRelease);
            Assert.NotNull(build.UpstreamVersion);
        }
    }

    [Fact]
    public void AnAssemblyWithoutTheStampReadsAsUnknown()
    {
        var build = BuildInfo.FromAssembly(typeof(object).Assembly);
        Assert.Equal(BuildInfo.UnknownValue, build.UpstreamRelease);
        Assert.Equal(BuildInfo.UnknownValue, build.ScarletCommit);
        Assert.Null(build.UpstreamVersion);
    }

    [Fact]
    public void OnlyTheAppIsStamped()
    {
        // The stamp target is imported by the app's project alone: upstream builds as it ships, and the engine
        // needs no stamp. A move to a shared Directory.Build.props or .targets file would stamp these too.
        Assembly[] unstamped = [typeof(CharacterClass).Assembly, typeof(CombatSessionManager).Assembly, typeof(ICombatEngine).Assembly];
        foreach (var assembly in unstamped)
        {
            Assert.DoesNotContain(
                assembly.GetCustomAttributes<AssemblyMetadataAttribute>(),
                entry => entry.Key is BuildInfo.UpstreamReleaseKey or BuildInfo.ScarletCommitKey);
        }
        Assert.Contains(
            typeof(BuildInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>(),
            entry => entry.Key == BuildInfo.UpstreamReleaseKey);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("  ", "\t")]
    public void BlankValuesReadAsUnknown(string upstreamRelease, string scarletCommit)
    {
        var build = new BuildInfo(upstreamRelease, scarletCommit);
        Assert.Equal(BuildInfo.UnknownValue, build.UpstreamRelease);
        Assert.Equal(BuildInfo.UnknownValue, build.ScarletCommit);
    }

    [Fact]
    public void ACommitStampHasNoUpstreamVersion()
    {
        var build = new BuildInfo("57974f8", "9495922");
        Assert.Equal("57974f8", build.UpstreamRelease);
        Assert.Null(build.UpstreamVersion);
        Assert.Equal(new Version(1, 11, 4, 334), new BuildInfo("v1.11.4.334", "9495922").UpstreamVersion);
    }

    [Theory]
    [InlineData("v1.11.4.334", 1, 11, 4, 334)]
    [InlineData("v0.0.0.0", 0, 0, 0, 0)]
    [InlineData("v999999999.10.0.1", 999999999, 10, 0, 1)]
    public void ReleaseTagsParseAsVersions(string tag, int major, int minor, int build, int revision)
    {
        Assert.True(UpstreamReleaseTag.TryParse(tag, out var version));
        Assert.Equal(new Version(major, minor, build, revision), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("57974f8")]
    [InlineData("1.11.4.334")]
    [InlineData("V1.11.4.334")]
    [InlineData("v1.11.4")]
    [InlineData("v1.11.4.334.1")]
    [InlineData("v1.11.4.334-beta")]
    [InlineData(" v1.11.4.334")]
    [InlineData("v1.11.4.334\n")]
    [InlineData("v1.11.4.3a4")]
    [InlineData("v1.11.4.\uFF13\uFF13\uFF14")] // full-width digits: \d would accept them
    [InlineData("v1.11.4.9999999999")] // does not fit an int
    public void OtherStampsAreNotReleaseTags(string? tag)
    {
        Assert.False(UpstreamReleaseTag.TryParse(tag, out var version));
        Assert.Null(version);
    }
}
