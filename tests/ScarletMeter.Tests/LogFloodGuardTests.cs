using System.Globalization;
using System.IO;
using Microsoft.Extensions.Time.Testing;
using ScarletMeter.App.Logging;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace ScarletMeter.Tests;

/// <summary>
/// The log's guard against upstream's flood of TCP gap warnings (<see cref="LogFloodGuard"/> with the meter's own
/// rule, <see cref="MeterLog.TcpGaps"/>), over synthetic Serilog events and a fake clock: a burst's first five are
/// written and the rest counted once a minute until it stops, an isolated gap is written, every other event passes
/// untouched, and closing the log writes what is still counted. The events name upstream's category and text as
/// literals, so a change to the rule's constants fails here (LESSONS: a test named an id through its constant).
/// </summary>
public sealed class LogFloodGuardTests
{
    private const string CaptureCategory = "AionDpsMeter.Services.PacketCapture.CaptureDevice";
    private static readonly DateTimeOffset Start = new(2026, 11, 4, 21, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);
    private static readonly MessageTemplateParser Parser = new();

    private readonly FakeTimeProvider clock = new(Start);
    private readonly RecordingSink file = new();

    [Fact]
    public void ABurstWritesItsFirstFiveThenACountAMinuteLater()
    {
        // The PC's time zone is not UTC, so a summary stamped in UTC would stand out among the file's local times.
        clock.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("Synthetic+13", TimeSpan.FromHours(13), "Synthetic+13", "Synthetic+13"));
        using var guard = NewGuard();
        var gaps = new List<LogEvent>();
        for (var i = 0; i < 1000; i++)
        {
            gaps.Add(Gap(i));
            guard.Emit(gaps[^1]);
            clock.Advance(TimeSpan.FromMilliseconds(10));
        }

        Assert.Equal(gaps.Take(5), file.Events);

        // The count comes a minute after the first counted gap (the sixth, at 50 ms), not before.
        clock.SetUtcNow(At(60_050) - TimeSpan.FromTicks(1));
        Assert.Equal(5, file.Events.Count);
        clock.Advance(TimeSpan.FromTicks(1));

