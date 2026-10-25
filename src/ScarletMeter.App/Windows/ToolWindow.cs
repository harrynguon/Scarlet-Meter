using System.Windows;
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
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
    }

    public ToolWindowSpec Spec { get; }

    private void PlaceBeside(Window owner)
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        var at = ScreenPlacement.Beside(owner, Width, Height);
        Left = at.X;
        Top = at.Y;
    }
}
