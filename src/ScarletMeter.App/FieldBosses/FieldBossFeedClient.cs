using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using ScarletMeter.WorldEvents.FieldBosses;

namespace ScarletMeter.App.FieldBosses;

/// <summary>
/// The result of one read of the shared timers: the server's observations (none when the file does not list it yet), or a
/// one-sentence reason why there are none this time (<see cref="Problem"/>).
/// </summary>
public sealed record FieldBossFeedResult(IReadOnlyList<FieldBossTimer> Timers, string? Problem)
{
    public static FieldBossFeedResult Failed(string problem) => new([], problem);
}

/// <summary>
/// Reads NotMeter's shared field-boss timers for a Global server (<see cref="NotMeterFieldBossFeed"/>): one unauthenticated GET
/// of the region's public file on raw.githubusercontent.com, with a fixed User-Agent and, after the first answer, the file's
/// ETag, so an unchanged file comes back as a 304 with no body. Nothing about the player, the character or the game is sent;
/// the file asked for names only the region. No redirects, no cookies, a ten-second timeout and at most
/// <see cref="MaxResponseBytes"/> read. It never throws to callers: a failure is logged as a warning with its reason only,
/// never the file's text. How often to ask is the caller's (<see cref="FieldBossService"/>).
/// </summary>
public sealed class FieldBossFeedClient : IDisposable
{
    /// <summary>How long one request, headers and body, may take.</summary>
    public static TimeSpan RequestTimeout { get; } = TimeSpan.FromSeconds(10);

    /// <summary>A larger answer is refused; the published files are under 110 KB.</summary>
    public const int MaxResponseBytes = 1024 * 1024;

    private const string UserAgent = "ScarletMeter-FieldBossTimers";

    private readonly HttpClient client;
    private readonly TimeProvider time;
    private readonly ILogger<FieldBossFeedClient> logger;
    private readonly Lock gate = new();
    // The last file read at each address with its ETag, so a 304 can be read again against the current time.
    private readonly Dictionary<Uri, (string ETag, byte[] Body)> lastFiles = [];
    private volatile bool disposed;

    /// <summary>
    /// Creates a client that sends through <paramref name="handler"/> and owns it. Production uses
    /// <see cref="CreateDefaultHandler"/>; tests pass a fake, so no test touches the network.
    /// </summary>
    public FieldBossFeedClient(HttpMessageHandler handler, TimeProvider time, ILogger<FieldBossFeedClient> logger)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        this.time = time;
        this.logger = logger;
    }

    /// <summary>The production handler: no redirects (a moved file reads as an HTTP error), no cookies, compressed answers accepted.</summary>
    public static HttpMessageHandler CreateDefaultHandler() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.All,
    };

    /// <summary>Reads the shared timers of <paramref name="serverId"/>'s region and returns that server's.</summary>
    public async Task<FieldBossFeedResult> FetchAsync(int serverId, CancellationToken cancellationToken = default)
    {
        if (NotMeterFieldBossFeed.Address(serverId) is not { } address)
            return FieldBossFeedResult.Failed("Shared timers are published for Global servers only.");
        if (disposed) return FieldBossFeedResult.Failed("The meter is closing.");
        using var timeout = new CancellationTokenSource(RequestTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            (string ETag, byte[] Body)? cached;
            lock (gate) cached = lastFiles.TryGetValue(address, out var file) ? file : null;
            if (cached is { } held && EntityTagHeaderValue.TryParse(held.ETag, out var tag)) request.Headers.IfNoneMatch.Add(tag);

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
            byte[] body;
            if (response.StatusCode == HttpStatusCode.NotModified && cached is { } unchanged)
            {
                body = unchanged.Body;
            }
            else if (response.IsSuccessStatusCode)
            {
                if (await ReadBoundedAsync(response.Content, linked.Token).ConfigureAwait(false) is not { } read)
                    return Fail("The shared timers file was larger than expected.");
                body = read;
                if (response.Headers.ETag?.ToString() is { Length: > 0 and <= 256 } etag)
                    lock (gate) lastFiles[address] = (etag, body);
            }
            else
            {
                return Fail($"The shared timers file answered HTTP {(int)response.StatusCode}.");
            }

            if (!NotMeterFieldBossFeed.TryRead(body, serverId, time.GetUtcNow(), out var timers, out var problem))
                return Fail(problem ?? "The shared timers file did not read.");
            return new FieldBossFeedResult(timers, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return FieldBossFeedResult.Failed("The read was cancelled.");
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return Fail($"The shared timers did not answer within {RequestTimeout.TotalSeconds:0} seconds.");
        }
        catch (Exception) when (disposed)
        {
            return FieldBossFeedResult.Failed("The meter is closing.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return Fail("The shared timers could not be reached.", ex);
        }
        catch (Exception ex)
        {
            return Fail("The shared timers could not be read.", ex);
        }
    }

    public void Dispose()
    {
        disposed = true;
        client.Dispose();
    }

    // The body, or null when it exceeds MaxResponseBytes (declared or actual). Decompressed bytes are what count.
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

    // Logs a failed read (the reason and exception only, never the file's text) and returns it.
    private FieldBossFeedResult Fail(string reason, Exception? exception = null)
    {
        logger.LogWarning(exception, "Shared field boss timers not read: {Reason}", reason);
        return FieldBossFeedResult.Failed(reason);
    }
}
