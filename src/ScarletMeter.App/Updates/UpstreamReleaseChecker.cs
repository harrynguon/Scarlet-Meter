using System.IO;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using static ScarletMeter.App.Updates.UpstreamReleaseCheckResult;

namespace ScarletMeter.App.Updates;

/// <summary>
/// Asks GitHub for upstream RATmeter's latest release and compares it with the release this build is pinned to
/// (<see cref="BuildInfo.UpstreamRelease"/>). One unauthenticated GET to <see cref="LatestReleaseApi"/> with a
/// User-Agent and an Accept header and nothing else: no token, no cookies, no data about the player or the
/// game. At most one request per <see cref="CheckInterval"/> per process; the last result is cached in memory
/// only. A build that does not know its pinned release makes no request at all. Nothing is requested until
/// <see cref="CheckAsync"/> is called, so the UI decides whether checks happen (a settings switch).
/// </summary>
public sealed class UpstreamReleaseChecker : IUpstreamReleaseChecker, IDisposable
{
    /// <summary>The only URL this checker requests.</summary>
    public static Uri LatestReleaseApi { get; } = new("https://api.github.com/repos/Kuroukihime/AIon2-Dps-Meter/releases/latest");

    /// <summary>The minimum time between two requests, measured from the start of the previous one.</summary>
    public static TimeSpan CheckInterval { get; } = TimeSpan.FromHours(6);

    /// <summary>How long one request, headers and body, may take.</summary>
    public static TimeSpan RequestTimeout { get; } = TimeSpan.FromSeconds(10);

    /// <summary>A larger answer is rejected unread; upstream's release responses are tens of kilobytes.</summary>
    public const int MaxResponseBytes = 1024 * 1024;

    private const string UserAgent = "ScarletMeter-UpstreamReleaseCheck";

    private readonly HttpClient client;
    private readonly BuildInfo build;
    private readonly TimeProvider time;
    private readonly ILogger<UpstreamReleaseChecker> logger;
    private readonly Lock gate = new();
    private Task<UpstreamReleaseCheckResult>? latestCheck; // in flight or completed; guarded by gate
    private long latestCheckStarted; // TimeProvider timestamp, monotonic so a clock change cannot stretch the window
    private volatile UpstreamReleaseCheckResult? lastResult;

