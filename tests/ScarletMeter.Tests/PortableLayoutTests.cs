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

    public void Dispose() => directory.Dispose();

    // The directory holding ScarletMeter.slnx, above the test assembly (never the working directory).
    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ScarletMeter.slnx"))) return dir.FullName;
        throw new InvalidOperationException("ScarletMeter.slnx was not found above " + AppContext.BaseDirectory);
    }
}
