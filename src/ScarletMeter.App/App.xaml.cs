using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScarletMeter.Engine;
using Serilog;

namespace ScarletMeter.App;

/// <summary>
/// Composition root. Builds the generic host with the combat engine and Blazor WebView services, starts
/// capture (unless --no-capture) and shows the meter window. Everything written to disk goes under the
/// absolute data directory in <see cref="EngineOptions"/>; the working directory is never used.
/// </summary>
public partial class App : Application
{
    private IHost? host;
    private ICombatEngine? engine;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        LaunchOptions launch;
        try
        {
            launch = LaunchOptions.Parse(e.Args);
        }
        catch (ArgumentException ex)
        {
            Fail("Scarlet Meter could not start", ex.Message);
            return;
        }

        var options = new EngineOptions { Capture = launch.NoCapture ? CaptureMode.None : CaptureMode.Live };
        if (launch.DataDirectory is not null) options.DataDirectory = launch.DataDirectory;
        Directory.CreateDirectory(options.DataDirectory);

        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = e.Args,
            ContentRootPath = AppContext.BaseDirectory, // never the working directory
        });
        builder.Services.AddSerilog(configuration => configuration
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(options.DataDirectory, "logs", "scarlet-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14));
        builder.Services.AddScarletEngine(options);
        builder.Services.AddWpfBlazorWebView();

        host = builder.Build();
        host.Start();
        engine = host.Services.GetRequiredService<ICombatEngine>();

        try
        {
            engine.Start();
        }
        catch (CaptureStartException ex)
        {
            host.Services.GetRequiredService<ILogger<App>>().LogError(ex, "Capture failed to start.");
            Fail("Packet capture could not start", ex.Message);
            return;
        }

        MainWindow = new MainWindow(host.Services);
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // The engine completes and flushes open fights before the container, and its database, goes away.
        engine?.Dispose();
        host?.Dispose();
        base.OnExit(e);
    }

    private void Fail(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(1);
    }
}
