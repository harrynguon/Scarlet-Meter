using System.ComponentModel;
using System.Windows;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ScarletMeter.App.Pages;

namespace ScarletMeter.App;

/// <summary>
/// The meter window: one BlazorWebView rendering <see cref="MeterPage"/>. Closing releases the Blazor
/// content before WPF destroys the WebView (see <see cref="DeferredWindowClose"/>); because the application
/// shuts down with this window, the engine is disposed afterwards in App.OnExit.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ILogger<MainWindow> logger;
    private readonly DeferredWindowClose deferredClose;

    public MainWindow(IServiceProvider services)
    {
        InitializeComponent();
        logger = services.GetRequiredService<ILogger<MainWindow>>();

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

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!e.Cancel)
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
