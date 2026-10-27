using System.Reflection;
using AionDpsMeter.Services.Models;
using AionDpsMeter.Services.PacketCapture;
using AionDpsMeter.Services.Services.Session;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using SharpPcap;
using ScarletMeter.Engine;
using ScarletMeter.Engine.Capture;
using ScarletMeter.Engine.Upstream;
using static ScarletMeter.Tests.SyntheticRecords;

namespace ScarletMeter.Tests;

/// <summary>
/// The capture lock guard (CaptureLockWatch, CaptureLockGuard): a lock that has carried no server-time record from the
/// game is dropped after 30 s on the web's ports, or 5 minutes on any other, and upstream's detection starts again; one
/// that has carried a record, through upstream's real pipeline, is the game's and is kept. Adapter names, ports and
/// records are made up.
/// </summary>
public sealed class CaptureLockTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static readonly CaptureLock Web = new("adapter", 443);
    private static readonly CaptureLock Game = new("adapter", 13328);

    // Harry's case on 2026-10-27: a lock on port 443 without the game's records goes after 30 s, not before.
    [Fact]
    public void ALockOnAWebPortWithoutTheGamesRecordIsDroppedAfterThirtySeconds()
    {
        var clock = new FakeTimeProvider(Start);
        var watch = new CaptureLockWatch();
        Assert.False(Check(watch, Web, null, clock));
        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.False(Check(watch, Web, null, clock));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(watch.ShouldRestart(Web, null, clock.GetTimestamp(), clock, out var quiet));
        Assert.Equal(TimeSpan.FromSeconds(30), quiet);
    }

    // Any other port gets 5 minutes: a lock made at character select, where the game's record may not come, is rarely
    // reached; and longer than upstream's watchdog takes to drop a port that goes silent (up to 120 s), so a game that
    // closes is left to upstream. Port 80 is the web's too.
    [Theory]
    [InlineData(13328, 300)]
    [InlineData(27015, 300)]
    [InlineData(80, 30)]
    [InlineData(443, 30)]
    public void TheQuietLimitDependsOnThePort(int port, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), CaptureLockWatch.QuietLimit(port));
        var clock = new FakeTimeProvider(Start);
        var watch = new CaptureLockWatch();
        var locked = new CaptureLock("adapter", port);
        Assert.False(Check(watch, locked, null, clock));
        clock.Advance(TimeSpan.FromSeconds(seconds - 1));
        Assert.False(Check(watch, locked, null, clock));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(Check(watch, locked, null, clock));
    }

    // A lock that has carried one of the game's records (its ping's reply) is the game's, even on a web port (a relay
    // that tunnels the game there): kept however long the records then stop (at character select, a loading screen),
    // which leaves it to upstream's watchdog (audit on #54: the game's own lock would have been dropped every few
    // minutes at character select). One record is enough, if it came after the lock was first seen.
    [Theory]
    [InlineData(443, 29)]
    [InlineData(443, 1)]
    [InlineData(13328, 299)]
    public void ALockThatCarriedTheGamesRecordIsKept(int port, int recordAfter)
    {
        var clock = new FakeTimeProvider(Start);
        var watch = new CaptureLockWatch();
        var locked = new CaptureLock("adapter", port);
        Assert.False(Check(watch, locked, null, clock));
        clock.Advance(TimeSpan.FromSeconds(recordAfter));
        var record = clock.GetTimestamp();
        Assert.False(Check(watch, locked, record, clock));
        for (var minute = 0; minute < 120; minute++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            Assert.False(Check(watch, locked, record, clock));
        }
        Assert.False(Check(watch, null, record, clock)); // detecting: the next lock must prove itself again
        Assert.False(Check(watch, locked, record, clock));
        clock.Advance(CaptureLockWatch.QuietLimit(port));
        Assert.True(Check(watch, locked, record, clock));
    }

    // A record from before the lock (the game's, before it closed) does not count for a lock made since: the quiet time
    // runs from the lock.
    [Fact]
    public void ARecordFromBeforeTheLockDoesNotCount()
    {
        var clock = new FakeTimeProvider(Start);
        var watch = new CaptureLockWatch();
        var before = clock.GetTimestamp();
        clock.Advance(TimeSpan.FromHours(1));
        Assert.False(Check(watch, Web, before, clock));
        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.False(Check(watch, Web, before, clock));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(Check(watch, Web, before, clock));
    }

    // A new lock is timed afresh: another port, detection in between, or the same port again after a restart.
    [Fact]
    public void EachLockIsTimedAfresh()
    {
        var clock = new FakeTimeProvider(Start);
        var watch = new CaptureLockWatch();
        Assert.False(Check(watch, Game, null, clock));
        clock.Advance(TimeSpan.FromSeconds(140));
        Assert.False(Check(watch, Web, null, clock)); // another port: from now
        clock.Advance(TimeSpan.FromSeconds(20));
        Assert.False(Check(watch, null, null, clock)); // detecting
        Assert.False(Check(watch, Web, null, clock)); // locked again: from now
        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.False(Check(watch, Web, null, clock));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(Check(watch, Web, null, clock));
        Assert.False(Check(watch, Web, null, clock)); // the same port after the restart: from now
        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.False(Check(watch, Web, null, clock));
        clock.Advance(TimeSpan.FromSeconds(2)); // the lock before would now be 31 s old
        var other = new CaptureLock("other adapter", 443);
        Assert.False(Check(watch, other, null, clock)); // another adapter, the same port: from now
        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.False(Check(watch, other, null, clock));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(Check(watch, other, null, clock));
    }

    [Fact]
    public void NoLockIsNothingToDrop()
    {
        var clock = new FakeTimeProvider(Start);
        var watch = new CaptureLockWatch();
        for (var i = 0; i < 100; i++)
        {
            Assert.False(Check(watch, null, null, clock));
            clock.Advance(TimeSpan.FromMinutes(1));
        }
    }

    // The guard over a made-up lock: started, it drops a 443 lock after 30 s, once, and says so; the lock then gone, it
    // does nothing more.
    [Fact]
    public void TheGuardRestartsDetectionOnAStaleLock()
    {
        var clock = new FakeTimeProvider(Start);
        var upstream = new FakeLock(Web);
        var log = new RecordingLogger<CaptureLockGuard>();
        using var host = new TestEngineHost(CaptureMode.None);
        using var guard = NewGuard(host, upstream, clock, log, out _);
        guard.Start();
        clock.Advance(CaptureLockGuard.CheckInterval); // first seen
        clock.Advance(TimeSpan.FromSeconds(25));
        Assert.Equal(0, upstream.Restarts);
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(1, upstream.Restarts);
        Assert.Null(upstream.Locked);
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(1, upstream.Restarts);
        var warning = Assert.Single(log.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("port 443 for 30 s", warning.Message, StringComparison.Ordinal);
    }

    // Through upstream's real reassembly and pipeline: the game's server-time record (03 36) on a 443 lock (as a relay
    // over 443 would carry it) makes it the game's, kept after the records stop; a lock made after detection must carry
    // one again, and without it goes after 30 s.
    [Fact]
    public void TheGamesServerTimeRecordThroughUpstreamKeepsTheLock()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        _ = host.Services.GetRequiredService<IPacketService>(); // subscribes upstream's pipeline to the stream
        var stream = host.Services.GetRequiredService<TcpStreamBuffer>();
        var sessions = host.Services.GetRequiredService<CombatSessionManager>();
        var clock = new FakeTimeProvider(Start);
        var upstream = new FakeLock(Web);
        using var guard = new CaptureLockGuard(new RunningCapture(), sessions, clock, NullLogger<CaptureLockGuard>.Instance, upstream.Binding);
        guard.Start();
        Synchronise(stream);
        clock.Advance(CaptureLockGuard.CheckInterval); // first seen
        clock.Advance(TimeSpan.FromSeconds(10));
        stream.AddData("synthetic", ServerTime(), 0);
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(0, upstream.Restarts);
        upstream.Locked = null; // upstream's watchdog went back to detection
        clock.Advance(CaptureLockGuard.CheckInterval);
        upstream.Locked = Web; // and locked again
        clock.Advance(CaptureLockGuard.CheckInterval);
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(1, upstream.Restarts);
    }

    // Capture not running (stopped, or never started): nothing is read or dropped.
    [Fact]
    public void NothingIsDroppedWhileCaptureIsNotRunning()
    {
        var clock = new FakeTimeProvider(Start);
        var upstream = new FakeLock(Web);
        using var host = new TestEngineHost(CaptureMode.None);
        using var guard = NewGuard(host, upstream, clock, NullLogger<CaptureLockGuard>.Instance, out var capture);
        capture.Running = false;
        guard.Start();
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(0, upstream.Reads);
        Assert.Equal(0, upstream.Restarts);
    }

    // Stopped or disposed, the guard checks no more; started twice, it checks once per interval.
    [Fact]
    public void AStoppedGuardChecksNoMore()
    {
        var clock = new FakeTimeProvider(Start);
        var upstream = new FakeLock(Web);
        using var host = new TestEngineHost(CaptureMode.None);
        var guard = NewGuard(host, upstream, clock, NullLogger<CaptureLockGuard>.Instance, out _);
        guard.Start();
        guard.Start();
        Assert.True(guard.Checking);
        clock.Advance(CaptureLockGuard.CheckInterval);
        Assert.Equal(1, upstream.Reads);
        guard.Stop();
        Assert.False(guard.Checking);
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(1, upstream.Reads);
        guard.Start();
        guard.Dispose();
        guard.Dispose(); // twice is fine
        guard.Start(); // and starts nothing once disposed
        Assert.False(guard.Checking);
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(1, upstream.Reads);
    }

    // A failing check (here upstream's lock cannot be read) is contained, logged once while it lasts and once more when
    // checking works again.
    [Fact]
    public void AFailingCheckIsContainedAndLoggedOnce()
    {
        var clock = new FakeTimeProvider(Start);
        var upstream = new FakeLock(Web) { Failing = true };
        var log = new RecordingLogger<CaptureLockGuard>();
        using var host = new TestEngineHost(CaptureMode.None);
        using var guard = NewGuard(host, upstream, clock, log, out _);
        guard.Start();
        for (var i = 0; i < 5; i++) clock.Advance(CaptureLockGuard.CheckInterval);
        upstream.Failing = false;
        clock.Advance(CaptureLockGuard.CheckInterval);
        Assert.Single(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("failed", StringComparison.Ordinal));
        Assert.Single(log.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("works again", StringComparison.Ordinal));
    }

    // On real threads (audit on #54: none of this was tested): Stop waits for a check under way (here a slow reset)
    // before it returns, so the engine stops capture only after it; a tick that comes meanwhile is skipped, not queued
    // behind it; and a check that begins after Stop (a callback already dispatched) does nothing.
    [Fact]
    public async Task StopWaitsForACheckUnderWayAndNoneRunsAfter()
    {
        var token = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(Start);
        using var hold = new ManualResetEventSlim();
        var upstream = new FakeLock(Web) { HoldRestart = hold };
        using var host = new TestEngineHost(CaptureMode.None);
        using var guard = NewGuard(host, upstream, clock, NullLogger<CaptureLockGuard>.Instance, out _);
        guard.Start();
        // One tick per step (the fake clock fires a periodic timer once an advance): first seen at 5 s, the reset at 35 s.
        var checking = Task.Run(() => { for (var i = 0; i < 7; i++) clock.Advance(CaptureLockGuard.CheckInterval); }, token);
        Assert.True(upstream.RestartEntered.Wait(Patience, token));
        var reads = upstream.Reads;
        await Task.Run(guard.Check, token).WaitAsync(Patience, token); // another tick while the reset runs: skipped
        Assert.Equal(reads, upstream.Reads);
        var stopping = Task.Run(guard.Stop, token);
        Assert.NotSame(stopping, await Task.WhenAny(stopping, Task.Delay(TimeSpan.FromMilliseconds(300), token)));
        hold.Set();
        await stopping.WaitAsync(Patience, token);
        await checking.WaitAsync(Patience, token);
        upstream.Locked = Web;
        guard.Check(); // a callback that began as Stop ran
        Assert.Equal(reads, upstream.Reads);
    }

    // Disposed, the guard stops listening to upstream's ping (it holds the session manager's event otherwise).
    [Fact]
    public void ADisposedGuardStopsListening()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var sessions = host.Services.GetRequiredService<CombatSessionManager>();
        var before = PingListeners(sessions);
        var guard = NewGuard(host, new FakeLock(Web), new FakeTimeProvider(Start), NullLogger<CaptureLockGuard>.Instance, out _);
        Assert.Equal(before + 1, PingListeners(sessions));
        guard.Dispose();
        Assert.Equal(before, PingListeners(sessions));
    }

    // The engine starts the guard once capture runs, and stops it before capture, on Stop and on Dispose, so no reset can
    // reopen the adapters capture is closing. Whether it was still checking is noted as capture stops, not asserted
    // there: the engine's Dispose logs what its shutdown throws.
    [Fact]
    public void TheEngineStopsTheGuardBeforeCapture()
    {
        CaptureLockGuard? guard = null;
        var checkingAtStop = new List<bool>();
        var capture = new RunningCapture { Running = false, AtStop = () => checkingAtStop.Add(guard!.Checking) };
        using var host = new TestEngineHost(CaptureMode.None, services =>
        {
            services.RemoveAll<IPacketCaptureDevice>();
            services.AddSingleton<IPacketCaptureDevice>(capture);
            services.RemoveAll<CaptureLockGuard>();
            services.AddSingleton(provider => new CaptureLockGuard(
                capture, provider.GetRequiredService<CombatSessionManager>(), new FakeTimeProvider(Start),
                NullLogger<CaptureLockGuard>.Instance, new FakeLock(null).Binding));
        });
        var engine = host.Services.GetRequiredService<ICombatEngine>();
        guard = host.Services.GetRequiredService<CaptureLockGuard>();
        Assert.False(guard.Checking);
        engine.Start();
        Assert.True(guard.Checking);
        engine.Stop();
        engine.Start();
        Assert.True(guard.Checking);
        engine.Dispose();
        Assert.Equal([false, false], checkingAtStop);
        Assert.False(guard.Checking);
    }

    // Live, the engine's guard is bound to upstream's capture; in demo mode and with none, it is inert.
    [Theory]
    [InlineData(CaptureMode.Live, true)]
    [InlineData(CaptureMode.Demo, false)]
    [InlineData(CaptureMode.None, false)]
    public void TheGuardIsBoundOnlyToUpstreamsCapture(CaptureMode mode, bool active)
    {
        using var host = new TestEngineHost(mode);
        Assert.Equal(active, host.Services.GetRequiredService<CaptureLockGuard>().Active);
    }

    // The reflection binds to upstream's private lockedPort and ResetToDetection hold for the pinned release: the lock
    // reads as the adapter's name with the port, and the reset (upstream's own, with no adapter to reopen here) clears it
    // and says so. An update of the pin that renames or reshapes them fails here.
    [Fact]
    public void TheGuardBindsToThePinnedUpstream()
    {
        var log = new RecordingLogger<CaptureDevice>();
        using var device = new CaptureDevice(log, new TcpStreamBuffer(NullLogger<TcpStreamBuffer>.Instance));
        var binding = CaptureLockGuard.Bind(device);
        Assert.NotNull(binding);
        Assert.Null(binding.ReadLock()); // detecting
        LockForTest(device, "made-up adapter", 443);
        Assert.Equal(new CaptureLock("made-up adapter", 443), binding.ReadLock());
        binding.Restart();
        Assert.Null(binding.ReadLock());
        Assert.Contains(log.Entries, e => e.Message.Contains("Detection mode re-started", StringComparison.Ordinal));
    }

    // How many handlers upstream's session manager has on its ping event (the field behind it, read by reflection).
    private static int PingListeners(CombatSessionManager sessions) =>
        (typeof(CombatSessionManager).GetField(nameof(CombatSessionManager.PingUpdated), BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(sessions) as Delegate)?.GetInvocationList().Length ?? 0;

    private static bool Check(CaptureLockWatch watch, CaptureLock? locked, long? lastGameRecord, FakeTimeProvider clock) =>
        watch.ShouldRestart(locked, lastGameRecord, clock.GetTimestamp(), clock, out _);

    // A guard over a made-up lock and a capture that runs, listening to the session manager of the host's graph.
    private static CaptureLockGuard NewGuard(TestEngineHost host, FakeLock upstream, TimeProvider clock, ILogger<CaptureLockGuard> logger, out RunningCapture capture)
    {
        capture = new RunningCapture();
        return new CaptureLockGuard(capture, host.Services.GetRequiredService<CombatSessionManager>(), clock, logger, upstream.Binding);
    }

    // The server-time record upstream reads the ping from: 03 36, two bytes, then an 8-byte clock value (made up here,
    // so upstream makes ping 0 of it; the record counts all the same).
    private static byte[] ServerTime() => Frame(0x3603, "00 00 01 02 03 04 05 06 07 08");

    // Puts upstream's device into the state its CommitLock leaves (an adapter and a port), as a lock would, without Npcap.
    private static void LockForTest(CaptureDevice device, string adapterName, int port)
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        var adapter = DispatchProxy.Create<ICaptureDevice, NamedCaptureDevice>();
        ((NamedCaptureDevice)(object)adapter).Name = adapterName;
        var contextType = typeof(CaptureDevice).Assembly.GetType("AionDpsMeter.Services.PacketCapture.AdapterContext", throwOnError: true)!;
        var kindType = typeof(CaptureDevice).Assembly.GetType("AionDpsMeter.Services.PacketCapture.AdapterKind", throwOnError: true)!;
        var context = Activator.CreateInstance(contextType, Private | BindingFlags.Public, binder: null, [adapter, Enum.Parse(kindType, "Physical")], culture: null);
        typeof(CaptureDevice).GetField("lockedAdapter", Private)!.SetValue(device, context);
        typeof(CaptureDevice).GetField("lockedPort", Private)!.SetValue(device, port);
        typeof(CaptureDevice).GetField("isLocked", Private)!.SetValue(device, true);
    }

    /// <summary>A capture adapter that has only a name: all upstream's DeviceName reads of it.</summary>
    public class NamedCaptureDevice : DispatchProxy
    {
        public string Name { get; set; } = string.Empty;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == "get_Name" ? Name : throw new NotSupportedException(targetMethod?.Name);
    }

    // Upstream's lock as the test sets it: read through the binding, cleared by its restart.
    private sealed class FakeLock(CaptureLock? locked)
    {
        public CaptureLock? Locked { get; set; } = locked;

        // When set, the restart waits for it (a slow reset), after setting RestartEntered.
        public ManualResetEventSlim? HoldRestart { get; set; }

        public ManualResetEventSlim RestartEntered { get; } = new();

        public int Reads { get; private set; }

        public int Restarts { get; private set; }

        public bool Failing { get; set; }

        public CaptureLockGuard.Binding Binding => new(
            () =>
            {
                if (Failing) throw new InvalidOperationException("A failing read.");
                Reads++;
                return Locked;
            },
            () =>
            {
                Restarts++;
                Locked = null;
                RestartEntered.Set();
                HoldRestart?.Wait(TimeSpan.FromSeconds(30));
            });
    }

    // A capture device that says it is capturing (or not, as set), and can note something as it is stopped.
    private sealed class RunningCapture : IPacketCaptureDevice
    {
        public bool Running { get; set; } = true;

        public Action? AtStop { get; set; }

        public bool IsCapturing => Running;

        public string? DeviceName => null;

        public void DiscoverAdapters()
        {
        }

        public void StartCapture() => Running = true;

        public void StopCapture()
        {
            AtStop?.Invoke();
            Running = false;
        }

        public void Dispose()
        {
        }
    }
}
