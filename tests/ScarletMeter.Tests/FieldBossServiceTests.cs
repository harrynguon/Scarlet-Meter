using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ScarletMeter.App.FieldBosses;
using ScarletMeter.App.Settings;
using ScarletMeter.Engine;
using ScarletMeter.WorldEvents.FieldBosses;

namespace ScarletMeter.Tests;

/// <summary>
/// The field-boss timers in the app (FieldBossService, FieldBossFeedClient): a map reading put on the server of the character
/// being played, held while that is not sure and dropped on a move; the character's server kept in the settings, a server
/// the player chose kept until the game names a character on another; the shared timers read only while wanted, every
/// three minutes, never with the switch off; and the feed client's request, its ETag and its failures. No test touches the
/// network: the client sends through a fake handler.
/// </summary>
public sealed class FieldBossServiceTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 8, 5, 0, 0, TimeSpan.Zero);
    private const int Gartua = 101021;
    private const int Server = 1503;

    private readonly TestDataDirectory directory = new();
    private readonly FakeTimeProvider clock = new(Start);
    private readonly RosterTests.FakeEngine engine = new();

    public void Dispose() => directory.Dispose();

    private static CharacterIdentity Character(int serverId) => new("Alpha", string.Empty, 16, "Spiritmaster", 41_000, serverId);

    private static CharacterProgress Progress(int serverId) => new(Character(serverId), new Dictionary<uint, EntryCount>());

    private static FieldBossMap Map(DateTimeOffset seenAt, params FieldBossOnMap[] bosses) => new(1010, bosses, seenAt);

    private SettingsService NewSettings(Func<FieldBossSettings, FieldBossSettings>? change = null)
    {
        var settings = new SettingsService(new SettingsStore(directory.FullPath, NullLogger<SettingsStore>.Instance), new EngineSettings(), NullLogger<SettingsService>.Instance);
        if (change is not null) settings.Update(s => s with { FieldBosses = change(s.FieldBosses) });
        return settings;
    }

    private FieldBossService NewService(SettingsService settings, FakeFeed? feed = null) =>
        new(engine, settings, new FieldBossFeedClient(feed ?? new FakeFeed(), clock, NullLogger<FieldBossFeedClient>.Instance), clock, NullLogger<FieldBossService>.Instance);

    // The settings change for the character's server is made off the capture thread; wait for it.
    private static void WaitForServer(SettingsService settings, int? server) =>
        Assert.True(SpinWait.SpinUntil(() => settings.Current.FieldBosses.ServerId == server, TimeSpan.FromSeconds(5)), $"the server never became {server}");

    // ---------- The player's map ----------

    // With the character known, a map goes on its server, which becomes the timers' server.
    [Fact]
    public void AMapIsPutOnTheServerOfTheCharacterBeingPlayed()
    {
        using var settings = NewSettings();
        using var service = NewService(settings);
        service.Start();
        engine.Character = Character(Server);

        engine.RaiseFieldBossMap(Map(Start, new FieldBossOnMap(Gartua, false, Start.AddMinutes(90))));
        WaitForServer(settings, Server);

        var view = service.View(Start);
        Assert.Equal(Server, view.ServerId);
        Assert.Equal(new FieldBossTimer(Gartua, false, Start.AddMinutes(90), Start, FieldBossSource.Map), view.Board[Gartua]);
        Assert.Equal(Start, view.MapReadAt);
    }

    // While the character is not sure, a map is held and goes on the server of the character the game names next.
    [Fact]
    public void AMapReadWhileTheCharacterIsNotSureWaitsForTheGameToNameThem()
    {
        using var settings = NewSettings();
        using var service = NewService(settings);
        service.Start();

        service.TakeMap(Map(Start, new FieldBossOnMap(Gartua, false, Start.AddMinutes(90))));
        Assert.Empty(service.View(Start).Board);

        clock.Advance(FieldBossService.HoldFor);
        service.TakeProgress(Progress(Server));
        WaitForServer(settings, Server);
        Assert.True(service.View(clock.GetUtcNow()).Board.ContainsKey(Gartua));
    }

    // Held past ten minutes, or across a move to a new connection, it is dropped: whose server it was is not known.
    [Fact]
    public void AHeldMapIsDroppedWhenTooOldOrWhenTheGameMoves()
    {
        using var settings = NewSettings(f => f with { ServerId = Server });
        using var service = NewService(settings);
        service.Start();

        service.TakeMap(Map(Start, new FieldBossOnMap(Gartua, false, Start.AddMinutes(90))));
        clock.Advance(FieldBossService.HoldFor + TimeSpan.FromTicks(1));
        service.TakeProgress(Progress(Server));
        Assert.Empty(service.View(clock.GetUtcNow()).Board);

        service.TakeMap(Map(clock.GetUtcNow(), new FieldBossOnMap(Gartua, false, Start.AddMinutes(90))));
        engine.RaiseConnection();
        service.TakeProgress(Progress(Server));
        Assert.Empty(service.View(clock.GetUtcNow()).Board);
    }

    // A character named without a server id keeps nothing; nor does a map's boss on another map than the catalog's.
    [Fact]
    public void AMapIsNotKeptWithoutAServerOrForABossOfAnotherMap()
    {
        using var settings = NewSettings(f => f with { ServerId = Server });
        using var service = NewService(settings);
        service.Start();
        engine.Character = Character(0);
        service.TakeMap(Map(Start, new FieldBossOnMap(Gartua, false, Start.AddMinutes(90))));
        Assert.Empty(service.View(Start).Board);

        engine.Character = Character(Server);
        service.TakeMap(new FieldBossMap(1110, [new FieldBossOnMap(Gartua, false, Start.AddMinutes(90))], Start));
        Assert.Empty(service.View(Start).Board);
    }

    // A server the player chose stays until the game names a character on another server than the last one named.
    [Fact]
    public void AChosenServerStaysUntilTheGameNamesACharacterOnAnother()
    {
        using var settings = NewSettings();
        using var service = NewService(settings);
        service.Start();
        service.TakeProgress(Progress(Server));
        WaitForServer(settings, Server);

        service.ChooseServer(1504);
        Assert.Equal(1504, settings.Current.FieldBosses.ServerId);
        service.TakeProgress(Progress(Server)); // the same character's server again: the choice stays
        Thread.Sleep(50);
        Assert.Equal(1504, settings.Current.FieldBosses.ServerId);

        service.TakeProgress(Progress(1505));
        WaitForServer(settings, 1505);
        service.ChooseServer(42); // not a server: ignored
        Assert.Equal(1505, settings.Current.FieldBosses.ServerId);
    }

    // ---------- The shared timers ----------

    // Not read while nothing wants them; read once the window watches; again only after three minutes.
    [Fact]
    public void TheSharedTimersAreReadOnlyWhileWantedAndEveryThreeMinutes()
    {
        var feed = new FakeFeed();
        using var settings = NewSettings(f => f with { ServerId = Server });
        using var service = NewService(settings, feed);
        service.Start();
        Assert.True(feed.Requests.IsEmpty);

        using (service.Watch())
        {
            feed.WaitFor(1);
            Assert.True(SpinWait.SpinUntil(() => service.View(clock.GetUtcNow()).Board.ContainsKey(Gartua), TimeSpan.FromSeconds(5)));
            clock.Advance(FieldBossService.RefreshInterval - TimeSpan.FromSeconds(1));
            Thread.Sleep(50);
            Assert.Single(feed.Requests);
            clock.Advance(TimeSpan.FromSeconds(1));
            feed.WaitFor(2);
        }
        clock.Advance(FieldBossService.RefreshInterval * 3);
        Thread.Sleep(50);
        Assert.Equal(2, feed.Requests.Count);
    }

    // A boss with a notice wants them too, and the switch off stops them whatever wants them.
    [Fact]
    public void AChosenBossWantsTheSharedTimersAndTheSwitchStopsThem()
    {
        var feed = new FakeFeed();
        using var settings = NewSettings(f => f with { ServerId = Server, SharedTimers = false, Bosses = [Gartua] });
        using var service = NewService(settings, feed);
        service.Start();
        using (service.Watch())
        {
            Thread.Sleep(50);
            Assert.True(feed.Requests.IsEmpty);
        }
        settings.Update(s => s with { FieldBosses = s.FieldBosses with { SharedTimers = true } });
        feed.WaitFor(1);
        Assert.Equal(new Uri("https://raw.githubusercontent.com/Not4You-Dev/NotMeter-Cache/main/presence/notmeter-field-boss-public-ww-as-expanded.json"), feed.Requests.Single().Uri);
    }

    // The overlay's notice for a chosen boss, from the shared timers, and the player's newer map over them.
    [Fact]
    public void TheNoticeComesFromTheNewestOfTheMapAndTheSharedTimers()
    {
        var feed = new FakeFeed(respawn: Start.AddMinutes(8), observed: Start.AddMinutes(-2));
        using var settings = NewSettings(f => f with { ServerId = Server, Bosses = [Gartua] });
        using var service = NewService(settings, feed);
        service.Start();
        feed.WaitFor(1);
        Assert.True(SpinWait.SpinUntil(() => service.Plan(Start).Alerts.Count == 1, TimeSpan.FromSeconds(5)));
        var soon = Assert.Single(service.Plan(Start).Alerts);
        Assert.Equal((FieldBossAlertKind.Soon, Start.AddMinutes(8), FieldBossSource.NotMeter), (soon.Kind, soon.At, soon.Timer.Source));

        engine.Character = Character(Server);
        service.TakeMap(Map(Start, new FieldBossOnMap(Gartua, true, Start.AddMinutes(-1))));
        var up = Assert.Single(service.Plan(Start).Alerts);
        Assert.Equal((FieldBossAlertKind.Up, FieldBossSource.Map), (up.Kind, up.Timer.Source));

        settings.Update(s => s with { FieldBosses = s.FieldBosses with { Enabled = false } });
        Assert.Empty(service.Plan(Start).Alerts);
    }

    // Turned off, the shared timers' last read is no longer shown or used for a notice; back on, it is read again (deep
    // audit on #75: the requests stopped but the last read stayed).
    [Fact]
    public void TurningTheSharedTimersOffDropsWhatTheyGave()
    {
        var feed = new FakeFeed(respawn: Start.AddMinutes(8));
        using var settings = NewSettings(f => f with { ServerId = Server, Bosses = [Gartua] });
        using var service = NewService(settings, feed);
        service.Start();
        Assert.True(SpinWait.SpinUntil(() => service.Plan(Start).Alerts.Count == 1, TimeSpan.FromSeconds(5)));

        settings.Update(s => s with { FieldBosses = s.FieldBosses with { SharedTimers = false } });
        Assert.Empty(service.View(Start).Board);
        Assert.Empty(service.Plan(Start).Alerts);

        settings.Update(s => s with { FieldBosses = s.FieldBosses with { SharedTimers = true } });
        Assert.Single(service.Plan(Start).Alerts);
    }

    // A read for the server before a change that comes back after the new server's read is not taken: the timers stay the
    // new server's, and the new read's place is kept (deep audit on #75: no test held a read back).
    [Fact]
    public void AReadForTheServerBeforeAChangeIsNotTaken()
    {
        var feed = new FakeFeed(respawn: Start.AddMinutes(90), holdFirst: true);
        using var settings = NewSettings(f => f with { ServerId = Server });
        using var service = NewService(settings, feed);
        service.Start();
        using var watch = service.Watch();
        feed.WaitFor(1); // the Asia read, held

        settings.Update(s => s with { FieldBosses = s.FieldBosses with { ServerId = 2205 } });
        feed.WaitFor(2); // the NA West read, answered
        Assert.True(SpinWait.SpinUntil(() => service.View(Start).Board.ContainsKey(101006), TimeSpan.FromSeconds(5)));

        feed.ReleaseFirst();
        Thread.Sleep(100);
        var view = service.View(Start);
        Assert.Equal(2205, view.Feed.ServerId);
        Assert.Equal([101006], view.Board.Keys);
        // Taken, the stale read (the client gives it up as cancelled at once) would have dropped the new read's place and
        // asked again: two requests, never three.
        Assert.Equal(2, feed.Requests.Count);
        Assert.Equal(new Uri("https://raw.githubusercontent.com/Not4You-Dev/NotMeter-Cache/main/presence/notmeter-field-boss-public-ww-naw.json"), feed.Requests.Last().Uri);
    }

    // The server of the character the game named is saved on the thread Start ran on (the UI thread in the app), in order,
    // never on the capture thread or the thread pool (deep audit on #75).
    [Fact]
    public void TheCharactersServerIsSavedOnTheThreadStartRanOn()
    {
        var posts = new QueuedContext();
        using var settings = NewSettings();
        using var service = NewService(settings);
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(posts);
        try
        {
            service.Start();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        service.TakeProgress(Progress(Server));
        service.TakeProgress(Progress(1505));
        Assert.Null(settings.Current.FieldBosses.ServerId); // nothing yet: both are posted
        Assert.Equal(2, posts.Count);
        posts.RunAll();
        Assert.Equal(1505, settings.Current.FieldBosses.ServerId); // in the order the game named them
    }

    // The settings on A, the game names B then A before the saves run: the last named stands. Compared with the settings
    // before its turn, A's save was dropped (they still said A) and B's landed last (Codex on #75).
    [Fact]
    public void ACharacterNamedBackBeforeTheSavesRunIsTheLastSaved()
    {
        var posts = new QueuedContext();
        using var settings = NewSettings(f => f with { ServerId = Server });
        using var service = NewService(settings);
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(posts);
        try
        {
            service.Start();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        service.TakeProgress(Progress(1505));
        service.TakeProgress(Progress(Server));
        posts.RunAll();
        Assert.Equal(Server, settings.Current.FieldBosses.ServerId);
    }

    // ---------- The feed client ----------

    // The production handler follows no redirect and keeps no cookie (SECURITY.md, "Network").
    [Fact]
    public void TheDefaultHandlerFollowsNoRedirectAndKeepsNoCookie()
    {
        using var handler = Assert.IsType<SocketsHttpHandler>(FieldBossFeedClient.CreateDefaultHandler());
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
    }

    // A file that does not come within ten seconds is given up, with the reason.
    [Fact]
    public async Task AReadThatDoesNotAnswerInTenSecondsIsGivenUp()
    {
        var feed = new FakeFeed { Hang = true };
        using var client = new FieldBossFeedClient(feed, clock, NullLogger<FieldBossFeedClient>.Instance);
        var read = client.FetchAsync(Server, TestContext.Current.CancellationToken);
        feed.WaitFor(1);
        clock.Advance(FieldBossFeedClient.RequestTimeout - TimeSpan.FromSeconds(1));
        Assert.False(read.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(1));
        // Awaited with a real-time bound (a TimeoutException), so a timeout that does not fire fails the test rather than
        // hanging the run, as it once hung a mutation run.
        var result = await read.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("The shared timers did not answer within 10 seconds.", result.Problem);
    }

    // One GET of the region's file with a fixed User-Agent and nothing else about the player; the ETag the next time, and a
    // 304 read again from the last file.
    [Fact]
    public async Task TheClientAsksForTheRegionsFileAndReusesItWhenUnchanged()
    {
        var feed = new FakeFeed(etag: "\"abc\"");
        using var client = new FieldBossFeedClient(feed, clock, NullLogger<FieldBossFeedClient>.Instance);

        var first = await client.FetchAsync(Server, TestContext.Current.CancellationToken);
        Assert.Null(first.Problem);
        Assert.Equal(Gartua, Assert.Single(first.Timers).BossId);
        var request = feed.Requests.Single();
        Assert.Equal(("ScarletMeter-FieldBossTimers", false), (request.Headers["User-Agent"], request.Headers.ContainsKey("If-None-Match")));
        Assert.DoesNotContain(request.Headers.Keys, name => name is "Cookie" or "Authorization");

        feed.Status = HttpStatusCode.NotModified;
        var second = await client.FetchAsync(Server, TestContext.Current.CancellationToken);
        Assert.Null(second.Problem);
        Assert.Equal(first.Timers, second.Timers);
        Assert.Equal("\"abc\"", feed.Requests.Last().Headers["If-None-Match"]);
    }

    // Failures give a reason and no timers: an HTTP error, an answer too large, a server with no shared timers (no request).
    [Fact]
    public async Task TheClientsFailuresGiveAReason()
    {
        var feed = new FakeFeed { Status = HttpStatusCode.InternalServerError };
        using var client = new FieldBossFeedClient(feed, clock, NullLogger<FieldBossFeedClient>.Instance);
        Assert.Equal("The shared timers file answered HTTP 500.", (await client.FetchAsync(Server, TestContext.Current.CancellationToken)).Problem);

        feed.Status = HttpStatusCode.OK;
        feed.Padding = FieldBossFeedClient.MaxResponseBytes;
        Assert.Equal("The shared timers file was larger than expected.", (await client.FetchAsync(Server, TestContext.Current.CancellationToken)).Problem);

        var before = feed.Requests.Count;
        Assert.NotNull((await client.FetchAsync(1003, TestContext.Current.CancellationToken)).Problem);
        Assert.Equal(before, feed.Requests.Count);
    }

    // A synchronization context that queues what is posted until the test runs it, as a UI thread's dispatcher would.
    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> queue = new();

        public int Count { get { lock (queue) return queue.Count; } }

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (queue) queue.Enqueue((d, state));
        }

        public void RunAll()
        {
            while (true)
            {
                (SendOrPostCallback Callback, object? State) next;
                lock (queue)
                {
                    if (queue.Count == 0) return;
                    next = queue.Dequeue();
                }
                next.Callback(next.State);
            }
        }
    }

    // A stand-in for raw.githubusercontent.com: records each request and answers with a made-up file in NotMeter's shape,
    // listing Gartua on Asia's 1503 and Bodyguard Teegant on NA West's 2205. It can hold its first answer until released
    // (holdFirst), ignoring cancellation as a slow network may, or never answer until cancelled (Hang).
    private sealed class FakeFeed(DateTimeOffset? respawn = null, DateTimeOffset? observed = null, string? etag = null, bool holdFirst = false) : HttpMessageHandler
    {
        private readonly TaskCompletionSource first = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentQueue<(Uri? Uri, Dictionary<string, string> Headers)> Requests { get; } = new();

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public int Padding { get; set; }

        public bool Hang { get; set; }

        public void WaitFor(int count) =>
            Assert.True(SpinWait.SpinUntil(() => Requests.Count >= count, TimeSpan.FromSeconds(5)), $"fewer than {count} requests");

        public void ReleaseFirst() => first.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue((request.RequestUri, request.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value))));
            if (Hang) await Task.Delay(Timeout.Infinite, cancellationToken);
            if (holdFirst && Requests.Count == 1) await first.Task;
            var response = new HttpResponseMessage(Status);
            if (Status == HttpStatusCode.OK)
            {
                var back = (respawn ?? Start.AddMinutes(90)).ToUnixTimeMilliseconds();
                var seen = (observed ?? Start).ToUnixTimeMilliseconds();
                var file = JsonSerializer.Serialize(new
                {
                    schema = NotMeterFieldBossFeed.Schema,
                    version = 1,
                    serviceRegion = "WW",
                    globalRegion = "as",
                    generatedAt = seen / 1000,
                    padding = new string('x', Padding),
                    servers = new[]
                    {
                        new { serverId = Server, maps = new[] { new { mapId = 1010, observedAt = seen / 1000, entries = new[] { new { observedAtMs = seen, bossCode = 2101074, targetAt = back } } } } },
                        new { serverId = 2205, maps = new[] { new { mapId = 1010, observedAt = seen / 1000, entries = new[] { new { observedAtMs = seen, bossCode = 2100079, targetAt = back } } } } },
                    },
                });
                response.Content = new StringContent(file, Encoding.UTF8, "application/json");
                if (etag is not null) response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
            }
            return response;
        }
    }
}
