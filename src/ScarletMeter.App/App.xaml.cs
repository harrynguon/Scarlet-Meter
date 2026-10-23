using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScarletMeter.App.Alerts;
using ScarletMeter.App.Settings;
using ScarletMeter.App.Updates;
using ScarletMeter.App.Windows;
using ScarletMeter.Engine;
using Serilog;
using Serilog.Events;

namespace ScarletMeter.App;

/// <summary>
/// Composition root. Builds the generic host with the combat engine and Blazor WebView services, starts
/// capture (unless --no-capture) and shows the meter window. Everything written to disk goes under the
/// absolute data directory in <see cref="EngineOptions"/>; the working directory is never used. Any startup
/// failure ends in an error dialog, and is logged once the log exists; unhandled exceptions are logged
/// before the process terminates.
/// </summary>
public partial class App : Application
{
    private IHost? host;
    private ICombatEngine? engine;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Logged, not handled: the process still terminates, but leaves a trace for in-game testing.
        DispatcherUnhandledException += (_, args) =>
            Log(logger => logger.LogCritical(args.Exception, "Unhandled exception on the UI thread."));
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log(logger => logger.LogCritical(args.ExceptionObject as Exception, "Unhandled exception."));

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

        try
        {
            StartMeter(launch, e.Args);
        }
        catch (CaptureStartException ex)
        {
            Log(logger => logger.LogError(ex, "Capture failed to start."));
            Fail("Packet capture could not start", ex.Message);
        }
        catch (Exception ex)
        {
            // An uncreatable data directory, missing game data, an unusable history database, a window failure.
            Log(logger => logger.LogCritical(ex, "Startup failed."));
            Fail("Scarlet Meter could not start", ex.Message);
        }
    }

    // Builds the host, starts capture and shows the window. Throws on any failure; OnStartup reports it.
    private void StartMeter(LaunchOptions launch, string[] args)
    {
        var options = new EngineOptions { Capture = launch.NoCapture ? CaptureMode.None : CaptureMode.Live };
        if (launch.DataDirectory is not null) options.DataDirectory = launch.DataDirectory;
        Directory.CreateDirectory(options.DataDirectory);

        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory, // never the working directory
        });
        builder.Services.AddSerilog(configuration => configuration
            .MinimumLevel.Information()
            // EF Core logs every SQL command at Information, which buries the capture lines.
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .WriteTo.File(
                Path.Combine(options.DataDirectory, "logs", "scarlet-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14));
        builder.Services.AddScarletEngine(options);
        builder.Services.AddSingleton(services => new SettingsStore(options.DataDirectory, services.GetRequiredService<ILogger<SettingsStore>>()));
        builder.Services.AddSingleton<SettingsService>();
        builder.Services.AddSingleton<MeterWindows>();
        builder.Services.AddSingleton<IMeterWindows>(services => services.GetRequiredService<MeterWindows>());
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<AlertMonitor>();
        builder.Services.AddUpstreamReleaseChecker();
        builder.Services.AddSingleton<UpdateNotice>();
        builder.Services.AddWpfBlazorWebView();

        host = builder.Build();
        host.Start();
        // Settings load before the engine resolves, so upstream sees the saved fight rules from the first hit.
        host.Services.GetRequiredService<SettingsService>();
        engine = host.Services.GetRequiredService<ICombatEngine>();
        engine.Start();

        var windows = host.Services.GetRequiredService<MeterWindows>();
        MainWindow = windows.ShowOverlay();
        windows.FollowAlerts(host.Services.GetRequiredService<AlertMonitor>());
        host.Services.GetRequiredService<UpdateNotice>().Start();
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

    // Writes to the meter's log once the host exists. Before that (bad arguments, an uncreatable data
    // directory) there is no log file, and the dialog is the only report. Never throws, so logging cannot
    // mask the failure being reported.
    private void Log(Action<ILogger<App>> write)
    {
        try
        {
            if (host?.Services.GetService<ILogger<App>>() is { } logger) write(logger);
        }
        catch (Exception)
        {
            // The log is best effort here.
        }
    }
}
