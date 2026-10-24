using System.IO;
using ScarletMeter.App;

namespace ScarletMeter.Tests;

/// <summary>
/// The portable layout: a marker beside the executable puts the data folder beside it, its absence leaves the
/// per-user default, and Publish-Portable.ps1 writes the marker under the name the app looks for.
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

    [Fact]
    public void PublishPortableWritesTheMarkerTheAppLooksFor()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "Publish-Portable.ps1"));
        Assert.Contains($"$portableMarker = '{PortableLayout.MarkerFileName}'", script, StringComparison.Ordinal);
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
