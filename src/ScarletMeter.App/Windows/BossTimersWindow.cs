using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using ScarletMeter.App.BossTimers;
using ScarletMeter.App.Settings;
using ScarletMeter.Engine;

namespace ScarletMeter.App.Windows;

/// <summary>
/// A small window showing aion2timers.com's world-boss timers for the player to read, opened from the overlay's
/// header; the meter never reads the page. The page is third-party content, so the window is kept apart from the
/// meter: a plain WebView2 (no Blazor, no host objects or web messages, no access to the meter's local hosts) in its
/// own browser profile, <c>webview2-timers</c> in the data directory, where the site keeps its own choices such as the
/// server and faction. Every top-level navigation and new-window request goes through <see cref="BossTimersSite"/>:
/// the site's pages show here, a public web page elsewhere that the page opens during the player's click goes to the
/// default browser (at most one per <see cref="BrowserLaunchGate.Interval"/>), anything else is refused. Downloads,
/// saving, permission prompts, screen capture, credentials, other programs' protocols, dropped files, the context
/// menu and the browser's own shortcuts are off. Not owned by the overlay and not on top unless the player pins it
/// ("Keep on top", saved), so it can sit behind the game; its placement is remembered. Nothing it does can end the
/// meter: its start and every button are guarded.
/// </summary>
public sealed class BossTimersWindow : Window
{
    /// <summary>The window's browser profile folder in the data directory, apart from the meter's own (<c>webview2</c>).</summary>
    public const string ProfileFolder = "webview2-timers";

    private static readonly System.Drawing.Color PageBackground = System.Drawing.Color.FromArgb(0x0B, 0x0B, 0x0F);

    private readonly SettingsService settings;
    private readonly ILogger logger;
    private readonly string profile;
    private readonly BrowserLaunchGate launches;
    private readonly WebView2 webView = new() { DefaultBackgroundColor = PageBackground, AllowExternalDrop = false };
    private readonly TextBlock status;
    private readonly ToggleButton keepOnTop;
    private readonly ButtonBase[] pageButtons;
    private readonly WindowPlacementTracker placement;
    private bool closed;
    private bool browserGone;

