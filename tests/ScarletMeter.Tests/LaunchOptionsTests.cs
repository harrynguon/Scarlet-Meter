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
        Assert.Null(options.DataDirectory);
    }

    [Fact]
    public void ParsesNoCaptureAndAbsoluteDataDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "scarlet");
        var options = LaunchOptions.Parse(["--no-capture", "--data-dir", directory]);
        Assert.True(options.NoCapture);
        Assert.Equal(directory, options.DataDirectory);
    }

    [Fact]
    public void RejectsRelativeDataDirectoryMissingValueAndUnknownSwitches()
    {
        Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["--data-dir", "data"]));
        Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["--data-dir"]));
        Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["--capture"]));
    }
}
