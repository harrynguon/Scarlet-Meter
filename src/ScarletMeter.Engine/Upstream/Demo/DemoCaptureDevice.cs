using AionDpsMeter.Services.PacketCapture;
using AionDpsMeter.Services.Services.Entity;
using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.Services.Services.Session.Persistence;
using Microsoft.Extensions.Logging;

namespace ScarletMeter.Engine.Upstream.Demo;

/// <summary>
/// The capture device of <see cref="CaptureMode.Demo"/>: instead of opening an adapter it runs
/// <see cref="DemoCombatFeed"/> on a background thread of its own, so upstream's pipeline starts and stops it
/// exactly as it starts and stops live capture (the engine's Start, Stop and Dispose all reach it). It reports
/// capturing on adapter "Demo" while the feed runs.
/// </summary>
/// <remarks>
/// Lifecycle invariant: at most one feed thread exists, and <see cref="StopCapture"/> returns only once it has
/// ended (or after <see cref="StopTimeout"/>, logged), so no thread outlives a stop. Nothing escapes the thread:
/// the feed logs a failing event and carries on (<see cref="DemoCombatFeed.Step"/>); anything that still
/// escapes a step is logged and retried after a pause, and the feed gives up, logged, after
/// <see cref="MaxConsecutiveFailures"/> such failures in a row; capture then reads as off.
/// </remarks>
internal sealed class DemoCaptureDevice : IPacketCaptureDevice
{
    /// <summary>The adapter name reported while the feed runs.</summary>
    public const string AdapterName = "Demo";

    private const int MaxConsecutiveFailures = 10;
    private static readonly TimeSpan StepInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan FailurePause = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);

    private readonly EntityTracker entities;
    private readonly CombatSessionManager sessions;
    private readonly ICombatHistoryStore history;
    private readonly ILoggerFactory loggers;
    private readonly ILogger logger;
    private readonly Lock gate = new();
    private DemoCombatFeed? feed;
    private Thread? thread;
    private CancellationTokenSource? stopping;
    private volatile bool feedRunning;
    private bool disposed;

    public DemoCaptureDevice(
        EntityTracker entities, CombatSessionManager sessions, ICombatHistoryStore history, ILoggerFactory loggers)
    {
        this.entities = entities;
        this.sessions = sessions;
        this.history = history;
        this.loggers = loggers;
        logger = loggers.CreateLogger<DemoCaptureDevice>();
    }

    public bool IsCapturing => feedRunning;

    public string? DeviceName => feedRunning ? AdapterName : null;

    /// <summary>The feed's thread while it runs, for tests that check it ends.</summary>
    internal Thread? FeedThread
    {
        get { lock (gate) { return thread; } }
    }

    public void DiscoverAdapters()
    {
    }

    /// <summary>Starts the feed thread unless it runs already.</summary>
    /// <exception cref="CaptureStartException">The demo roster does not match upstream's game data.</exception>
    public void StartCapture()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (thread is { IsAlive: true }) return;
            ReleaseStopped();

            // Built once, on the first start, so a roster that no longer matches the game data fails Start with
            // its own message; later starts resume the same feed (fight numbers and boss ids carry on).
            try
            {
                feed ??= new DemoCombatFeed(entities, sessions, history, loggers.CreateLogger<DemoCombatFeed>(), new Random());
            }
            catch (InvalidOperationException ex)
            {
                throw new CaptureStartException("The demo could not start: " + ex.Message, ex);
            }

            var token = (stopping = new CancellationTokenSource()).Token;
            var running = feed;
            feedRunning = true;
            thread = new Thread(() => Run(running, token)) { IsBackground = true, Name = "Scarlet demo feed" };
            thread.Start();
        }
    }

    /// <summary>Stops the feed thread and waits for it to end.</summary>
    public void StopCapture()
    {
        lock (gate)
        {
            if (thread is null) return;
            stopping!.Cancel();
            // The feed thread never takes the gate, so joining under it cannot deadlock; holding it makes a
            // concurrent start wait until this thread has gone.
            if (!thread.Join(StopTimeout))
            {
                logger.LogWarning("The demo feed did not stop within {Timeout}; it ends on its own.", StopTimeout);
                return;
            }
            ReleaseStopped();
        }
    }

    public void Dispose()
    {
        StopCapture();
        lock (gate) disposed = true;
    }

    // Forgets a thread that has ended (stopped, or given up after failures) and its token source.
    private void ReleaseStopped()
    {
        if (thread is { IsAlive: true }) return;
        thread = null;
        stopping?.Dispose();
        stopping = null;
    }

    private void Run(DemoCombatFeed running, CancellationToken token)
    {
        var failures = 0;
        try
        {
            running.Begin(DateTime.Now);
            while (!token.IsCancellationRequested)
            {
                try
                {
                    running.Step(DateTime.Now);
                    failures = 0;
                }
                catch (Exception ex)
                {
                    failures++;
                    logger.LogError(ex, "Demo feed step failed ({Failures} of {Max} in a row).", failures, MaxConsecutiveFailures);
                    if (failures >= MaxConsecutiveFailures)
                    {
                        logger.LogError("The demo feed stopped after {Max} failures in a row.", MaxConsecutiveFailures);
                        return;
                    }
                    token.WaitHandle.WaitOne(FailurePause);
                    continue;
                }
                token.WaitHandle.WaitOne(StepInterval);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The demo feed could not begin.");
        }
        finally
        {
            feedRunning = false;
        }
    }
}
