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
/// The capture lock guard (CaptureLockWatch, CaptureLockGuard): a lock that gets no server-time record from the game for
/// 30 s on the web's ports, or 150 s on any other, is dropped and upstream's detection starts again; the game's records,
/// through upstream's real pipeline, keep a lock. Adapter names, ports and records are made up.
/// </summary>
public sealed class CaptureLockTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 27, 16, 23, 0, TimeSpan.Zero);
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

    // Any other port gets 150 s: longer than upstream's watchdog takes to drop a port that goes silent (up to 120 s),
    // so a game that closes is left to upstream. Port 80 is the web's too.
    [Theory]
    [InlineData(13328, 150)]
    [InlineData(27015, 150)]
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

    // The game's record every ten seconds (its ping) keeps a lock, even on a web port (a relay that tunnels the game
    // there); the quiet time counts from the last one.
    [Fact]
    public void TheGamesRecordsKeepALock()
    {
        var clock = new FakeTimeProvider(Start);
        var watch = new CaptureLockWatch();
        long? last = null;
        for (var second = 0; second < 600; second += 5)
        {
            if (second % 10 == 0) last = clock.GetTimestamp();
            Assert.False(Check(watch, Web, last, clock));
            clock.Advance(TimeSpan.FromSeconds(5));
        }
        clock.Advance(TimeSpan.FromSeconds(19)); // the last record was at 590 s: now 29 s since
        Assert.False(Check(watch, Web, last, clock));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(Check(watch, Web, last, clock));
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
        Assert.False(Check(watch, new CaptureLock("other adapter", 443), null, clock)); // another adapter: from now
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

    // Through upstream's real reassembly and pipeline: the game's server-time record (03 36) every ten seconds keeps a
    // 443 lock (as a relay over 443 would); once they stop, the lock goes 30 s after the last.
    [Fact]
    public void TheGamesServerTimeRecordsThroughUpstreamKeepTheLock()
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
        for (var second = 0; second < 300; second += 5)
        {
            if (second % 10 == 0) stream.AddData("synthetic", ServerTime(), 0);
            clock.Advance(TimeSpan.FromSeconds(5));
        }
        Assert.Equal(0, upstream.Restarts);
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
        public CaptureLock? Locked { get; private set; } = locked;

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
