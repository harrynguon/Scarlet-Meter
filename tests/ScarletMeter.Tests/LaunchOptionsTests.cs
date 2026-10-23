using System.IO;
using ScarletMeter.App;

namespace ScarletMeter.Tests;

public sealed class LaunchOptionsTests
{
    [Fact]
    public void DefaultsToLiveCaptureInTheDefaultDirectory()
    {
        var options = LaunchOptions.Parse([]);
        Assert.False(options.NoCapture);
        Assert.False(options.Demo);
        Assert.Null(options.DataDirectory);
    }

    [Fact]
    public void ParsesNoCaptureAndAbsoluteDataDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "scarlet");
        var options = LaunchOptions.Parse(["--no-capture", "--data-dir", directory]);
        Assert.True(options.NoCapture);
        Assert.False(options.Demo);
        Assert.Equal(directory, options.DataDirectory);
    }

    [Fact]
    public void ParsesDemoWithADataDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "scarlet");
        var options = LaunchOptions.Parse(["--demo", "--data-dir", directory]);
        Assert.True(options.Demo);
        Assert.False(options.NoCapture);
        Assert.Equal(directory, options.DataDirectory);
    }

    [Fact]
    public void DemoAndNoCaptureExcludeEachOther()
    {
        // Either order: the switches name two different capture modes.
        var demoFirst = Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["--demo", "--no-capture"]));
        Assert.Contains("--demo and --no-capture", demoFirst.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["--no-capture", "--demo"]));
    }

    [Fact]
    public void RejectsRelativeDataDirectoryMissingValueAndUnknownSwitches()
    {
        Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["--data-dir", "data"]));
        Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["--data-dir"]));
        var unknown = Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["--capture"]));
        Assert.Contains("--demo", unknown.Message, StringComparison.Ordinal); // the message lists every switch
    }
}
