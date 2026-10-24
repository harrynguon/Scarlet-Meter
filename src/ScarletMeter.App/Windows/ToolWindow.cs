using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ScarletMeter.App.Windows;

/// <summary>What a tool window shows and how big it starts.</summary>
public sealed record ToolWindowSpec(string Key, Type RootComponent, string Title, double Width, double Height, double MinWidth = 360, double MinHeight = 260);

/// <summary>
/// A secondary window (settings, history, player details) with the standard Windows frame in dark mode.
/// It is owned by the overlay, so it stays above it and minimizes and restores with it, and it opens beside
/// the overlay unless a saved placement exists.
/// </summary>
public sealed class ToolWindow : ScarletWindow
{
    private const double Gap = 8;

    public ToolWindow(IServiceProvider services, ToolWindowSpec spec, Window? owner)
        : base(services, spec.RootComponent, spec.Key)
    {
        Spec = spec;
        Title = spec.Title;
        Width = spec.Width;
        Height = spec.Height;
        MinWidth = spec.MinWidth;
        MinHeight = spec.MinHeight;
        Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0F));
        Topmost = true;
        if (owner is { IsLoaded: true })
        {
            Owner = owner;
            PlaceBeside(owner);
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        RestorePlacement();
        SourceInitialized += (_, _) => UseDarkTitleBar();
    }

    public ToolWindowSpec Spec { get; }

    // Right of the owner when it fits on the owner's monitor, otherwise left of it, clamped to that monitor's
    // work area (the primary monitor's when the owner's cannot be determined).
    private void PlaceBeside(Window owner)
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        var area = ScreenPlacement.WorkAreaOf(owner);
        var left = owner.Left + owner.ActualWidth + Gap;
        if (left + Width > area.Right) left = owner.Left - Width - Gap;
        Left = Math.Clamp(left, area.Left, Math.Max(area.Left, area.Right - Width));
        Top = Math.Clamp(owner.Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
    }

    private void UseDarkTitleBar()
    {
        var enabled = 1;
        _ = NativeMethods.DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, NativeMethods.DwmUseImmersiveDarkMode, ref enabled, sizeof(int));
    }
}
