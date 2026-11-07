namespace ScarletMeter.Engine;

/// <summary>
/// Upstream log events that the host's log treats specially, named as plain strings so the app never references
/// an upstream type. The engine owns them because it is the project that knows upstream; tests tie each one to
/// upstream's type and source (MeterLogTests), so a moved pin that changes them fails there.
/// </summary>
public static class UpstreamLogs
{
    /// <summary>
    /// The logger category (Serilog's <c>SourceContext</c>) of upstream's capture device: its
    /// <c>ILogger&lt;CaptureDevice&gt;</c>, whose type this is.
    /// </summary>
    public const string CaptureCategory = "AionDpsMeter.Services.PacketCapture.CaptureDevice";

    /// <summary>
    /// How the capture device's warning for a TCP sequence gap begins. Upstream logs one per gap, with the numbers
    /// interpolated into the text ("[TCP GAP] Expected:… Got:… Diff:…"), so the text is also its message template.
    /// </summary>
    public const string TcpGapPrefix = "[TCP GAP]";
}
