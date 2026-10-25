using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace ScarletMeter.Tests;

/// <summary>
/// The meter's icon: one multi-size .ico with every size Windows draws (16 to 256 px, PNG entries that decode at the
/// size the file says), built into the exe, and set on no window, since WPF gives a window without an Icon the exe's
/// own: so every window shows it, one added later included. Its source is named in THIRD-PARTY-NOTICES.md. The
/// checkout is found from the test assembly's own location, never the working directory.
/// </summary>
public sealed partial class AppIconTests
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
    private static readonly byte[] PngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly IntPtr GroupIcon = 14; // RT_GROUP_ICON

    // The directory is checked against each PNG's own header, and WPF's decoder, which skips an entry it cannot read,
    // must return every one at its size (deep audit on PR #32: swapped or broken entries passed the first test).
    [Fact]
    public void TheIconHoldsEverySizeWindowsDraws()
    {
        var bytes = File.ReadAllBytes(IconPath());
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
            // IHDR follows the signature: its length, "IHDR", then width and height, big-endian.
            Assert.Equal(size, BigEndian(bytes, offset + 16));
            Assert.Equal(size, BigEndian(bytes, offset + 20));
            sizes.Add(size);
        }
        Assert.Equal(Sizes, sizes.Order());

        using var stream = new MemoryStream(bytes);
        var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        Assert.Equal(Sizes, decoder.Frames.Select(f => f.PixelWidth).Order());
        Assert.All(decoder.Frames, f => Assert.Equal(f.PixelWidth, f.PixelHeight));
    }

    // The exe, and the app assembly its resources are copied from, carry the icon with every size: what Explorer, the
    // taskbar and, through WPF's default, every window show.
    [Theory]
    [InlineData("ScarletMeter.exe")]
    [InlineData("ScarletMeter.dll")]
    public void TheBuiltMeterCarriesIt(string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, file);
        Assert.True(File.Exists(path), $"{file} is not beside the tests");
        Assert.Equal(Sizes, IconGroupSizes(path).Order());
    }

    // Every window takes the exe's icon because none sets its own, so none can miss it or load it differently.
    [Fact]
    public void NoWindowSetsItsOwnIcon()
    {
        var app = Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App");
        var project = File.ReadAllText(Path.Combine(app, "ScarletMeter.App.csproj"));
        Assert.Contains(@"<ApplicationIcon>Assets\ScarletMeter.ico</ApplicationIcon>", project);
        Assert.DoesNotContain("ScarletMeter.ico\" />", project); // no second copy as a resource

        var sources = Directory.EnumerateFiles(app, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();
        Assert.Contains(sources, f => f.EndsWith("ScarletWindow.cs", StringComparison.Ordinal));
        var setters = sources
            .Where(f => IconSetter().IsMatch(WithoutComments(File.ReadAllText(f))))
            .Select(f => Path.GetRelativePath(app, f))
            .ToList();
        Assert.Empty(setters);
    }

    [Fact]
    public void TheNoticesSayWhereTheIconCameFrom()
    {
        var notices = File.ReadAllText(Path.Combine(RepositoryRoot(), "THIRD-PARTY-NOTICES.md"));
        Assert.Contains("## Artwork", notices);
        Assert.Contains("src/ScarletMeter.App/Assets/ScarletMeter.ico", notices);
        Assert.Contains("src/ScarletMeter.App/wwwroot/images/avatar.png", notices); // the header's face, from the same portrait
        Assert.Contains("AI-generated", notices);
        Assert.Contains("GPT 6.1 Astra", notices);
    }

    // The sizes in a module's first icon group, read from its resources as data, without running it.
    private static List<int> IconGroupSizes(string path)
    {
        const uint asDataFile = 0x2, asImageResource = 0x20;
        var module = LoadLibraryExW(path, IntPtr.Zero, asDataFile | asImageResource);
        Assert.NotEqual(IntPtr.Zero, module);
        try
        {
            var sizes = new List<int>();
            var groups = 0;
            EnumResourceNamesW(module, GroupIcon, (m, type, name, _) =>
            {
                if (++groups > 1) return false;
                var info = FindResourceW(m, name, type);
                var group = new byte[SizeofResource(m, info)];
                Marshal.Copy(LockResource(LoadResource(m, info)), group, 0, group.Length);
                // GRPICONDIR: reserved, type, count, then 14-byte entries that start with width and height.
                for (var i = 0; i < BitConverter.ToUInt16(group, 4); i++)
                {
                    var width = group[6 + 14 * i];
                    sizes.Add(width == 0 ? 256 : width);
                }
                return true;
            }, IntPtr.Zero);
            Assert.True(groups >= 1, $"{Path.GetFileName(path)} has no icon");
            return sizes;
        }
        finally
        {
            FreeLibrary(module);
        }
    }

    private delegate bool EnumResourceNames(IntPtr module, IntPtr type, IntPtr name, IntPtr parameter);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);

    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr module);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumResourceNamesW(IntPtr module, IntPtr type, EnumResourceNames callback, IntPtr parameter);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindResourceW(IntPtr module, IntPtr name, IntPtr type);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LoadResource(IntPtr module, IntPtr info);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LockResource(IntPtr data);

    [DllImport("kernel32.dll")]
    private static extern uint SizeofResource(IntPtr module, IntPtr info);

    private static int BigEndian(byte[] bytes, int at) => (bytes[at] << 24) | (bytes[at + 1] << 16) | (bytes[at + 2] << 8) | bytes[at + 3];

    // Source text without its comments, so a commented-out line neither counts nor hides one. "://" in a URL is kept.
    private static string WithoutComments(string source) =>
        LineComment().Replace(BlockComment().Replace(source, string.Empty), string.Empty);

    // "Icon = …" in C# (a property set or an initializer) or Icon="…" in XAML; not "SkillIcon =" or "Icon ==".
    [GeneratedRegex(@"(?<!\w)Icon\s*=(?!=)")]
    private static partial Regex IconSetter();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockComment();

    [GeneratedRegex(@"(?m)(?<!:)//.*$")]
    private static partial Regex LineComment();

    private static string IconPath() => Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App", "Assets", "ScarletMeter.ico");

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ScarletMeter.slnx"))) return dir.FullName;
        throw new InvalidOperationException("ScarletMeter.slnx was not found above " + AppContext.BaseDirectory);
    }
}
