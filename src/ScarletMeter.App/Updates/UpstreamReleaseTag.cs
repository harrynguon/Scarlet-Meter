using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace ScarletMeter.App.Updates;

/// <summary>
/// Upstream RATmeter's release tags, such as v1.11.4.334: a "v" and exactly four numeric parts, compared as
/// numbers (v1.10.0.1 is newer than v1.9.4.277). Anything else, a short commit or "unknown" included, is not a
/// release tag.
/// </summary>
public static partial class UpstreamReleaseTag
{
    /// <summary>
    /// Parses a release tag into its version. ASCII digits only, at most nine per part so each fits an int, and
    /// nothing after the last part (\z, because $ would also accept a trailing newline).
    /// </summary>
    public static bool TryParse(string? tag, [NotNullWhen(true)] out Version? version)
    {
        version = null;
        return tag is not null && Pattern().IsMatch(tag) && Version.TryParse(tag.AsSpan(1), out version);
    }

    [GeneratedRegex(@"^v[0-9]{1,9}(\.[0-9]{1,9}){3}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
