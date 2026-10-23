using System.ComponentModel;
using System.IO;
using System.Windows;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ScarletMeter.App.Settings;
using ScarletMeter.Engine;

namespace ScarletMeter.App.Windows;

/// <summary>
/// A window hosting one Blazor root component, with the hosting rules every Scarlet window shares: the
/// WebView2 profile and image hosts live under the data directory, the placement is remembered, and a
/// close is deferred until the Blazor content is released (<see cref="DeferredWindowClose"/>), because a
/// Blazor message reaching a destroyed WebView terminates the process.
/// </summary>
public abstract class ScarletWindow : Window
{
    private readonly DeferredWindowClose deferredClose;
    private readonly WindowPlacementTracker placement;

    protected ScarletWindow(IServiceProvider services, Type rootComponent, string placementKey)
    {
        Services = services;
        Logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());
        var dataDirectory = services.GetRequiredService<EngineOptions>().DataDirectory;

        // Without this BlazorWebView puts the profile (cache, local storage) in %LocalAppData%\<entry
        // assembly>.WebView2. DataDirectory is absolute and App has created it; WebView2 creates the folder.
        // The WebView starts once its template is applied, after the constructors have run.
        var profile = Path.Combine(dataDirectory, "webview2");
        WebView = new BlazorWebView { HostPage = "wwwroot/index.html" };
        WebView.BlazorWebViewInitializing += (_, e) => e.UserDataFolder = profile;
        WebView.BlazorWebViewInitialized += (_, e) =>
        {
            e.WebView.DefaultBackgroundColor = WebViewBackground;
            WebAssetHosts.Map(e.WebView.CoreWebView2, dataDirectory);
        };
        // Root components first, then Services, the order BlazorWebView expects.
        WebView.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = rootComponent });
        WebView.Services = services;
        Content = WebView;

        placement = new WindowPlacementTracker(this, placementKey, services.GetRequiredService<SettingsService>());
        deferredClose = new DeferredWindowClose(
            ReleaseBlazorContentAsync,
            Close,
            ex => Logger.LogWarning(ex, "Blazor content disposal failed; closing anyway."));
        Closed += (_, _) => deferredClose.MarkClosed();
    }

    protected IServiceProvider Services { get; }

    protected ILogger Logger { get; }

    protected BlazorWebView WebView { get; }

    /// <summary>Completes once a requested close has released the content and closed the window.</summary>
    public Task CloseCompletion => deferredClose.Completion;

    /// <summary>The colour the WebView paints before and under the page.</summary>
    protected virtual System.Drawing.Color WebViewBackground => System.Drawing.Color.FromArgb(0x0B, 0x0B, 0x0F);

    /// <summary>Restores the saved placement; subclasses call it at the end of their constructor, after their defaults.</summary>
    protected bool RestorePlacement() => placement.Restore();

    /// <summary>Work that must finish before this window's Blazor content is released.</summary>
    protected virtual ValueTask BeforeReleaseAsync() => ValueTask.CompletedTask;

    // deferredClose is null when a constructor threw after WPF registered the window: App's Shutdown then
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
        await BeforeReleaseAsync();
        try
        {
            await WebView.DisposeAsync();
        }
        catch (InvalidCastException ex)
        {
            Logger.LogInformation(ex, "BlazorWebView disposed the WebView2 control off the UI thread; disposing it on the UI thread.");
            WebView.WebView?.Dispose();
            Logger.LogInformation("WebView2 control disposed on the UI thread.");
        }
    }
}
