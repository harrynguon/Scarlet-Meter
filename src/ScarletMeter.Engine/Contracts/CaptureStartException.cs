namespace ScarletMeter.Engine;

/// <summary>
/// Capture could not start: Npcap is not installed in WinPcap API-compatible mode, or upstream found no
/// eligible network adapter. An adapter that is found but fails to open does not raise this; upstream logs
/// it and listens on the others. The inner exception is upstream's or SharpPcap's original error.
/// </summary>
public sealed class CaptureStartException(string message, Exception? innerException = null)
    : Exception(message, innerException);
