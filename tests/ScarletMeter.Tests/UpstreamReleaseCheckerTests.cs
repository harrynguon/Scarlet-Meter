using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using ScarletMeter.App.Updates;
using static ScarletMeter.App.Updates.UpstreamReleaseCheckResult;

namespace ScarletMeter.Tests;

/// <summary>
/// The upstream release check against a fake GitHub: parsing, validation of the untrusted answer, numeric
/// comparison, the six-hour cache, the ten-second timeout, and every failure ending as Unknown rather than an
/// exception. No test touches the network; the clock is a FakeTimeProvider.
/// </summary>
public sealed class UpstreamReleaseCheckerTests
{
    private const string Pinned = "v1.11.4.334";
    private const string Prefix = "https://github.com/Kuroukihime/AIon2-Dps-Meter/";
    private const string TagPage = "(the tag's release page)";
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10); // real time, for a broken test only

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));
    private readonly ListLogger<UpstreamReleaseChecker> logger = new();

    [Fact]
    public async Task NewerReleaseIsReportedWithItsPageAndDate()
    {
        var github = FakeGitHub.Answering(Release("v1.11.5.341"));
        using var checker = Checker(github);

        var newer = Assert.IsType<NewerAvailable>(await checker.CheckAsync(Token));

        Assert.Equal(Pinned, newer.PinnedRelease);
        Assert.Equal("v1.11.5.341", newer.Tag);
        Assert.Equal(new Uri(Prefix + "releases/tag/v1.11.5.341"), newer.ReleasePage);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 9, 30, 0, TimeSpan.Zero), newer.PublishedAt);
        Assert.Same(newer, checker.LastResult);
    }

    [Theory]
    [InlineData("v1.11.4.334", "v1.11.4.334")] // the pinned release itself
    [InlineData("v1.11.4.334", "v1.10.3.302")]
    [InlineData("v1.11.4.334", "v1.11.4.99")] // older as a number, newer as text
    [InlineData("v1.10.0.1", "v1.9.4.277")]
    public async Task SameOrOlderReleaseIsUpToDate(string pinned, string latest)
    {
        using var checker = Checker(FakeGitHub.Answering(Release(latest)), pinned);
        Assert.Equal(new UpToDate(pinned, latest), await checker.CheckAsync(Token));
    }

    [Theory]
    [InlineData("v1.9.4.277", "v1.10.0.1")] // newer as a number, older as text
    [InlineData("v1.11.4.334", "v1.11.4.335")]
    [InlineData("v1.11.4.334", "v2.0.0.0")]
    public async Task NewerVersionsCompareNumerically(string pinned, string latest)
    {
        using var checker = Checker(FakeGitHub.Answering(Release(latest)), pinned);
        Assert.Equal(latest, Assert.IsType<NewerAvailable>(await checker.CheckAsync(Token)).Tag);
    }

    [Fact]
    public async Task RequestIsOneAnonymousGetToTheLatestReleaseApi()
    {
        var github = FakeGitHub.Answering(Release("v1.11.5.341"));
        using var checker = Checker(github);
        await checker.CheckAsync(Token);

        var request = Assert.Single(github.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(new Uri("https://api.github.com/repos/Kuroukihime/AIon2-Dps-Meter/releases/latest"), request.Uri);
        Assert.False(request.HasContent);
        Assert.False(string.IsNullOrWhiteSpace(request.Headers["User-Agent"]));
        Assert.Equal("application/vnd.github+json", request.Headers["Accept"]);
        // Nothing that identifies the player or authenticates: only the headers GitHub's API asks for.
        Assert.Equal(new[] { "Accept", "User-Agent", "X-GitHub-Api-Version" }, request.Headers.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ResultIsCachedForSixHoursFromTheLastRequest()
    {
        var github = FakeGitHub.Answering(Release("v1.11.5.341"));
        using var checker = Checker(github);

        var first = await checker.CheckAsync(Token);
        time.Advance(UpstreamReleaseChecker.CheckInterval - TimeSpan.FromSeconds(1));
        Assert.Same(first, await checker.CheckAsync(Token));
        Assert.Single(github.Requests);

        time.Advance(TimeSpan.FromSeconds(1));
        var second = await checker.CheckAsync(Token);
        Assert.Equal(2, github.Requests.Count);
        Assert.NotSame(first, second);
        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AnsweredFailuresAreCachedForSixHours(HttpStatusCode status)
    {
        // At most one request per window when GitHub answered at all, so a rate limit is never made worse.
        var github = FakeGitHub.Answering(status);
        using var checker = Checker(github);

        var failed = Assert.IsType<Unknown>(await checker.CheckAsync(Token));
        time.Advance(UpstreamReleaseChecker.CheckInterval - TimeSpan.FromSeconds(1));
        Assert.Same(failed, await checker.CheckAsync(Token));
        Assert.Single(github.Requests);
    }

    [Fact]
    public async Task InvalidAnswersAreCachedForSixHours()
    {
        var github = FakeGitHub.Answering("not json");
        using var checker = Checker(github);

        AssertFailure(UpstreamReleaseCheckFailure.InvalidResponse, await checker.CheckAsync(Token));
        time.Advance(UpstreamReleaseChecker.RetryInterval);
        AssertFailure(UpstreamReleaseCheckFailure.InvalidResponse, await checker.CheckAsync(Token));
        Assert.Single(github.Requests);
    }

    [Fact]
    public async Task AnUnreachableGitHubIsRetriedAfterTenMinutes()
    {
        // The first request fails to connect; the retry, ten minutes later rather than six hours, gets the release.
        var attempts = 0;
        var github = new FakeGitHub(_ => Interlocked.Increment(ref attempts) == 1
            ? Task.FromException<HttpResponseMessage>(new HttpRequestException("No such host is known."))
            : Task.FromResult(FakeGitHub.Ok(Release("v1.11.5.341"))));
        using var checker = Checker(github);

        var offline = await checker.CheckAsync(Token);
        AssertFailure(UpstreamReleaseCheckFailure.Network, offline);
        time.Advance(UpstreamReleaseChecker.RetryInterval - TimeSpan.FromSeconds(1));
        Assert.Same(offline, await checker.CheckAsync(Token));
        Assert.Single(github.Requests);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.IsType<NewerAvailable>(await checker.CheckAsync(Token));
        Assert.Equal(2, github.Requests.Count);
    }

    [Fact]
    public async Task ATimedOutCheckIsRetriedAfterTenMinutes()
    {
        var attempts = 0;
        var github = new FakeGitHub(async cancellationToken =>
        {
            if (Interlocked.Increment(ref attempts) == 1) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return FakeGitHub.Ok(Release("v1.11.5.341"));
        });
        using var checker = Checker(github);

        var check = checker.CheckAsync(Token);
        await github.Entered.Task.WaitAsync(Wait, Token);
        time.Advance(UpstreamReleaseChecker.RequestTimeout);
        AssertFailure(UpstreamReleaseCheckFailure.Timeout, await check.WaitAsync(Wait, Token));

        time.Advance(UpstreamReleaseChecker.RetryInterval - UpstreamReleaseChecker.RequestTimeout - TimeSpan.FromSeconds(1));
        AssertFailure(UpstreamReleaseCheckFailure.Timeout, await checker.CheckAsync(Token));
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.IsType<NewerAvailable>(await checker.CheckAsync(Token).WaitAsync(Wait, Token));
        Assert.Equal(2, github.Requests.Count);
    }

    [Fact]
    public async Task ConcurrentChecksShareOneRequest()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var github = new FakeGitHub(async _ =>
        {
            await gate.Task;
            return FakeGitHub.Ok(Release("v1.11.5.341"));
        });
        using var checker = Checker(github);

        var first = checker.CheckAsync(Token);
        var second = checker.CheckAsync(Token);
        await github.Entered.Task.WaitAsync(Wait, Token);
        Assert.Null(checker.LastResult);
        gate.SetResult();

        var results = await Task.WhenAll(first, second).WaitAsync(Wait, Token);
        Assert.Same(results[0], results[1]);
        Assert.Single(github.Requests);
    }

    [Fact]
    public async Task SlowHeadersTimeOutAfterTenSeconds()
    {
        var github = new FakeGitHub(async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable: the delay only ends by cancellation.");
        });
        using var checker = Checker(github);

        var check = checker.CheckAsync(Token);
        await github.Entered.Task.WaitAsync(Wait, Token);
        time.Advance(UpstreamReleaseChecker.RequestTimeout - TimeSpan.FromMilliseconds(1));
        Assert.False(check.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(1));

        AssertFailure(UpstreamReleaseCheckFailure.Timeout, await check.WaitAsync(Wait, Token));
    }

    [Fact]
    public async Task SlowBodyTimesOutAfterTenSeconds()
    {
        var body = new StalledStream();
        var github = new FakeGitHub(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) }));
        using var checker = Checker(github);

        var check = checker.CheckAsync(Token);
        await body.Reading.Task.WaitAsync(Wait, Token);
        time.Advance(UpstreamReleaseChecker.RequestTimeout);

        AssertFailure(UpstreamReleaseCheckFailure.Timeout, await check.WaitAsync(Wait, Token));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "x-ratelimit-remaining", "0", UpstreamReleaseCheckFailure.RateLimited)]
    [InlineData(HttpStatusCode.Forbidden, "Retry-After", "60", UpstreamReleaseCheckFailure.RateLimited)]
    [InlineData(HttpStatusCode.TooManyRequests, null, null, UpstreamReleaseCheckFailure.RateLimited)]
    [InlineData(HttpStatusCode.Forbidden, "x-ratelimit-remaining", "42", UpstreamReleaseCheckFailure.HttpError)]
    [InlineData(HttpStatusCode.NotFound, null, null, UpstreamReleaseCheckFailure.NotFound)]
    [InlineData(HttpStatusCode.MovedPermanently, "Location", "https://api.github.com/repositories/1/releases/latest", UpstreamReleaseCheckFailure.HttpError)]
    [InlineData(HttpStatusCode.InternalServerError, null, null, UpstreamReleaseCheckFailure.HttpError)]
    public async Task ErrorStatusesGiveUnknown(HttpStatusCode status, string? header, string? value, UpstreamReleaseCheckFailure expected)
    {
        var github = new FakeGitHub(_ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent("""{"message":"x"}""") };
            if (header is not null) response.Headers.TryAddWithoutValidation(header, value);
            return Task.FromResult(response);
        });
        using var checker = Checker(github);

        AssertFailure(expected, await checker.CheckAsync(Token));
        Assert.Single(github.Requests);
    }

    public static TheoryData<string> InvalidAnswers => new()
    {
        "not json",
        "",
        "[]",
        "null",
        "{}",
        """{"tag_name": 341, "html_url": "https://github.com/Kuroukihime/AIon2-Dps-Meter/releases/tag/v1.11.5.341"}""",
        Release(null),
        Release("1.11.5.341"),
        Release("v1.11.5"),
        Release("v1.11.5.341.1"),
        Release("v1.11.5.341-beta"),
        Release("v1.11.5.341\n"),
        Release("v1.11.5.\uFF13\uFF14\uFF11"), // full-width digits
        Release("v1.11.5.<script>"),
        Release("v1.11.5.341", page: null),
        Release("v1.11.5.341", page: "/Kuroukihime/AIon2-Dps-Meter/releases/tag/v1.11.5.341"),
        Release("v1.11.5.341", page: "javascript:alert(1)"),
        Release("v1.11.5.341", page: "http://github.com/Kuroukihime/AIon2-Dps-Meter/releases/tag/v1.11.5.341"),
        Release("v1.11.5.341", page: "https://evil.example/Kuroukihime/AIon2-Dps-Meter/releases/tag/v1.11.5.341"),
        Release("v1.11.5.341", page: "https://github.com.evil.example/Kuroukihime/AIon2-Dps-Meter/releases/tag/v1.11.5.341"),
        Release("v1.11.5.341", page: "https://github.com/Kuroukihime/AIon2-Dps-Meter.evil/releases/tag/v1.11.5.341"),
        Release("v1.11.5.341", page: "https://github.com/someone-else/AIon2-Dps-Meter/releases/tag/v1.11.5.341"),
        Release("v1.11.5.341", page: "https://github.com/Kuroukihime/AIon2-Dps-Meter/../../evil/repo"),
        Release("v1.11.5.341", page: "https://github.com/Kuroukihime/AIon2-Dps-Meter/%2e%2e/%2e%2e/evil/repo"),
        Release("v1.11.5.341", page: "https://someone@github.com/Kuroukihime/AIon2-Dps-Meter/releases/tag/v1.11.5.341"),
        Release("v1.11.5.341", page: "https://github.com:8443/Kuroukihime/AIon2-Dps-Meter/releases/tag/v1.11.5.341"),
        Release("v1.11.5.341", page: "https://github.com/Kuroukihime/AIon2-Dps-Meter/releases?next=https://evil.example"),
    };

    [Theory]
    [MemberData(nameof(InvalidAnswers))]
    public async Task InvalidAnswersGiveUnknown(string answer)
    {
        using var checker = Checker(FakeGitHub.Answering(answer));
        AssertFailure(UpstreamReleaseCheckFailure.InvalidResponse, await checker.CheckAsync(Token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("yesterday")]
    public async Task AMissingOrUnreadableDateIsAllowed(string? published)
    {
        using var checker = Checker(FakeGitHub.Answering(Release("v1.11.5.341", published: published)));
        Assert.Null(Assert.IsType<NewerAvailable>(await checker.CheckAsync(Token)).PublishedAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnAnswerOfExactlyTheCapIsRead(bool declaresLength)
    {
        var answer = ReleaseOfSize(UpstreamReleaseChecker.MaxResponseBytes);
        using var checker = Checker(AnsweringBytes(answer, declaresLength));
        Assert.IsType<NewerAvailable>(await checker.CheckAsync(Token));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnAnswerOneByteOverTheCapIsRejected(bool declaresLength)
    {
        var answer = ReleaseOfSize(UpstreamReleaseChecker.MaxResponseBytes + 1);
        using var checker = Checker(AnsweringBytes(answer, declaresLength));
        AssertFailure(UpstreamReleaseCheckFailure.InvalidResponse, await checker.CheckAsync(Token));
    }

    [Fact]
    public async Task ACompressedAnswerIsDecompressedByTheProductionHandler()
    {
        // Through the production handler (proxy off), against a one-shot HTTP server on the loopback address: the
        // handler asks for compression and inflates the gzip body before the checker reads it.
        await using var server = await LoopbackServer.StartAsync(Gzip(Encoding.UTF8.GetBytes(Release("v1.11.5.341"))));
        using var checker = new UpstreamReleaseChecker(
            new RedirectToLoopback(server.Address) { InnerHandler = ProductionHandlerWithoutProxy() },
            new BuildInfo(Pinned, "9495922"), time, logger);

        Assert.IsType<NewerAvailable>(await checker.CheckAsync(Token).WaitAsync(Wait, Token));
        Assert.Contains("gzip", await server.AcceptEncoding.WaitAsync(Wait, Token), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheCapCountsDecompressedBytes()
    {
        // About 1 KiB on the wire, over 1 MiB once inflated: rejected, not buffered.
        await using var server = await LoopbackServer.StartAsync(Gzip(Encoding.UTF8.GetBytes(ReleaseOfSize(UpstreamReleaseChecker.MaxResponseBytes + 1))));
        using var checker = new UpstreamReleaseChecker(
            new RedirectToLoopback(server.Address) { InnerHandler = ProductionHandlerWithoutProxy() },
            new BuildInfo(Pinned, "9495922"), time, logger);

        AssertFailure(UpstreamReleaseCheckFailure.InvalidResponse, await checker.CheckAsync(Token).WaitAsync(Wait, Token));
    }

    [Fact]
    public async Task DisposingMidCheckEndsItAsCancelled()
    {
        var github = new FakeGitHub(async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable: the delay only ends by cancellation.");
        });
        var checker = Checker(github);

        var check = checker.CheckAsync(Token);
        await github.Entered.Task.WaitAsync(Wait, Token);
        checker.Dispose();

        AssertFailure(UpstreamReleaseCheckFailure.Cancelled, await check.WaitAsync(Wait, Token));
        AssertFailure(UpstreamReleaseCheckFailure.Cancelled, await checker.CheckAsync(Token));
        Assert.Single(github.Requests);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task NetworkFailuresGiveUnknown()
    {
        using var unreachable = Checker(FakeGitHub.Throwing(new HttpRequestException("No such host is known.")));
        AssertFailure(UpstreamReleaseCheckFailure.Network, await unreachable.CheckAsync(Token));

        using var broken = Checker(FakeGitHub.Throwing(new IOException("The connection was reset.")));
        AssertFailure(UpstreamReleaseCheckFailure.Network, await broken.CheckAsync(Token));
    }

    [Fact]
    public async Task UnexpectedExceptionsNeverReachTheCaller()
    {
        using var checker = Checker(FakeGitHub.Throwing(new InvalidOperationException("Boom.")));

        AssertFailure(UpstreamReleaseCheckFailure.Unexpected, await checker.CheckAsync(Token));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Exception is InvalidOperationException);
    }

    [Theory]
    [InlineData(BuildInfo.UnknownValue)]
    [InlineData("57974f8")] // the commit fallback of a clone without upstream's tags
    public async Task AnUnknownPinnedReleaseMakesNoRequest(string stamped)
    {
        var github = FakeGitHub.Answering(Release("v1.11.5.341"));
        using var checker = Checker(github, stamped);

        AssertFailure(UpstreamReleaseCheckFailure.PinnedReleaseUnknown, await checker.CheckAsync(Token));
        Assert.Empty(github.Requests);
    }

    [Fact]
    public async Task CallerCancellationEndsTheWaitButNotTheSharedCheck()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var github = new FakeGitHub(async _ =>
        {
            await gate.Task;
            return FakeGitHub.Ok(Release("v1.11.5.341"));
        });
        using var checker = Checker(github);
        using var cancel = new CancellationTokenSource();

        var waiting = checker.CheckAsync(cancel.Token);
        await github.Entered.Task.WaitAsync(Wait, Token);
        await cancel.CancelAsync();
        AssertFailure(UpstreamReleaseCheckFailure.Cancelled, await waiting.WaitAsync(Wait, Token));
        Assert.Null(checker.LastResult);

        gate.SetResult();
        Assert.IsType<NewerAvailable>(await checker.CheckAsync(Token).WaitAsync(Wait, Token));
        Assert.Single(github.Requests);
    }

    [Fact]
    public async Task TheAnswerNeverReachesTheLog()
    {
        // Only the reason is logged, never GitHub's text: here an invalid tag carrying a marker.
        using var checker = Checker(FakeGitHub.Answering(Release("v1.11.5.MARKER", notes: "MARKER")));

        AssertFailure(UpstreamReleaseCheckFailure.InvalidResponse, await checker.CheckAsync(Token));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.DoesNotContain(logger.Entries, entry => entry.Message.Contains("MARKER", StringComparison.Ordinal));
    }

    [Fact]
    public void RegistrationResolvesOneCheckerAndSendsNothing()
    {
        var services = new ServiceCollection();
        services.AddUpstreamReleaseChecker();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        var checker = provider.GetRequiredService<UpstreamReleaseChecker>();
        Assert.Same(checker, provider.GetRequiredService<IUpstreamReleaseChecker>());
        Assert.Same(BuildInfo.Current, provider.GetRequiredService<BuildInfo>());
        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
        Assert.Null(checker.LastResult);
    }

    [Fact]
    public void RegistrationKeepsTheHostsClock()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddUpstreamReleaseChecker();
        using var provider = services.BuildServiceProvider();
        Assert.Same(time, provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void ProductionHandlerFollowsNoRedirectsAndKeepsNoCookies()
    {
        using var handler = Assert.IsType<SocketsHttpHandler>(UpstreamReleaseChecker.CreateDefaultHandler());
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
    }

    private UpstreamReleaseChecker Checker(FakeGitHub github, string pinned = Pinned) =>
        new(github, new BuildInfo(pinned, "9495922"), time, logger);

    private static void AssertFailure(UpstreamReleaseCheckFailure expected, UpstreamReleaseCheckResult result)
    {
        var unknown = Assert.IsType<Unknown>(result);
        Assert.Equal(expected, unknown.Failure);
        Assert.False(string.IsNullOrWhiteSpace(unknown.Reason));
    }

    // A body shaped like GitHub's latest-release answer. A null field is left out; the page defaults to the tag's.
    private static string Release(string? tag, string? page = TagPage, string? published = "2026-10-20T09:30:00Z", string notes = "Release notes.")
    {
        var fields = new Dictionary<string, object?>
        {
            ["url"] = "https://api.github.com/repos/Kuroukihime/AIon2-Dps-Meter/releases/1",
            ["id"] = 1,
            ["draft"] = false,
            ["prerelease"] = false,
            ["body"] = notes,
        };
        if (tag is not null) fields["tag_name"] = tag;
        if (page is not null) fields["html_url"] = page == TagPage ? $"{Prefix}releases/tag/{tag}" : page;
        if (published is not null) fields["published_at"] = published;
        return JsonSerializer.Serialize(fields);
    }

    /// <summary>A stand-in for api.github.com: records each request, then answers through a delegate.</summary>
    private sealed class FakeGitHub(Func<CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

        /// <summary>Completes when the first request arrives.</summary>
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static FakeGitHub Answering(string body) => new(_ => Task.FromResult(Ok(body)));

        public static FakeGitHub Answering(HttpStatusCode status) =>
            new(_ => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{}") }));

        public static FakeGitHub Throwing(Exception exception) => new(_ => Task.FromException<HttpResponseMessage>(exception));

        public static HttpResponseMessage Ok(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(new RecordedRequest(
                request.Method,
                request.RequestUri,
                request.Headers.ToDictionary(header => header.Key, header => string.Join(", ", header.Value)),
                request.Content is not null));
            Entered.TrySetResult();
            return answer(cancellationToken);
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri? Uri, Dictionary<string, string> Headers, bool HasContent);

    // A valid release answer of exactly `bytes` UTF-8 bytes, padded through its notes (one byte per character).
    private static string ReleaseOfSize(int bytes)
    {
        var unpadded = Encoding.UTF8.GetByteCount(Release("v1.11.5.341", notes: ""));
        var answer = Release("v1.11.5.341", notes: new string('x', bytes - unpadded));
        Assert.Equal(bytes, Encoding.UTF8.GetByteCount(answer));
        return answer;
    }

    // Answers with the body, declaring its length or not (then the checker reads until its cap, as for chunked data).
    private static FakeGitHub AnsweringBytes(string body, bool declaresLength) => new(_ =>
    {
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        if (!declaresLength) content.Headers.ContentLength = null;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    });

    private static byte[] Gzip(byte[] data)
    {
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(data);
        return compressed.ToArray();
    }

    // The production handler with its settings (no redirects, no cookies, decompression) but no proxy: the system
    // proxy or HTTP_PROXY would otherwise carry the loopback request off the machine, or fail it.
    private static SocketsHttpHandler ProductionHandlerWithoutProxy()
    {
        var handler = Assert.IsType<SocketsHttpHandler>(UpstreamReleaseChecker.CreateDefaultHandler());
        handler.UseProxy = false;
        return handler;
    }

    /// <summary>Sends the checker's request to a loopback server instead of api.github.com, keeping its path.</summary>
    private sealed class RedirectToLoopback(Uri server) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri = new Uri(server, request.RequestUri!.PathAndQuery);
            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// A one-shot HTTP/1.1 server on 127.0.0.1 that answers one request with a gzip-encoded 200 and reports the
    /// request's Accept-Encoding. Loopback only, and reached without a proxy: nothing leaves the machine.
    /// </summary>
    private sealed class LoopbackServer : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly TaskCompletionSource<string> acceptEncoding = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task serving = Task.CompletedTask;

        public Uri Address { get; private set; } = null!;

        /// <summary>The Accept-Encoding header of the request the server answered.</summary>
        public Task<string> AcceptEncoding => acceptEncoding.Task;

        public static Task<LoopbackServer> StartAsync(byte[] gzipBody)
        {
            var server = new LoopbackServer();
            server.listener.Start();
            server.Address = new Uri($"http://127.0.0.1:{((IPEndPoint)server.listener.LocalEndpoint).Port}/");
            server.serving = server.ServeOnceAsync(gzipBody);
            return Task.FromResult(server);
        }

        public async ValueTask DisposeAsync()
        {
            listener.Stop();
            try
            {
                await serving;
            }
            catch (Exception)
            {
                // Stopping the listener ends an accept that never happened; nothing to report.
            }
        }

        private async Task ServeOnceAsync(byte[] body)
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync(Token);
                using var stream = client.GetStream();
                var headers = await ReadHeadersAsync(stream);
                var encoding = headers.Split("\r\n")
                    .FirstOrDefault(line => line.StartsWith("Accept-Encoding:", StringComparison.OrdinalIgnoreCase));
                acceptEncoding.TrySetResult(encoding?["Accept-Encoding:".Length..].Trim() ?? "");
                var head = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Encoding: gzip\r\n" +
                    $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(head, Token);
                await stream.WriteAsync(body, Token);
            }
            catch (Exception ex)
            {
                acceptEncoding.TrySetException(ex);
            }
        }

        // The request's header block, read up to the blank line (at most 64 KiB).
        private static async Task<string> ReadHeadersAsync(NetworkStream stream)
        {
            var received = new MemoryStream();
            var buffer = new byte[4096];
            while (received.Length < 64 * 1024)
            {
                var read = await stream.ReadAsync(buffer, Token);
                if (read == 0) break;
                received.Write(buffer, 0, read);
                var text = Encoding.ASCII.GetString(received.GetBuffer(), 0, (int)received.Length);
                if (text.Contains("\r\n\r\n", StringComparison.Ordinal)) return text;
            }
            return Encoding.ASCII.GetString(received.ToArray());
        }
    }

    /// <summary>A response body that never yields a byte; reading ends only by cancellation.</summary>
    private sealed class StalledStream : Stream
    {
        /// <summary>Completes when the checker starts reading the body.</summary>
        public TaskCompletionSource Reading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Reading.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Keeps what the checker logs, so a test can check what reaches the log.</summary>
    private sealed class ListLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Enqueue((logLevel, formatter(state, exception), exception));
    }
}
