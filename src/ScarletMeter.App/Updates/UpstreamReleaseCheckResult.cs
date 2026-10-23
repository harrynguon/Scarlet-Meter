namespace ScarletMeter.App.Updates;

/// <summary>
/// The outcome of comparing the upstream release this build is pinned to with upstream's latest published
/// release: exactly one of <see cref="UpToDate"/>, <see cref="NewerAvailable"/> or <see cref="Unknown"/>.
/// Tags are validated release tags and <see cref="NewerAvailable.ReleasePage"/> is a page on upstream's GitHub
/// repository, so both are safe to show and to open.
/// </summary>
public abstract record UpstreamReleaseCheckResult
{
    private UpstreamReleaseCheckResult()
    {
    }

    /// <summary>Upstream's latest release is the pinned one, or older.</summary>
    public sealed record UpToDate(string PinnedRelease, string LatestRelease) : UpstreamReleaseCheckResult;

    /// <summary>
    /// Upstream published a newer release than the pinned one: time to move the submodule
    /// (scripts/Update-Upstream.ps1) and reinstall. <paramref name="PublishedAt"/> is null when GitHub gave no
    /// usable date.
    /// </summary>
    public sealed record NewerAvailable(string PinnedRelease, string Tag, Uri ReleasePage, DateTimeOffset? PublishedAt)
        : UpstreamReleaseCheckResult;

    /// <summary>No verdict. <paramref name="Reason"/> is one plain sentence, fit for a tooltip or the log.</summary>
    public sealed record Unknown(UpstreamReleaseCheckFailure Failure, string Reason) : UpstreamReleaseCheckResult;
}

/// <summary>Why an upstream release check reached no verdict.</summary>
public enum UpstreamReleaseCheckFailure
{
    /// <summary>The build stamped a commit or "unknown" rather than a release tag, so nothing was requested.</summary>
    PinnedReleaseUnknown,

    /// <summary>GitHub did not answer within <see cref="UpstreamReleaseChecker.RequestTimeout"/>.</summary>
    Timeout,

    /// <summary>No connection, a DNS or TLS failure, or a broken response stream.</summary>
    Network,

    /// <summary>GitHub's limit for unauthenticated requests from this address was reached (HTTP 403 or 429).</summary>
    RateLimited,

    /// <summary>HTTP 404: upstream has no published release, or the repository moved or is gone.</summary>
    NotFound,

    /// <summary>Any other HTTP status, redirects included: they are not followed.</summary>
    HttpError,

    /// <summary>
    /// The response was not a release as expected: malformed or oversized JSON, an invalid tag, or a release page
    /// outside upstream's repository.
    /// </summary>
    InvalidResponse,

    /// <summary>The caller stopped waiting. The shared request carries on, and its result is cached.</summary>
    Cancelled,

    /// <summary>Anything else; the exception is in the log.</summary>
    Unexpected,
}
