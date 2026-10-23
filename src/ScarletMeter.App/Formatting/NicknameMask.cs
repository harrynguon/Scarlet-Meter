namespace ScarletMeter.App.Formatting;

/// <summary>
/// Masks player names for screenshots and streams the way RATmeter does (upstream
/// AionDpsMeter.UI/Utils/NicknameObfuscator.cs), so a masked name reads the same in both meters: up to
/// two characters become stars, up to four keep the first character, and longer names keep the first two
/// with one stable digit (from an FNV-1a hash of the name) in the middle so different players stay apart.
/// </summary>
public static class NicknameMask
{
    public static string Mask(string? name)
    {
        if (string.IsNullOrEmpty(name)) return name ?? string.Empty;
        if (name.Length <= 2) return new string('*', name.Length);
        if (name.Length <= 4) return name[0] + new string('*', name.Length - 1);

        var masked = (name[..2] + new string('*', name.Length - 2)).ToCharArray();
        var digitIndex = 2 + (name.Length - 3) / 2;
        masked[digitIndex] = (char)('0' + Math.Abs(Fnv1a(name) % 10));
        return new string(masked);
    }

    // FNV-1a over UTF-16 code units, wrapped to a signed 32-bit value as upstream does.
    private static int Fnv1a(string text)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var c in text)
            {
                hash ^= c;
                hash *= 16777619u;
            }
            return (int)hash;
        }
    }
}
