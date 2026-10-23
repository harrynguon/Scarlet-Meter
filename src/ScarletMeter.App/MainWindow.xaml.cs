using System.ComponentModel;
using System.IO;
using System.Windows;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ScarletMeter.App.Pages;
using ScarletMeter.Engine;

namespace ScarletMeter.App;

/// <summary>
/// The meter window: one BlazorWebView rendering <see cref="MeterPage"/>. Closing releases the Blazor
/// content before WPF destroys the WebView (see <see cref="DeferredWindowClose"/>); because the application
/// shuts down with this window, the engine is disposed afterwards in App.OnExit. The WebView2 browser
/// profile lives under the data directory, so --data-dir isolates a run completely.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ILogger<MainWindow> logger;
    private readonly DeferredWindowClose deferredClose;

    public MainWindow(IServiceProvider services)
    {
        InitializeComponent();
        logger = services.GetRequiredService<ILogger<MainWindow>>();

        // Without this, BlazorWebView puts the profile (cache, local storage, GPU cache) in
        // %LocalAppData%\<entry assembly>.WebView2. DataDirectory is absolute and App has created it; WebView2
        // creates the subfolder. The WebView starts once its template is applied, after this constructor.
        var profile = Path.Combine(services.GetRequiredService<EngineOptions>().DataDirectory, "webview2");
        WebView.BlazorWebViewInitializing += (_, e) => e.UserDataFolder = profile;

        // Root components first, then Services, the order BlazorWebView expects.
        WebView.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = typeof(MeterPage) });
        WebView.Services = services;
        WebView.BlazorWebViewInitialized += (_, e) =>
            e.WebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x0B, 0x0B, 0x0F);

        deferredClose = new DeferredWindowClose(
            ReleaseBlazorContentAsync,
            Close,
            ex => logger.LogWarning(ex, "Blazor content disposal failed; closing anyway."));
        Closed += (_, _) => deferredClose.MarkClosed();
    }

    // deferredClose is null when the constructor threw after WPF registered the window: App's Shutdown then
    // closes the half-built window, which has no Blazor content to release.
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!e.Cancel && deferredClose is not null)
            e.Cancel = deferredClose.ShouldCancelClose();
    }

    // BlazorWebView.DisposeAsync disposes the Razor components first, then the WebView2 control on whichever
    // thread the component disposal completed on. The composition control refuses cross-thread COM access
    // (InvalidCastException, E_NOINTERFACE). The components are already gone by then, so the native control
    // is disposed again here, on the UI thread.
    private async ValueTask ReleaseBlazorContentAsync()
    {
        try
        {
            await WebView.DisposeAsync();
        }
        catch (InvalidCastException ex)
        {
            logger.LogInformation(ex, "BlazorWebView disposed the WebView2 control off the UI thread; disposing it on the UI thread.");
            WebView.WebView?.Dispose();
            logger.LogInformation("WebView2 control disposed on the UI thread.");
        }
    }
}
