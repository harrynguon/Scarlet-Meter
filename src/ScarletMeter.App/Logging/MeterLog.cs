using System.IO;
using ScarletMeter.Engine;
using Serilog;
using Serilog.Configuration;
using Serilog.Events;

namespace ScarletMeter.App.Logging;

/// <summary>
/// The meter's log: one Serilog file a day, <c>logs/scarlet-&lt;date&gt;.log</c> under the data directory, kept 14 days,
/// from Information up, with EF Core's SQL lines left out and upstream's warning per TCP sequence gap limited by a
/// <see cref="LogFloodGuard"/>. App configures its host with it, and the tests build the same log.
/// </summary>
internal static class MeterLog
{
    /// <summary>
    /// Upstream's capture device warns once per TCP sequence gap, up to thousands a second: a burst's first five are
    /// written, then a count a minute while it lasts.
    /// </summary>
    public static LogFloodRule TcpGaps { get; } =
        new(UpstreamLogs.CaptureCategory, UpstreamLogs.TcpGapPrefix, kept: 5, interval: TimeSpan.FromMinutes(1));

    /// <summary>
    /// Sets up <paramref name="configuration"/> as the meter's log under <paramref name="dataDirectory"/> (absolute,
    /// from <see cref="EngineOptions"/>; never the working directory), with <paramref name="time"/> pacing the
    /// gap summaries.
    /// </summary>
    public static LoggerConfiguration Configure(LoggerConfiguration configuration, string dataDirectory, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(time);
        if (!Path.IsPathFullyQualified(dataDirectory))
            throw new ArgumentException("The log's data directory must be an absolute path.", nameof(dataDirectory));

        // The guard wraps the file sink, and disposes it when the log closes, after writing what it still counts.
        var file = LoggerSinkConfiguration.Wrap(
            inner => new LogFloodGuard(inner, TcpGaps, time),
            writeTo => writeTo.File(
                Path.Combine(dataDirectory, "logs", "scarlet-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14));
        return configuration
            .MinimumLevel.Information()
            // EF Core logs every SQL command at Information, which buries the capture lines.
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .WriteTo.Sink(file);
    }
}