    /// <summary>
    /// Creates a checker that sends through <paramref name="handler"/> and owns it (disposed with the checker).
    /// Production uses <see cref="CreateDefaultHandler"/>; tests pass a fake, so no test touches the network.
    /// <paramref name="time"/> drives the check window and the request timeout.
    /// </summary>
    public UpstreamReleaseChecker(
        HttpMessageHandler handler, BuildInfo build, TimeProvider time, ILogger<UpstreamReleaseChecker> logger)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        // The timeout comes from RequestTimeout on the injected clock instead, so tests can drive it.
        client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        this.build = build;
        this.time = time;
        this.logger = logger;
    }

    /// <inheritdoc />
    public UpstreamReleaseCheckResult? LastResult => lastResult;

    /// <summary>
    /// The production handler: no redirects (the one GET goes to api.github.com and nowhere else; a moved
    /// repository reads as an HTTP error), no cookies, and compressed answers accepted.
    /// </summary>
    public static HttpMessageHandler CreateDefaultHandler() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.All,
    };

    /// <inheritdoc />
    public async Task<UpstreamReleaseCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        Task<UpstreamReleaseCheckResult> check;
        lock (gate)
        {
            if (latestCheck is null || time.GetElapsedTime(latestCheckStarted) >= CheckInterval)
            {
                latestCheckStarted = time.GetTimestamp();
                // On the thread pool: the caller (a Blazor component, say) never runs the request, and the lock
                // is held only to start it.
                latestCheck = Task.Run(RunCheckAsync, CancellationToken.None);
            }
            check = latestCheck;
        }

        try
        {
            return await check.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new Unknown(UpstreamReleaseCheckFailure.Cancelled, "The check was cancelled before it finished.");
        }
        catch (Exception)
        {
            // RunCheckAsync turns every failure into a result; only a logger that throws could get here.
            return new Unknown(UpstreamReleaseCheckFailure.Unexpected, "The check failed unexpectedly.");
        }
    }

    /// <summary>Disposes the HTTP client and its handler. A check still in flight ends as Unknown.</summary>
    public void Dispose() => client.Dispose();

    // The shared check behind CheckAsync. Never throws, so every waiter gets a result.
    private async Task<UpstreamReleaseCheckResult> RunCheckAsync()
    {
        UpstreamReleaseCheckResult result;
        try
        {
            result = await QueryAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result = Fail(UpstreamReleaseCheckFailure.Unexpected, "The check failed unexpectedly.", ex);
        }
        lastResult = result;
        return result;
    }

    private async Task<UpstreamReleaseCheckResult> QueryAsync()
    {
        if (build.UpstreamVersion is not { } pinned)
        {
            logger.LogInformation(
                "Upstream release check skipped: this build is stamped {UpstreamRelease}, not a release tag.",
                build.UpstreamRelease);
            return new Unknown(
                UpstreamReleaseCheckFailure.PinnedReleaseUnknown,
                "This build does not know which upstream release it was built from.");
        }

        using var timeout = new CancellationTokenSource(RequestTimeout, time);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (FailureFor(response) is { } failure) return failure;

            var body = await ReadBoundedAsync(response.Content, timeout.Token).ConfigureAwait(false);
            if (body is null)
                return Fail(UpstreamReleaseCheckFailure.InvalidResponse, "GitHub's answer was larger than expected.");
            if (!GitHubLatestRelease.TryParse(body, out var latest, out var problem))
                return Fail(UpstreamReleaseCheckFailure.InvalidResponse, problem);

            if (latest.Version > pinned)
            {
                logger.LogInformation(
                    "Upstream release check: {Latest} is newer than the pinned {Pinned}.", latest.Tag, build.UpstreamRelease);
                return new NewerAvailable(build.UpstreamRelease, latest.Tag, latest.ReleasePage, latest.PublishedAt);
            }
            logger.LogInformation(
                "Upstream release check: up to date (pinned {Pinned}, latest {Latest}).", build.UpstreamRelease, latest.Tag);
            return new UpToDate(build.UpstreamRelease, latest.Tag);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return Fail(
                UpstreamReleaseCheckFailure.Timeout,
                $"GitHub did not answer within {RequestTimeout.TotalSeconds:0} seconds.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return Fail(UpstreamReleaseCheckFailure.Network, "GitHub could not be reached.", ex);
        }
    }

    // Null for a success status; otherwise the failure. 403 counts as the rate limit only with GitHub's
    // rate-limit signals (no requests remaining, or a Retry-After for its secondary limits).
    private Unknown? FailureFor(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return null;
        var status = (int)response.StatusCode;
        if (status == 429 || (status == 403 && IsRateLimited(response)))
            return Fail(UpstreamReleaseCheckFailure.RateLimited, "GitHub's limit for anonymous requests was reached.");
        if (status == 404)
            return Fail(UpstreamReleaseCheckFailure.NotFound, "GitHub found no published upstream release.");
        return Fail(UpstreamReleaseCheckFailure.HttpError, $"GitHub answered HTTP {status}.");
    }

    private static bool IsRateLimited(HttpResponseMessage response) =>
        response.Headers.RetryAfter is not null
        || (response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining) && remaining.FirstOrDefault() == "0");

    // The body, or null when it exceeds MaxResponseBytes (declared or actual), so a hostile or broken answer
    // cannot make the meter buffer without bound. Decompressed bytes are what count.
    private static async Task<byte[]?> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaxResponseBytes) return null;
        var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > MaxResponseBytes) return null;
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
    }

    // Logs a failed check (reason and exception only: no response body, nothing about the player) and returns it.
    private Unknown Fail(UpstreamReleaseCheckFailure failure, string reason, Exception? exception = null)
    {
        logger.LogWarning(exception, "Upstream release check failed ({Failure}): {Reason}", failure, reason);
        return new Unknown(failure, reason);
    }
}
