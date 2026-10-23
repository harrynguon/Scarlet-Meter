using AionDpsMeter.Services.PacketCapture;

namespace ScarletMeter.Engine.Upstream;

/// <summary>
/// A capture device that captures nothing, for <see cref="CaptureMode.None"/>: UI work, tests and machines
/// without Npcap. It never reports itself as capturing, so the UI can say that capture is off.
/// </summary>
internal sealed class NoCaptureDevice : IPacketCaptureDevice
{
    public bool IsCapturing => false;

    public string? DeviceName => null;

    public void DiscoverAdapters()
    {
    }

    public void StartCapture()
    {
    }

    public void StopCapture()
    {
    }

    public void Dispose()
    {
    }
}
