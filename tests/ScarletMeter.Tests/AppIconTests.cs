using System.IO;

namespace ScarletMeter.Tests;

/// <summary>
/// The meter's icon: one multi-size .ico with every size Windows draws (16 to 256 px, PNG entries), set as the exe's
/// icon and taken by every window, with its source named in THIRD-PARTY-NOTICES.md. The checkout is found from the
/// test assembly's own location, never the working directory.
/// </summary>
public sealed class AppIconTests
{
    private static readonly byte[] PngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public void TheIconHoldsEverySizeWindowsDraws()
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App", "Assets", "ScarletMeter.ico"));
        Assert.Equal(0, BitConverter.ToUInt16(bytes, 0)); // reserved
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2)); // an icon, not a cursor
        var count = BitConverter.ToUInt16(bytes, 4);
        var sizes = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var entry = 6 + 16 * i;
            var size = bytes[entry] == 0 ? 256 : bytes[entry];
            Assert.Equal(bytes[entry], bytes[entry + 1]); // square
            var length = BitConverter.ToInt32(bytes, entry + 8);
            var offset = BitConverter.ToInt32(bytes, entry + 12);
            Assert.True(offset > 0 && offset + length <= bytes.Length, $"the {size} px entry lies outside the file");
            Assert.Equal(PngSignature, bytes.AsSpan(offset, PngSignature.Length).ToArray());
            sizes.Add(size);
        }
        Assert.Equal(new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 }, sizes.Order());
    }

    [Fact]
    public void TheExeAndEveryWindowUseIt()
    {
        var app = Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App");
        var project = File.ReadAllText(Path.Combine(app, "ScarletMeter.App.csproj"));
        Assert.Contains(@"<ApplicationIcon>Assets\ScarletMeter.ico</ApplicationIcon>", project);
        Assert.Contains(@"<Resource Include=""Assets\ScarletMeter.ico"" />", project);
        Assert.Contains("pack://application:,,,/Assets/ScarletMeter.ico", File.ReadAllText(Path.Combine(app, "Windows", "AppIcon.cs")));
        // ScarletWindow covers the overlay and the tool windows; the boss-timers window is a plain Window.
        foreach (var window in new[] { "ScarletWindow.cs", "BossTimersWindow.cs" })
            Assert.Contains("if (AppIcon.Source is { } icon) Icon = icon;", File.ReadAllText(Path.Combine(app, "Windows", window)));
    }

    [Fact]
    public void TheNoticesSayWhereTheIconCameFrom()
    {
        var notices = File.ReadAllText(Path.Combine(RepositoryRoot(), "THIRD-PARTY-NOTICES.md"));
        Assert.Contains("## Artwork", notices);
        Assert.Contains("src/ScarletMeter.App/Assets/ScarletMeter.ico", notices);
        Assert.Contains("AI-generated", notices);
        Assert.Contains("GPT 6.1 Astra", notices);
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ScarletMeter.slnx"))) return dir.FullName;
        throw new InvalidOperationException("ScarletMeter.slnx was not found above " + AppContext.BaseDirectory);
    }
}
