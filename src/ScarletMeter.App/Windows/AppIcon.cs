using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScarletMeter.App.Windows;

/// <summary>
/// The meter's icon (Assets/ScarletMeter.ico, also the exe's own), for every window's title bar and taskbar button.
/// The whole multi-size icon, so Windows picks the size it draws (16 to 256 px). Null, and the window keeps the
/// default, if the resource cannot be read: an icon must never stop a window from opening.
/// </summary>
internal static class AppIcon
{
    private static readonly Lazy<ImageSource?> Icon = new(Load);

    public static ImageSource? Source => Icon.Value;

    private static ImageSource? Load()
    {
        try
        {
            var frame = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/ScarletMeter.ico", UriKind.Absolute));
            frame.Freeze();
            return frame;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or UriFormatException or FileFormatException)
        {
            return null;
        }
    }
}
