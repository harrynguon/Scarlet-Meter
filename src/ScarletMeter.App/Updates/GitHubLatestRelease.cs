using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScarletMeter.App.Updates;

/// <summary>
/// GitHub's "latest release" response for upstream's repository, parsed and validated. The response is untrusted:
/// only tag_name, html_url and published_at are read, the tag must be a release tag, and the page must be an https
/// page on upstream's repository, so the UI can show the tag and open the page safely.
/// </summary>
internal sealed partial record GitHubLatestRelease(string Tag, Version Version, Uri ReleasePage, DateTimeOffset? PublishedAt)
{
    /// <summary>Every accepted release page starts with this, compared after URI normalisation.</summary>
    public const string ReleasePagePrefix = "https://github.com/Kuroukihime/AIon2-Dps-Meter/";

    /// <summary>
    /// Parses a response body. On failure <paramref name="problem"/> says what was wrong without echoing any of
    /// the body. A missing or unparsable published_at is not a failure; it reads as null.
    /// </summary>
    public static bool TryParse(
        ReadOnlyMemory<byte> json,
        [NotNullWhen(true)] out GitHubLatestRelease? release,
        [NotNullWhen(false)] out string? problem)
    {
        release = null;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            problem = "GitHub's answer was not valid JSON.";
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                problem = "GitHub's answer was not a release.";
                return false;
            }
            if (!TryGetString(root, "tag_name", out var tag) || !UpstreamReleaseTag.TryParse(tag, out var version))
            {
                problem = "GitHub's answer had no valid release tag.";
                return false;
            }
            if (!TryGetString(root, "html_url", out var page) || !TryParseReleasePage(page, out var releasePage))
            {
                problem = "GitHub's answer pointed outside upstream's repository.";
                return false;
            }

            DateTimeOffset? publishedAt = root.TryGetProperty("published_at", out var published)
                && published.ValueKind == JsonValueKind.String
                && published.TryGetDateTimeOffset(out var date)
                    ? date
                    : null;
            release = new GitHubLatestRelease(tag, version, releasePage, publishedAt);
            problem = null;
            return true;
        }
    }

    private static bool TryGetString(JsonElement element, string name, [NotNullWhen(true)] out string? value)
    {
        value = element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
        return value is not null;
    }

    // An absolute https URL whose normalised form (host lower-cased, dot segments resolved, user info and port
    // shown) starts with the prefix, so the host is github.com on the default port with no user info, followed
    // only by plain path characters: no query, fragment or percent-encoding, which could hide a "..".
    private static bool TryParseReleasePage(string value, [NotNullWhen(true)] out Uri? page)
    {
        page = Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.AbsoluteUri.StartsWith(ReleasePagePrefix, StringComparison.Ordinal)
            && PagePath().IsMatch(uri.AbsoluteUri.AsSpan(ReleasePagePrefix.Length))
                ? uri
                : null;
        return page is not null;
    }

    [GeneratedRegex(@"^[A-Za-z0-9._/-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex PagePath();
}
