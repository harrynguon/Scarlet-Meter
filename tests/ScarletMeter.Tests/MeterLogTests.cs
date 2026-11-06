using System.IO;
using AionDpsMeter.Services.PacketCapture;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScarletMeter.App.Logging;
using ScarletMeter.Engine;
using Serilog;

namespace ScarletMeter.Tests;

/// <summary>
/// The meter's log as App builds it (<see cref="MeterLog"/> in a host with Serilog), written through upstream's own
/// <c>ILogger&lt;CaptureDevice&gt;</c> into a test data directory and read back from the file; and the upstream facts
/// the gap rule relies on (<see cref="UpstreamLogs"/>), pinned to upstream's type and source, so a moved pin that
/// changes them fails here.
/// </summary>
public sealed class MeterLogTests : IDisposable
{
    private readonly TestDataDirectory directory = new();

    public void Dispose() => directory.Dispose();

    [Fact]
    public void UpstreamsGapWarningsAreLimitedInTheFileAndCountedWhenTheLogCloses()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
        builder.Services.AddSerilog(configuration => MeterLog.Configure(configuration, directory.FullPath, TimeProvider.System));
        using (var host = builder.Build())
        {
            var capture = host.Services.GetRequiredService<ILogger<CaptureDevice>>();
            var other = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ScarletMeter.Engine.Upstream.RecordTap");
            for (var i = 0; i < 50; i++)
            {
                uint expectedSeq = 1000u + (uint)i, currentSeq = expectedSeq + 1460;
                // Upstream's own form (CaptureDevice, "[TCP GAP]"): the numbers interpolated into the text.
                capture.LogWarning(
                    $"[TCP GAP] Expected:{expectedSeq} Got:{currentSeq} " +
                    $"Diff:{currentSeq - expectedSeq}");
                if (i != 20) continue;
                capture.LogWarning("Capture was locked onto port {Port} without a server-time record.", 443);
                other.LogWarning("[TCP GAP] from another category");
                // The levels kept from App's earlier configuration: Information up, EF Core's SQL lines left out.
                other.LogInformation("Synthetic information line");
                other.LogDebug("Synthetic debug line");
                host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Microsoft.EntityFrameworkCore.Database.Command")
                    .LogInformation("Executed DbCommand (synthetic)");
            }
        } // Disposing the host closes the log, which writes the count still held.

        var lines = Directory.GetFiles(Path.Combine(directory.FullPath, "logs"), "scarlet-*.log")
            .Order(StringComparer.Ordinal)
            .SelectMany(File.ReadAllLines)
            .ToList();
        string[] written = ["Expected:1000 Got:2460", "Expected:1001 Got:2461", "Expected:1002 Got:2462", "Expected:1003 Got:2463", "Expected:1004 Got:2464"];
        Assert.Equal(written, lines.Where(l => l.Contains("[TCP GAP] Expected:", StringComparison.Ordinal))
            .Select(l => l[(l.IndexOf("Expected:", StringComparison.Ordinal))..].Replace(" Diff:1460", "", StringComparison.Ordinal)));
        Assert.Contains(lines, l => l.Contains("[WRN] Capture was locked onto port 443 without", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("[WRN] [TCP GAP] from another category", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("[INF] Synthetic information line", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("Synthetic debug line", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("Executed DbCommand", StringComparison.Ordinal));
        var summary = Assert.Single(lines, l => l.Contains(" more not written", StringComparison.Ordinal));
        Assert.Matches(
            @"\[WRN\] \[TCP GAP\] 45 more not written, from \d\d:\d\d:\d\d\.\d{3} to \d\d:\d\d:\d\d\.\d{3} " +
            @"\(a burst's first 5 are written, then a count every 60 s\)$",
            summary);
        Assert.Equal(summary, lines[^1]);
    }

    [Fact]
    public void TheLogNeedsAnAbsoluteDataDirectory() =>
        Assert.Throws<ArgumentException>(() => MeterLog.Configure(new LoggerConfiguration(), "data", TimeProvider.System));

    [Fact]
    public void TheCaptureCategoryIsUpstreamsCaptureDevice() =>
        Assert.Equal(UpstreamLogs.CaptureCategory, typeof(CaptureDevice).FullName);

    [Fact]
    public void UpstreamStillWarnsOfEachTcpGapThroughItsOwnLoggerWithThatPrefix()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "upstream", "AionDpsMeter.Services", "PacketCapture", "CaptureDevice.cs"));

        Assert.Contains("ILogger<CaptureDevice> logger", source, StringComparison.Ordinal);
        Assert.Matches(@"logger\.LogWarning\(\s*\$""\[TCP GAP\] Expected:", source);
        Assert.Equal("[TCP GAP]", UpstreamLogs.TcpGapPrefix);
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ScarletMeter.slnx"))) return dir.FullName;
        throw new InvalidOperationException("ScarletMeter.slnx was not found above " + AppContext.BaseDirectory);
    }
}