    /// <summary>
    /// Builds the window (it starts its browser once loaded). <paramref name="beside"/>, the overlay, only places it
    /// when no placement is saved; the window is not owned by it.
    /// </summary>
    public BossTimersWindow(IServiceProvider services, Window? beside)
    {
        settings = services.GetRequiredService<SettingsService>();
        logger = services.GetRequiredService<ILoggerFactory>().CreateLogger<BossTimersWindow>();
        launches = new BrowserLaunchGate(services.GetRequiredService<TimeProvider>());
        // DataDirectory is absolute (EngineOptions); WebView2 creates the folder.
        profile = Path.Combine(services.GetRequiredService<EngineOptions>().DataDirectory, ProfileFolder);

        Title = "Scarlet Meter · Boss timers";
        Width = 460;
        Height = 640;
        MinWidth = 320;
        MinHeight = 240;
        Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0F));
        Topmost = settings.Current.BossTimers.KeepOnTop;

        status = new TextBlock
        {
            Text = BossTimersSite.Start.Host,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8D, 0x8A, 0x9D)),
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(8, 0, 8, 0),
        };
        keepOnTop = Tool(new ToggleButton { IsChecked = Topmost }, "", "Keep on top of other windows, the game included");
        keepOnTop.Click += (_, _) => KeepOnTop(keepOnTop.IsChecked == true);
        var back = Tool(new Button(), "", "Back to the world-boss timers", (_, _) => OnPage(core => core.Navigate(BossTimersSite.Start.AbsoluteUri)));
        var reload = Tool(new Button(), "", "Reload", (_, _) => OnPage(core => core.Reload()));
        var openOutside = Tool(new Button(), "", "Open this page in your browser", (_, _) => OnPage(core => OpenInBrowser(CurrentSitePage(core))));
        pageButtons = [back, reload, openOutside];
        Content = Layout([back, reload], [keepOnTop, openOutside]);

        if (beside is { IsLoaded: true })
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            var at = ScreenPlacement.Beside(beside, Width, Height);
            Left = at.X;
            Top = at.Y;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        placement = new WindowPlacementTracker(this, WindowKeys.BossTimers, settings, () => new Rect(Left, Top, ActualWidth, ActualHeight));
        placement.Restore();

        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        Loaded += async (_, _) => await StartAsync();
        Closed += (_, _) =>
        {
            closed = true;
            webView.Dispose();
        };
    }

    // The browser starts in the window's own profile, is locked down, then opens the site. Nothing is navigated before
    // the lock is in place, and the control's Source is never set, which would start it in a default profile. Any
    // failure is logged and shown in the bar rather than thrown out of the Loaded handler, which would end the meter;
    // a window closed while its browser starts simply stops.
    private async Task StartAsync()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: profile);
            if (closed) return;
            await webView.EnsureCoreWebView2Async(environment);
            if (closed) return;
            var core = webView.CoreWebView2;
            Lock(core);
            core.Navigate(BossTimersSite.Start.AbsoluteUri);
        }
        catch (Exception) when (closed)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The boss-timers window could not start its browser view.");
            status.Text = "The browser view could not start (WebView2).";
            foreach (var button in pageButtons) button.IsEnabled = false;
        }
    }

    // Nothing the page could use to reach the meter, this PC or the player's details: no bridge to .NET, no developer
    // tools, no saved passwords or form data, no context menu (Save as, Print) or browser shortcuts (Ctrl+S, Ctrl+P;
    // the bar has Reload); downloads, saving, permissions, screen capture, credentials and other programs' protocols
    // refused. SmartScreen stays on.
    private void Lock(CoreWebView2 core)
    {
        var s = core.Settings;
        s.AreHostObjectsAllowed = false;
        s.IsWebMessageEnabled = false;
        s.AreDevToolsEnabled = false;
        s.IsPasswordAutosaveEnabled = false;
        s.IsGeneralAutofillEnabled = false;
        s.AreDefaultContextMenusEnabled = false;
        s.AreBrowserAcceleratorKeysEnabled = false;
        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;
        core.NavigationCompleted += OnNavigationCompleted;
        core.DownloadStarting += (_, e) => e.Cancel = true;
        core.SaveAsUIShowing += (_, e) => e.Cancel = true;
        core.SaveFileSecurityCheckStarting += (_, e) => e.CancelSave = true;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.ScreenCaptureStarting += (_, e) => e.Cancel = true;
        core.BasicAuthenticationRequested += (_, e) => e.Cancel = true;
        core.ClientCertificateRequested += (_, e) => e.Cancel = true;
        core.LaunchingExternalUriScheme += (_, e) => e.Cancel = true;
        core.ProcessFailed += OnProcessFailed;
    }

    // Every top-level navigation, redirects included (BossTimersSite.DecideNavigation).
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        var decision = BossTimersSite.DecideNavigation(e.Uri, e.IsUserInitiated, e.IsRedirected);
        if (decision == SiteNavigation.Allow) return;
        e.Cancel = true;
        if (decision == SiteNavigation.OpenInBrowser) OpenInBrowser(e.Uri);
    }

    // Never a second browser window (BossTimersSite.DecideNewWindow): a site page opens here, a page elsewhere that the
    // site's own page opened during a click goes to the default browser, anything else nowhere.
    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        switch (BossTimersSite.DecideNewWindow(e.Uri, e.IsUserInitiated, e.OriginalSourceFrameInfo?.Source))
        {
            case SiteNavigation.Allow:
                OnPage(core => core.Navigate(e.Uri));
                break;
            case SiteNavigation.OpenInBrowser:
                OpenInBrowser(e.Uri);
                break;
        }
    }

    // A page that loads after a failure clears the failure's note.
    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess && !browserGone) status.Text = BossTimersSite.Start.Host;
    }

    // What the player sees when one of the page's processes fails. The whole browser gone: the buttons can do nothing
    // more (the control throws once its browser has crashed), and reopening the window starts a new one. The page's
    // own process: Reload brings it back. A frame's, the GPU's or a utility process's failure the browser recovers
    // from by itself, so it is only logged.
    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        logger.LogWarning("A boss-timers page process failed: {Kind}.", e.ProcessFailedKind);
        switch (e.ProcessFailedKind)
        {
            case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                browserGone = true;
                foreach (var button in pageButtons) button.IsEnabled = false;
                status.Text = "The page stopped. Close this window and open it again.";
                break;
            case CoreWebView2ProcessFailedKind.RenderProcessExited:
            case CoreWebView2ProcessFailedKind.RenderProcessUnresponsive:
                status.Text = "The page stopped. Reload it.";
                break;
        }
    }

    // Acts on the page while its browser is there. After the browser process has gone the control's CoreWebView2
    // throws, and an exception out of a button's handler would end the meter.
    private void OnPage(Action<CoreWebView2> act)
    {
        if (browserGone || closed) return;
        try
        {
            if (webView.CoreWebView2 is { } core) act(core);
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or ObjectDisposedException)
        {
            logger.LogInformation(ex, "The boss-timers page was gone; the action did nothing.");
        }
    }

    // The page shown, if it is one of the site's, else the timers: what "Open in browser" hands over.
    private static string CurrentSitePage(CoreWebView2 core) =>
        Uri.TryCreate(core.Source, UriKind.Absolute, out var shown) && BossTimersSite.IsSitePage(shown)
            ? shown.AbsoluteUri
            : BossTimersSite.Start.AbsoluteUri;

    // Only a public http or https page without credentials (BossTimersSite.IsWebPage), at most one per
    // BrowserLaunchGate.Interval, and outside WebView2's own callback: the shell is never handed anything else.
    private void OpenInBrowser(string? address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || !BossTimersSite.IsWebPage(uri) || !launches.TryPass()) return;
        _ = Dispatcher.InvokeAsync(() => DefaultBrowser.Open(uri, logger));
    }

    // "Keep on top": the window above every other, the game included, and the choice saved for next time.
    private void KeepOnTop(bool on)
    {
        Topmost = on;
        settings.Update(s => s with { BossTimers = s.BossTimers with { KeepOnTop = on } });
    }

    // A slim bar above the page: navigation on the left, the site's name (or why the page stopped), the window's own
    // switches on the right.
    private DockPanel Layout(IEnumerable<UIElement> left, IEnumerable<UIElement> right)
    {
        var bar = new DockPanel { Height = 34, LastChildFill = true };
        var leftPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        foreach (var item in left) leftPanel.Children.Add(item);
        var rightPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        foreach (var item in right) rightPanel.Children.Add(item);
        DockPanel.SetDock(leftPanel, Dock.Left);
        DockPanel.SetDock(rightPanel, Dock.Right);
        bar.Children.Add(leftPanel);
        bar.Children.Add(rightPanel);
        bar.Children.Add(status);

        var top = new Border
        {
            Child = bar,
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x18)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        var layout = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        layout.Children.Add(top);
        layout.Children.Add(webView);
        return layout;
    }

    // One of the bar's icon buttons: a Segoe Fluent Icons glyph, its label as the tooltip and the accessible name.
    private static T Tool<T>(T button, string glyph, string label, RoutedEventHandler? click = null) where T : ButtonBase
    {
        button.Content = glyph;
        button.ToolTip = label;
        AutomationProperties.SetName(button, label);
        button.Style = (Style)ToolStyles[button is ToggleButton ? "ToolToggle" : "ToolButton"];
        if (click is not null) button.Click += click;
        return button;
    }

    // The bar's flat icon buttons in the meter's colours: muted, lighter on hover, the pin in the soft scarlet when on.
    private static readonly ResourceDictionary ToolStyles = (ResourceDictionary)XamlReader.Parse("""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style x:Key="ToolButton" TargetType="ButtonBase">
            <Setter Property="Width" Value="30"/>
            <Setter Property="Height" Value="26"/>
            <Setter Property="Margin" Value="1,0"/>
            <Setter Property="Foreground" Value="#8D8A9D"/>
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="FontFamily" Value="Segoe Fluent Icons, Segoe MDL2 Assets"/>
            <Setter Property="FontSize" Value="13"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="ButtonBase">
                  <Border x:Name="Box" Background="{TemplateBinding Background}" CornerRadius="5">
                    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter Property="Foreground" Value="#EBE9F2"/>
                      <Setter TargetName="Box" Property="Background" Value="#14FFFFFF"/>
                    </Trigger>
                    <Trigger Property="IsKeyboardFocused" Value="True">
                      <Setter TargetName="Box" Property="Background" Value="#14FFFFFF"/>
                    </Trigger>
                    <Trigger Property="IsPressed" Value="True">
                      <Setter TargetName="Box" Property="Background" Value="#26FFFFFF"/>
                    </Trigger>
                    <Trigger Property="IsEnabled" Value="False">
                      <Setter Property="Opacity" Value="0.4"/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
          <Style x:Key="ToolToggle" TargetType="ToggleButton" BasedOn="{StaticResource ToolButton}">
            <Style.Triggers>
              <Trigger Property="IsChecked" Value="True">
                <Setter Property="Foreground" Value="#F28B9B"/>
              </Trigger>
            </Style.Triggers>
          </Style>
        </ResourceDictionary>
        """);
}
