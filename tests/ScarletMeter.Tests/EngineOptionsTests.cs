using System.IO;
using ScarletMeter.Engine;

namespace ScarletMeter.Tests;

/// <summary>Data lives at an absolute, per-user location by default and never at a relative one.</summary>
public sealed class EngineOptionsTests
{
    [Fact]
    public void DefaultDataDirectoryIsAbsoluteAndPerUser()
    {
        var options = new EngineOptions();
        Assert.True(Path.IsPathFullyQualified(options.DataDirectory));
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), options.DataDirectory);
        Assert.Equal("ScarletMeter", Path.GetFileName(options.DataDirectory));
        Assert.Equal(Path.Combine(options.DataDirectory, "combat-history.db"), options.HistoryDatabasePath);
    }

    [Fact]
    public void RelativeDataDirectoryIsRejected()
    {
        var options = new EngineOptions();
        Assert.Throws<ArgumentException>(() => options.DataDirectory = Path.Combine("data", "scarlet"));
        Assert.Equal(EngineOptions.DefaultDataDirectory, options.DataDirectory);
    }
}