        var summary = Assert.Single(Summaries());
        Assert.Equal(
            "[TCP GAP] 995 more not written, from 21:00:00.050 to 21:00:09.990 (a burst's first 5 are written, then a count every 60 s)",
            summary.RenderMessage(CultureInfo.InvariantCulture));
        Assert.Equal(LogEventLevel.Warning, summary.Level);
        Assert.Equal(CaptureCategory, Category(summary));
        Assert.Equal(clock.GetUtcNow(), summary.Timestamp);
        Assert.Equal(TimeSpan.FromHours(13), summary.Timestamp.Offset);
    }

    [Fact]
    public void AFloodIsCountedEachMinuteUntilItStopsAndTheNextBurstIsWrittenAgain()
    {
        using var guard = NewGuard();
        // A gap every 100 ms for 150 s: five written, then counts at 60.5 s, 120.5 s and a minute after the last
        // counted period began (180.5 s), though the flood stopped at 149.9 s.
        for (var i = 0; i < 1500; i++)
        {
            guard.Emit(Gap(i));
            clock.Advance(TimeSpan.FromMilliseconds(100));
        }
        clock.SetUtcNow(At(180_500) - TimeSpan.FromTicks(1));
        Assert.Equal(2, Summaries().Count);
        clock.Advance(TimeSpan.FromTicks(1));

        Assert.Equal(5, Gaps().Count);
        int[] counts = [600, 600, 295];
        DateTimeOffset[] firsts = [At(500), At(60_500), At(120_500)];
        DateTimeOffset[] lasts = [At(60_400), At(120_400), At(149_900)];
        Assert.Equal(counts, Summaries().Select(s => Scalar<int>(s, "Count")));
        Assert.Equal(firsts, Summaries().Select(s => Scalar<DateTimeOffset>(s, "First")));
        Assert.Equal(lasts, Summaries().Select(s => Scalar<DateTimeOffset>(s, "Last")));

        // Still one burst a moment before a minute without a gap; after exactly a minute the next five are written
        // again, and the sixth counted.
        clock.SetUtcNow(At(209_900) - TimeSpan.FromTicks(1));
        guard.Emit(Gap(1999));
        Assert.Equal(5, Gaps().Count);
        clock.SetUtcNow(At(269_900) - TimeSpan.FromTicks(1));
        var next = Enumerable.Range(2000, 6).Select(Gap).ToList();
        foreach (var gap in next) guard.Emit(gap);
        Assert.Equal(next.Take(5), file.Events.TakeLast(5));
        Assert.Equal(10, Gaps().Count);
        // The gap held at 209.9 s came out a minute later; the sixth of the new burst comes out a minute after it.
        Assert.Equal(4, Summaries().Count);
        Assert.Equal(1, Scalar<int>(Summaries()[^1], "Count"));
        clock.Advance(Minute);
        Assert.Equal(5, Summaries().Count);
        Assert.Equal(1, Scalar<int>(Summaries()[^1], "Count"));
    }

    [Fact]
    public void AnIsolatedGapIsWrittenAndNothingIsCounted()
    {
        using var guard = NewGuard();
        var gaps = new List<LogEvent>();
        for (var i = 0; i < 10; i++)
        {
            gaps.Add(Gap(i));
            guard.Emit(gaps[^1]);
            clock.Advance(TimeSpan.FromMinutes(2));
        }

        Assert.Equal(gaps, file.Events);
        Assert.Empty(Summaries());
    }

    [Fact]
    public void EveryOtherEventPassesUntouchedInItsOrderDuringAFlood()
    {
        using var guard = NewGuard();
        var others = new[]
        {
            Event("[CAPTURE] !!! LOCKED !!! Port:13328", CaptureCategory),
            Event("[TCP] Duplicate/Old ignored. Seq:5 Exp:9", CaptureCategory, LogEventLevel.Information),
            Event("[tcp gap] Expected:1 Got:2 Diff:1", CaptureCategory),
            Event("Saw [TCP GAP] Expected:1 Got:2 Diff:1", CaptureCategory),
            Event("[TCP GAP] Expected:1 Got:2 Diff:1", "AionDpsMeter.Services.PacketCapture.CaptureDeviceX"),
            Event("[TCP GAP] Expected:1 Got:2 Diff:1", "ScarletMeter.Engine.Upstream.RecordTap", LogEventLevel.Error),
            Event("[TCP GAP] Expected:1 Got:2 Diff:1", category: null),
            new LogEvent(Start, LogEventLevel.Warning, null, Parser.Parse("[TCP GAP] Expected:1 Got:2 Diff:1"),
                new[] { new LogEventProperty("SourceContext", new ScalarValue(42)) }),
        };
        var expected = new List<LogEvent>();
        for (var i = 0; i < 20; i++)
        {
            var gap = Gap(i);
            guard.Emit(gap);
            if (i < 5) expected.Add(gap);
            if (i % 2 == 1 && i / 2 < others.Length)
            {
                guard.Emit(others[i / 2]);
                expected.Add(others[i / 2]);
            }
        }

        Assert.Equal(expected, file.Events);
        clock.Advance(Minute);
        Assert.Equal(15, Scalar<int>(Assert.Single(Summaries()), "Count"));
    }

    [Fact]
    public void ClosingTheLogWritesWhatIsCountedThenClosesTheFile()
    {
        var guard = NewGuard();
        for (var i = 0; i < 8; i++) guard.Emit(Gap(i));

        guard.Dispose();
        var summary = Assert.Single(Summaries());
        Assert.Equal(3, Scalar<int>(summary, "Count"));
        Assert.Equal(1, file.Disposals);
        Assert.Equal(file.Events.Count, file.EventsAtFirstDisposal); // the count went out before the file closed

        guard.Dispose();
        clock.Advance(TimeSpan.FromMinutes(5));
        guard.Emit(Gap(8)); // would start a new burst, and be written, were the log still open
        Assert.Equal(1, file.Disposals);
        Assert.Equal(6, file.Events.Count);
    }

    [Fact]
    public void ALateTimerDoesNotHoldTheCountBack()
    {
        // A timer that never fires, as on a starved thread pool: the flood's own next gap brings the count out.
        using var guard = new LogFloodGuard(file, MeterLog.TcpGaps, new ManualTimers(clock));
        for (var i = 0; i < 7; i++) guard.Emit(Gap(i));
        clock.Advance(Minute - TimeSpan.FromTicks(1));
        guard.Emit(Gap(7));
        Assert.Empty(Summaries());

        clock.Advance(TimeSpan.FromTicks(1));
        guard.Emit(Gap(8));
        Assert.Equal(4, Scalar<int>(Assert.Single(Summaries()), "Count"));

        // And a burst that ended in silence is counted out at the next gap, which is written.
        guard.Emit(Gap(9));
        clock.Advance(TimeSpan.FromMinutes(2));
        var after = Gap(10);
        guard.Emit(after);
        Assert.Equal(1, Scalar<int>(Summaries()[^1], "Count"));
        Assert.Same(after, file.Events[^1]);
    }

    [Fact]
    public void AnEarlyOrStrayTimerCallbackDoesNotWriteTheCountEarly()
    {
        // A thread-pool timer's callback can come after the timer was changed (one queued just before): it waits out
        // the rest of the minute, and does nothing once the count is out.
        var timers = new ManualTimers(clock);
        using var guard = new LogFloodGuard(file, MeterLog.TcpGaps, timers);
        for (var i = 0; i < 6; i++) guard.Emit(Gap(i));
        Assert.Equal(Minute, timers.Due);

        clock.Advance(TimeSpan.FromSeconds(20));
        timers.Fire();
        Assert.Empty(Summaries());
        Assert.Equal(TimeSpan.FromSeconds(40), timers.Due);

        clock.Advance(TimeSpan.FromSeconds(40));
        timers.Fire();
        Assert.Equal(1, Scalar<int>(Assert.Single(Summaries()), "Count"));
        Assert.Null(timers.Due);
        timers.Fire();
        Assert.Single(Summaries());
    }

    [Fact]
    public void AFileThatFailsOnTheTimersThreadDoesNotEndTheProcessOrRepeatTheCount()
    {
        // An exception escaping a thread-pool timer's callback ends the process; the guard keeps it in.
        var timers = new ManualTimers(clock);
        var failing = new FailingSink();
        using var guard = new LogFloodGuard(failing, MeterLog.TcpGaps, timers);
        for (var i = 0; i < 8; i++) guard.Emit(Gap(i));
        clock.Advance(TimeSpan.FromSeconds(59));
        guard.Emit(Gap(8));
        clock.Advance(TimeSpan.FromSeconds(1));
        failing.Failing = true;
        timers.Fire();

        failing.Failing = false;
        guard.Emit(Gap(9));
        clock.Advance(Minute);
        timers.Fire();
        // The four lost with the failed line are not added to the next.
        Assert.Equal(1, Scalar<int>(Assert.Single(failing.Events, e => e.Properties.ContainsKey("Count")), "Count"));
    }

    [Fact]
    public void AFileThatFailsWhenTheLogClosesDoesNotStopTheShutdown()
    {
        // The last count fails (a new day's file that cannot be created, a full disk): the line is lost, nothing
        // escapes the host's disposal, and the file is still closed.
        var failing = new FailingSink();
        var guard = new LogFloodGuard(failing, MeterLog.TcpGaps, clock);
        for (var i = 0; i < 8; i++) guard.Emit(Gap(i));
        failing.Failing = true;

        guard.Dispose();
        Assert.Equal(1, failing.Disposals);
        Assert.DoesNotContain(failing.Events, e => e.Properties.ContainsKey("Count"));
    }

    [Fact]
    public void AStepInTheSystemsClockNeitherHoldsACountBackNorEndsABurst()
    {
        var timers = new ManualTimers(clock);
        using var guard = new LogFloodGuard(file, MeterLog.TcpGaps, timers);
        for (var i = 0; i < 6; i++) guard.Emit(Gap(i));

        // The wall clock jumps two minutes ahead: a burst is still one minute of quiet away, so the next gap counts.
        timers.WallShift = TimeSpan.FromMinutes(2);
        guard.Emit(Gap(6));
        Assert.Equal(5, Gaps().Count);

        // Then an hour back: the count still comes out a minute after it began.
        timers.WallShift = TimeSpan.FromHours(-1);
        clock.Advance(Minute);
        timers.Fire();
        Assert.Equal(2, Scalar<int>(Assert.Single(Summaries()), "Count"));
    }

    [Fact]
    public void GapsFromManyThreadsAreEachWrittenOrCountedOnce()
    {
        // The fake clock runs due timers whenever it is read or moved, on whichever thread does it, so the timer's
        // callback also comes in on a writing thread that holds the guard's lock. A count written twice that way showed
        // in about three runs of four of one round (LESSONS), so the test runs several.
        const int Rounds = 8, Threads = 4, PerThread = 5000;
        for (var round = 0; round < Rounds; round++)
        {
            var sink = new RecordingSink();
            var guard = new LogFloodGuard(sink, MeterLog.TcpGaps, clock);
            using var done = new CountdownEvent(Threads);
            var writers = Enumerable.Range(0, Threads).Select(t => new Thread(() =>
            {
                for (var i = 0; i < PerThread; i++) guard.Emit(Gap(t * PerThread + i));
                done.Signal();
            })).ToList();
            writers.ForEach(w => w.Start());
            // The clock moves, and the timer fires, while the threads write. How far it moves between two gaps is up
            // to the scheduler (a minute's quiet starts a new burst), so the test checks that nothing is lost or doubled.
            while (!done.IsSet)
            {
                clock.Advance(TimeSpan.FromSeconds(1));
                Thread.Yield();
            }
            writers.ForEach(w => w.Join());
            guard.Dispose();

            var written = sink.Events.Count(e => e.MessageTemplate.Text.StartsWith("[TCP GAP] Expected:", StringComparison.Ordinal));
            var counted = sink.Events.Where(e => e.Properties.ContainsKey("Count")).Sum(s => Scalar<int>(s, "Count"));
            Assert.InRange(written, 5, Threads * PerThread);
            Assert.Equal(Threads * PerThread, written + counted);
        }
    }

    [Fact]
    public void TheMetersRuleIsUpstreamsGapWarningFiveAndAMinute()
    {
        Assert.Equal(CaptureCategory, MeterLog.TcpGaps.Category);
        Assert.Equal("[TCP GAP]", MeterLog.TcpGaps.Prefix);
        Assert.Equal(5, MeterLog.TcpGaps.Kept);
        Assert.Equal(Minute, MeterLog.TcpGaps.Interval);
    }

    [Fact]
    public void ARuleNeedsACategoryAPrefixAndAnInterval()
    {
        Assert.ThrowsAny<ArgumentException>(() => new LogFloodRule(" ", "[X]", 1, Minute));
        Assert.ThrowsAny<ArgumentException>(() => new LogFloodRule("A.B", "", 1, Minute));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LogFloodRule("A.B", "[X]", -1, Minute));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LogFloodRule("A.B", "[X]", 1, TimeSpan.Zero));
        // A prefix with braces is kept as text in the summary, not read as a property.
        var sink = new RecordingSink();
        using var guard = new LogFloodGuard(sink, new LogFloodRule("A.B", "{X}", 0, Minute), clock);
        guard.Emit(Event("{X} one", "A.B"));
        guard.Dispose();
        Assert.StartsWith("{X} 1 more not written", Assert.Single(sink.Events).RenderMessage(CultureInfo.InvariantCulture));
    }

    private LogFloodGuard NewGuard() => new(file, MeterLog.TcpGaps, clock);

    // A time so many milliseconds after the start, exactly (no floating-point seconds).
    private static DateTimeOffset At(long milliseconds) => Start + TimeSpan.FromMilliseconds(milliseconds);

    // Upstream's warning, as its capture device writes it: the numbers interpolated into the text.
    private LogEvent Gap(int n) => Event($"[TCP GAP] Expected:{1000 + n} Got:{2000 + n} Diff:1000", CaptureCategory, at: clock.GetUtcNow());

    private static LogEvent Event(string text, string? category, LogEventLevel level = LogEventLevel.Warning, DateTimeOffset? at = null) =>
        new(at ?? Start, level, null, Parser.Parse(text),
            category is null ? Array.Empty<LogEventProperty>() : new[] { new LogEventProperty("SourceContext", new ScalarValue(category)) });

    private List<LogEvent> Gaps() => [.. file.Events.Where(e => e.MessageTemplate.Text.StartsWith("[TCP GAP] Expected:", StringComparison.Ordinal))];

    private List<LogEvent> Summaries() => [.. file.Events.Where(e => e.Properties.ContainsKey("Count"))];

    private static string? Category(LogEvent e) => (e.Properties.GetValueOrDefault("SourceContext") as ScalarValue)?.Value as string;

    private static T Scalar<T>(LogEvent e, string name) => (T)((ScalarValue)e.Properties[name]).Value!;

    /// <summary>The file behind the guard: keeps what it is given, and when it was first closed. Thread-safe.</summary>
    private sealed class RecordingSink : ILogEventSink, IDisposable
    {
        private readonly List<LogEvent> events = [];

        public IReadOnlyList<LogEvent> Events
        {
            get
            {
                lock (events) return [.. events];
            }
        }

        public int Disposals { get; private set; }

        public int EventsAtFirstDisposal { get; private set; } = -1;

        public void Emit(LogEvent logEvent)
        {
            lock (events) events.Add(logEvent);
        }

        public void Dispose()
        {
            lock (events)
            {
                if (Disposals++ == 0) EventsAtFirstDisposal = events.Count;
            }
        }
    }

    /// <summary>A file that throws while <see cref="Failing"/> is set, and keeps what it is given otherwise.</summary>
    private sealed class FailingSink : ILogEventSink, IDisposable
    {
        public bool Failing { get; set; }

        public List<LogEvent> Events { get; } = [];

        public int Disposals { get; private set; }

        public void Emit(LogEvent logEvent)
        {
            if (Failing) throw new IOException("The disk is full (synthetic).");
            Events.Add(logEvent);
        }

        public void Dispose() => Disposals++;
    }

    /// <summary>
    /// The fake clock's time, with one timer that fires only when the test calls <see cref="Fire"/>: never, as on a
    /// starved thread pool, or early, as a callback queued before the timer was changed. <see cref="Due"/> is the
    /// last due time set, null when the timer was stopped. <see cref="WallShift"/> moves the wall clock alone, as a
    /// change to the system's time does; the timestamps follow the fake clock.
    /// </summary>
    private sealed class ManualTimers(FakeTimeProvider clock) : TimeProvider
    {
        private TimerCallback? callback;

        public TimeSpan? Due { get; private set; }

        public TimeSpan WallShift { get; set; }

        public override DateTimeOffset GetUtcNow() => clock.GetUtcNow() + WallShift;

        public override long GetTimestamp() => clock.GetTimestamp();

        public override long TimestampFrequency => clock.TimestampFrequency;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            this.callback = callback;
            var timer = new Timer(this);
            timer.Change(dueTime, period);
            return timer;
        }

        public void Fire() => callback!(null);

        private sealed class Timer(ManualTimers owner) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                owner.Due = dueTime == Timeout.InfiniteTimeSpan ? null : dueTime;
                return true;
            }

            public void Dispose() => owner.Due = null;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
