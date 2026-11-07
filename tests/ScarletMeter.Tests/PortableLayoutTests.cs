using System.IO;
using ScarletMeter.App;

namespace ScarletMeter.Tests;

/// <summary>
/// The portable layout: a marker beside the executable puts the data folder beside it, its absence leaves the
/// per-user default, and Publish-Portable.ps1 uses the marker and data folder names the app uses.
/// </summary>
public sealed class PortableLayoutTests : IDisposable
{
    private readonly TestDataDirectory directory = new();

    [Fact]
    public void AMarkerBesideTheExecutableKeepsTheDataBesideIt()
    {
        Directory.CreateDirectory(directory.FullPath); // TestDataDirectory names the folder; the test makes it
        File.WriteAllText(Path.Combine(directory.FullPath, PortableLayout.MarkerFileName), "portable");
        Assert.Equal(Path.Combine(directory.FullPath, "data"), PortableLayout.DataDirectory(directory.FullPath));
    }

    [Fact]
    public void WithoutTheMarkerTheMeterIsNotPortable()
    {
        Assert.Null(PortableLayout.DataDirectory(directory.FullPath));
        Directory.CreateDirectory(Path.Combine(directory.FullPath, PortableLayout.MarkerFileName)); // a folder is no marker
        Assert.Null(PortableLayout.DataDirectory(directory.FullPath));
    }

    [Fact]
    public void TheBaseDirectoryMustBeAbsolute() =>
        Assert.Throws<ArgumentException>(() => PortableLayout.DataDirectory("relative"));

    [Fact]
    public void TheTestRunIsNotPortable() =>
        Assert.False(PortableLayout.IsCurrent); // no marker beside the test assembly

    // Publish-Portable.ps1 takes both names from PortableFolders.ps1: the marker it writes, and the data folder it
    // carries into each new build and never deletes.
    [Fact]
    public void PublishPortableUsesTheMarkerAndDataFolderTheAppUses()
    {
        var folders = File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "PortableFolders.ps1"));
        Assert.Contains($"$PortableMarker = '{PortableLayout.MarkerFileName}'", folders, StringComparison.Ordinal);
        Assert.Contains($"$PortableDataFolder = '{PortableLayout.DataFolderName}'", folders, StringComparison.Ordinal);
        var publish = File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "Publish-Portable.ps1"));
        Assert.Contains("PortableFolders.ps1", publish, StringComparison.Ordinal);
    }

    // Publish-Portable.ps1 refuses a folder where Windows would start the meter below Medium integrity, where it
    // cannot start: the output root before it restores a cut-short update or builds, and the built exe inside the
    // block that drops a failed staging folder, before the swap makes it the live copy. Update-Meter.ps1 asks before
    // its own build. Test-PortableFolders.ps1 checks the refusal itself and the root's place; this pins where the
    // scripts ask, which for the built exe and Update-Meter.ps1 only a full build would reach.
    [Fact]
    public void PublishPortableChecksTheIntegrityLevelBeforeBuildingAndBeforeTheSwap()
    {
        // Line ends folded, so the pin holds however git checked the scripts out.
        var publish = File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "Publish-Portable.ps1")).ReplaceLineEndings("\n");
        var rootCheck = At(publish, "    Assert-PortableRootStartsAtMedium $OutputRoot\n");
        var restore = At(publish, "$restored = Restore-InterruptedPortableUpdate $folders");
        var build = At(publish, "& (Join-Path $PSScriptRoot 'Build.ps1') -Configuration Release");
        Assert.True(rootCheck < restore && restore < build, "the output root is not checked before the restore and the build");

        var layout = At(publish, "& (Join-Path $PSScriptRoot 'Test-PublishLayout.ps1') -PublishDirectory $folders.Staging", build);
        var exeCheck = At(publish, "Assert-PortableStartsAtMedium (Join-Path $folders.Staging 'ScarletMeter.exe') $OutputRoot", build);
        var dropStaging = At(publish, "# The live copy is untouched; drop the half-made staging folder", build);
        var swap = At(publish, "Invoke-PortableSwap $folders", build);
        Assert.True(layout < exeCheck && exeCheck < dropStaging && dropStaging < swap,
            "the built exe is not checked between the layout check and the staging folder's catch, before the swap");

        var update = File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "Update-Meter.ps1")).ReplaceLineEndings("\n");
        var updateCheck = At(update, "\nAssert-PortableRootStartsAtMedium $portable.Root\n");
        var upstream = At(update, "& (Join-Path $work 'scripts\\Update-Upstream.ps1') -Tag $Tag");
        Assert.True(updateCheck < upstream, "Update-Meter.ps1 does not check the portable folder before its build");
    }

    // Where the text first stands in the script, at or after a position; fails, naming the text, when it is not there.
    private static int At(string script, string text, int from = 0)
    {
        var index = script.IndexOf(text, from, StringComparison.Ordinal);
        Assert.True(index >= 0, $"The script no longer holds: {text}");
        return index;
    }

    public void Dispose() => directory.Dispose();

    // The directory holding ScarletMeter.slnx, above the test assembly (never the working directory).
    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ScarletMeter.slnx"))) return dir.FullName;
        throw new InvalidOperationException("ScarletMeter.slnx was not found above " + AppContext.BaseDirectory);
    }
}
