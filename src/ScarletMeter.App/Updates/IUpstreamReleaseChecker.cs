namespace ScarletMeter.App.Updates;

/// <summary>
/// Tells whether upstream RATmeter has published a release newer than the one this build is pinned to, which is
/// when parser fixes for a game patch arrive. Implemented by <see cref="UpstreamReleaseChecker"/>; the UI
/// depends on this interface so it can be faked.
/// </summary>
public interface IUpstreamReleaseChecker
{
    /// <summary>The result of the most recent completed check, or null before one has completed.</summary>
    UpstreamReleaseCheckResult? LastResult { get; }

    /// <summary>
    /// Checks upstream's latest release, or returns the cached result when a check started less than
    /// <see cref="UpstreamReleaseChecker.CheckInterval"/> ago; concurrent callers share one request. Never
    /// throws: every failure, the caller's cancellation included, is an <see cref="UpstreamReleaseCheckResult.Unknown"/>.
    /// </summary>
    Task<UpstreamReleaseCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}
