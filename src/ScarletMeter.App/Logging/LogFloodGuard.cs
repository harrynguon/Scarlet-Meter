using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Parsing;

namespace ScarletMeter.App.Logging;

/// <summary>
/// A Serilog sink in front of another (the log file) that keeps one kind of event, a <see cref="LogFloodRule"/>, from
/// flooding it. Upstream logs a warning per TCP sequence gap, and one session wrote about two million of them in five
/// minutes. Here each burst's first <see cref="LogFloodRule.Kept"/> events are written as they come; the rest are
/// counted, and a summary line (how many, and the first and last one's time) is written an interval after the first
/// counted one: so one a minute at most while a flood lasts, the last within a minute of its end, and whatever is
/// still counted when the log closes. A burst ends once an interval passes without such an event, so the next one is
/// written again. Every other event passes straight through, uncounted and in its own order.
/// </summary>
/// <remarks>
/// Thread-safe: Serilog emits from any thread, and the summary timer fires on the thread pool. The rule's own events
/// and the summaries are written under one lock, so their order in the file is the order they were decided; other
/// events never take it. The lock admits its own thread again, and a clock may run due timers when it is read, so
/// every entry reads the clock first and finishes changing the state before it calls the timer or the file.
/// </remarks>
internal sealed class LogFloodGuard : ILogEventSink, IDisposable
{
    private readonly ILogEventSink inner;
    private readonly LogFloodRule rule;
    private readonly TimeProvider time;
    private readonly MessageTemplate summaryTemplate;
    private readonly ITimer timer;
    private readonly Lock gate = new();

    private int keptInBurst;
    private DateTimeOffset? lastSeen;        // clock time of the burst's latest event, written or counted
    private int counted;                     // events counted, not written, since the last summary
    private DateTimeOffset countingSince;    // clock time of the first of them
    private DateTimeOffset firstCounted;     // the first and latest one's own timestamps, for the summary
    private DateTimeOffset lastCounted;
    private LogEventLevel countedLevel;
    private LogEventPropertyValue? countedCategory;
    private bool disposed;

    public LogFloodGuard(ILogEventSink inner, LogFloodRule rule, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(time);
        this.inner = inner;
        this.rule = rule;
        this.time = time;
        // The summary begins with the rule's prefix, so a search for the events also finds their counts.
        var prefix = rule.Prefix.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal);
        summaryTemplate = new MessageTemplateParser().Parse(
            prefix + " {Count} more not written, from {First:HH:mm:ss.fff} to {Last:HH:mm:ss.fff} " +
            "(a burst's first {Kept} are written, then a count every {Seconds} s)");
        timer = time.CreateTimer(_ => OnTimer(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Writes the event, or counts it when it is the rule's and its burst has had its written ones.</summary>
    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        if (!rule.Matches(logEvent))
        {
            inner.Emit(logEvent);
            return;
        }

        lock (gate)
        {
            // The clock is read first, before any state: a clock may run due timers when read (a test's fake one
            // does), and the lock lets the callback in on this thread, so it must find the state whole.
            var now = time.GetUtcNow();
            if (disposed) return;
            if (lastSeen is { } last && now - last >= rule.Interval)
            {
                // A quiet interval ended the last burst; its count is normally out already (the timer's).
                WriteSummary(now);
                keptInBurst = 0;
            }
            lastSeen = now;

            if (keptInBurst < rule.Kept)
            {
                keptInBurst++;
                inner.Emit(logEvent);
                return;
            }

            var starting = counted == 0;
            if (starting)
            {
                countingSince = now;
                firstCounted = logEvent.Timestamp;
                countedLevel = logEvent.Level;
                countedCategory = logEvent.Properties.GetValueOrDefault("SourceContext");
            }
            counted++;
            lastCounted = logEvent.Timestamp;
            // The timer is late (a starved thread pool): the flood itself brings the summary out on time.
            if (now - countingSince >= rule.Interval) WriteSummary(now);
            else if (starting) timer.Change(rule.Interval, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Writes what is still counted, then closes the sink behind it. Safe to call more than once.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            var now = time.GetUtcNow(); // first, as in Emit
            if (disposed) return;
            disposed = true;
            try
            {
                WriteSummary(now);
            }
            finally
            {
                timer.Dispose();
                (inner as IDisposable)?.Dispose();
            }
        }
    }

    // The timer's callback, an interval after a summary period began. It must never throw (on the thread pool that
    // would end the process), so a failure goes to Serilog's own diagnostic log, as Serilog's sinks report theirs.
    private void OnTimer()
    {
        try
        {
            lock (gate)
            {
                var now = time.GetUtcNow(); // first, as in Emit
                if (disposed || counted == 0) return;
                var left = countingSince + rule.Interval - now;
                if (left > TimeSpan.Zero) timer.Change(left, Timeout.InfiniteTimeSpan); // early: wait out the rest
                else WriteSummary(now);
            }
        }
        catch (Exception ex)
        {
            SelfLog.WriteLine("The log flood guard could not write its summary: {0}", ex);
        }
    }

    // Writes one line, stamped now, for the events counted since the last one, if any. Called under the lock, with the
    // time its caller read: it reads no clock itself, and clears the count before calling out (the timer, the sink), so
    // nothing it calls can find the count still there and write it again. A sink that throws loses that line rather
    // than repeating it with every event after.
    private void WriteSummary(DateTimeOffset now)
    {
        if (counted == 0) return;
        List<LogEventProperty> properties =
        [
            new("Count", new ScalarValue(counted)),
            new("First", new ScalarValue(firstCounted)),
            new("Last", new ScalarValue(lastCounted)),
            new("Kept", new ScalarValue(rule.Kept)),
            new("Seconds", new ScalarValue(rule.Interval.TotalSeconds)),
        ];
        if (countedCategory is not null) properties.Add(new("SourceContext", countedCategory));
        var summary = new LogEvent(TimeZoneInfo.ConvertTime(now, time.LocalTimeZone), countedLevel, null, summaryTemplate, properties);
        counted = 0;
        timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        inner.Emit(summary);
    }
}
