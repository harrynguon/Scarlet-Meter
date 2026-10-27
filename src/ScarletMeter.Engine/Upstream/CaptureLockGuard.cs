using System.Reflection;
using AionDpsMeter.Services.PacketCapture;
using AionDpsMeter.Services.Services.Session;
using Microsoft.Extensions.Logging;
using ScarletMeter.Engine.Capture;

namespace ScarletMeter.Engine.Upstream;

/// <summary>
/// Restarts upstream's detection when its capture is locked onto traffic that is not the game's, as
/// <see cref="CaptureLockWatch"/> decides: every <see cref="CheckInterval"/> while capture runs, it reads upstream's lock
/// and the time of the game's last server-time record (upstream's <see cref="CombatSessionManager.PingUpdated"/>), and
/// when the rule says so calls upstream's own reset, the one its watchdog uses (<c>CaptureDevice.ResetToDetection</c>,
/// private, bound by reflection once; a test guards the binding against the pinned release). Upstream's
/// <see cref="CaptureDevice.StopCapture"/> cannot be followed by a start (it cancels a token the device never renews),
/// so the reset is the only way back to detection short of restarting the meter.
/// </summary>
/// <remarks>
/// <para>Inert with any other capture device (the demo's, none), or when the pinned release no longer has the members
/// it binds, which it logs once.</para>
/// <para>Threads: the server-time record comes on the capture thread and only stores a timestamp. The check runs on a
/// timer thread under the gate, which <see cref="Stop"/> and <see cref="Dispose"/> take too, so once they return no
/// check is under way and none will start: the engine stops the guard before capture, so a reset cannot reopen the
/// adapters capture has just closed. A tick that finds a check still running is skipped. Upstream's watchdog runs on
/// its own timer and calls the same reset; the two could reset at once only if the watchdog ran its check in the
/// instant this one resets (a reset stops the watchdog, and it finds no lock to check after one). Two resets at once
/// could each add a packet handler to an adapter the lock had closed (upstream adds it before opening, and SharpPcap's
/// open of an adapter already open returns without a word), so that adapter's packets would be queued twice until it
/// is next closed; once locked, upstream's sequence check drops the copies. An adapter that fails to open keeps the
/// handler each reset adds, since it is never closed (no adapter failed to open in Harry's logs). Both are upstream's
/// to fix (docs/HANDOFF.md). Nothing escapes: an exception from a timer callback would end the process.</para>
/// </remarks>
internal sealed class CaptureLockGuard : IDisposable
{
    /// <summary>How often the lock is checked.</summary>
    internal static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

    private readonly IPacketCaptureDevice capture;
    private readonly CombatSessionManager sessions;
    private readonly TimeProvider clock;
    private readonly ILogger<CaptureLockGuard> logger;
    private readonly Binding? upstream;
    private readonly CaptureLockWatch watch = new(); // touched only under the gate
    private readonly Lock gate = new();
    private ITimer? timer; // non-null while checking
    private long lastGameRecord; // a timestamp on clock, 0 for none; written on the capture thread
    private bool failing;
    private bool disposed;

    public CaptureLockGuard(IPacketCaptureDevice capture, CombatSessionManager sessions, TimeProvider clock, ILogger<CaptureLockGuard> logger)
        : this(capture, sessions, clock, logger, capture is CaptureDevice device ? Bind(device) : null)
    {
        if (capture is CaptureDevice && upstream is null)
            logger.LogWarning("A capture locked onto traffic that is not the game's is left to upstream's watchdog: upstream's CaptureDevice no longer has the lockedPort field or the ResetToDetection method this reads and calls.");
    }

    /// <summary>With upstream's lock and reset of the caller's (null for none): the constructor above, and tests.</summary>
    internal CaptureLockGuard(IPacketCaptureDevice capture, CombatSessionManager sessions, TimeProvider clock, ILogger<CaptureLockGuard> logger, Binding? upstream)
    {
        this.capture = capture;
        this.sessions = sessions;
        this.clock = clock;
        this.logger = logger;
        this.upstream = upstream;
        if (upstream is not null) sessions.PingUpdated += OnServerTime;
    }

    /// <summary>Upstream's lock as it stands (null while detecting), and its reset to detection.</summary>
    internal sealed record Binding(Func<CaptureLock?> ReadLock, Action Restart);

    /// <summary>Whether the guard is bound to upstream's capture (false with another device, or a release it cannot bind).</summary>
    internal bool Active => upstream is not null;

    /// <summary>Whether the guard is checking: started and not stopped since.</summary>
    internal bool Checking
    {
        get
        {
            lock (gate) return timer is not null;
        }
    }

    /// <summary>
    /// Binds to the pinned release's CaptureDevice: the lock is its adapter's name (the public DeviceName, set while
    /// locked) with the private lockedPort, and the reset its private ResetToDetection(). Null when either member is
    /// missing or has another shape.
    /// </summary>
    internal static Binding? Bind(CaptureDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        var port = typeof(CaptureDevice).GetField("lockedPort", Private);
        var reset = typeof(CaptureDevice).GetMethod("ResetToDetection", Private, binder: null, Type.EmptyTypes, modifiers: null);
        if (port?.FieldType != typeof(int) || reset?.ReturnType != typeof(void)) return null;
        Action restart;
        try
        {
            restart = reset.CreateDelegate<Action>(device);
        }
        catch (ArgumentException)
        {
            return null;
        }
        return new Binding(
            () => device.DeviceName is { } adapter && (int)port.GetValue(device)! is > 0 and var locked ? new CaptureLock(adapter, locked) : null,
            restart);
    }

    /// <summary>Starts checking (the engine's start, once capture runs). Nothing when inert or already checking.</summary>
    public void Start()
    {
        if (upstream is null) return;
        lock (gate)
        {
            if (disposed || timer is not null) return;
            timer = clock.CreateTimer(_ => Check(), null, CheckInterval, CheckInterval);
        }
    }

    /// <summary>Stops checking (the engine's stop, before capture's); waits for a check under way to finish.</summary>
    public void Stop()
    {
        lock (gate)
        {
            timer?.Dispose();
            timer = null;
        }
    }

    // The game's server-time record was read (on the capture thread): only its time is kept.
    private void OnServerTime(object? sender, int milliseconds) => Volatile.Write(ref lastGameRecord, clock.GetTimestamp());

    /// <summary>
    /// One check (the timer's callback; tests call it as a callback already under way): drops a lock the rule says is
    /// not the game's. Skipped while another runs, and does nothing once the guard is stopped.
    /// </summary>
    internal void Check()
    {
        if (!gate.TryEnter()) return;
        try
        {
            if (timer is null || upstream is null) return;
            var now = capture.IsCapturing ? upstream.ReadLock() : null;
            var last = Volatile.Read(ref lastGameRecord);
            if (watch.ShouldRestart(now, last == 0 ? null : last, clock.GetTimestamp(), clock, out var quiet) && now is { } stale)
            {
                logger.LogWarning(
                    "Capture was locked onto port {Port} for {Seconds:0} s without a server-time record from the game, so it is likely other traffic; looking for the game's connection again.",
                    stale.Port, quiet.TotalSeconds);
                upstream.Restart();
            }
            if (failing) logger.LogInformation("Checking the capture's lock works again.");
            failing = false;
        }
        catch (Exception ex)
        {
            if (!failing) logger.LogWarning(ex, "Checking the capture's lock failed; trying again at the next check.");
            failing = true;
        }
        finally
        {
            gate.Exit();
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            timer?.Dispose();
            timer = null;
        }
        if (upstream is not null) sessions.PingUpdated -= OnServerTime;
    }
}
