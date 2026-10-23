using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ScarletMeter.Tests;

/// <summary>
/// Collects warning and error entries (the message, then the exception's message) from every logger, for tests
/// that check what the engine logged. Add it to a TestEngineHost's logging.
/// </summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<(LogLevel Level, string Text)> entries = new();

    public IReadOnlyList<string> Errors => [.. entries.Where(e => e.Level >= LogLevel.Error).Select(e => e.Text)];

    public IReadOnlyList<string> Warnings => [.. entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Text)];

    public ILogger CreateLogger(string categoryName) => new Logger(entries);

    public void Dispose()
    {
    }

    private sealed class Logger(ConcurrentQueue<(LogLevel Level, string Text)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) entries.Enqueue((logLevel, $"{formatter(state, exception)} {exception?.Message}"));
        }
    }
}
