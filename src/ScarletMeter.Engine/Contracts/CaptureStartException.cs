namespace ScarletMeter.Engine;

/// <summary>
/// Capture could not start: Npcap is not installed in WinPcap API-compatible mode, or no network adapter
/// could be opened. The inner exception is upstream's or SharpPcap's original error.
/// </summary>
public sealed class CaptureStartException(string message, Exception? innerException = null)
    : Exception(message, innerException);
