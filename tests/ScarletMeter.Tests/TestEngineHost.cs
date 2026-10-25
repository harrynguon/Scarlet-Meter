using System.IO;
using System.Runtime.ExceptionServices;
using AionDpsMeter.Services.Services.Session.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ScarletMeter.Engine;

namespace ScarletMeter.Tests;

/// <summary>
/// The engine's real service graph over a throwaway data directory, for one test. Upstream's history store
/// starts a cleanup, fire-and-forget, in its constructor (expired-session delete, VACUUM, ANALYZE). The host
/// hands the store out only once that cleanup has finished, so it cannot delete a row a test writes, and
/// disposal waits for it too: the task outlives the provider and re-creates the database when the directory
/// is deleted under it. Then the graph is disposed and the directory deleted, so no test leaves files behind.
/// </summary>
internal sealed class TestEngineHost : IDisposable
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(10);

    private readonly TestDataDirectory directory = new();
    private readonly HistoryCleanupWatcher cleanup = new();
    private readonly EngineOptions options;

    /// <param name="configure">Runs after AddScarletEngine, so its registrations win.</param>
    public TestEngineHost(CaptureMode capture, Action<IServiceCollection>? configure = null)
    {
        options = new EngineOptions { DataDirectory = directory.FullPath, Capture = capture };
        var services = new ServiceCollection().AddScarletEngine(options);
        services.AddLogging(logging => logging.AddProvider(cleanup));
        configure?.Invoke(services);
        HandOutHistoryStoreAfterItsCleanup(services);
        // ValidateOnBuild stays off: upstream registers every type in its processor namespace, including nested
        // records and compiler-generated iterators that nothing resolves and DI cannot construct. Resolving the
        // engine still constructs every real processor through the opcode registry.
        Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public ServiceProvider Services { get; }

    /// <summary>The absolute data directory the graph writes to.</summary>
    public string DataDirectory => directory.FullPath;

    // Replaces the history store's registration (the test's own, when it makes one) with one that returns the
    // store only after its startup cleanup has finished. The cleanup runs on the thread pool, so on a busy runner
    // it can start seconds late and delete rows a test has written since: a fight past the retention period, or
    // one damaged to end near DateTime.MinValue (LESSONS, "A late history cleanup"). Everything that writes
    // history, the engine included, gets the store through here. A store that is not upstream's starts no
    // cleanup and comes back at once.
    private void HandOutHistoryStoreAfterItsCleanup(IServiceCollection services)
    {
        var registered = services.Last(d => d.ServiceType == typeof(ICombatHistoryStore) && !d.IsKeyedService);
        services.RemoveAll<ICombatHistoryStore>();
        services.AddSingleton<ICombatHistoryStore>(provider =>
        {
            var store = (ICombatHistoryStore)(registered.ImplementationInstance
                ?? registered.ImplementationFactory?.Invoke(provider)
                ?? ActivatorUtilities.CreateInstance(provider, registered.ImplementationType!));
            cleanup.WaitUntilFinished(CleanupTimeout);
            return store;
        });
    }

    // A timeout is reported after the graph and directory are released, together with any failure there, so
    // neither hides the other.
    public void Dispose()
    {
        Exception? cleanupFailure = null;
        try
        {
            // The store creates the database before it starts the cleanup. No file means the store was never
            // built or failed first (the test then fails with that error), so there is nothing to wait for.
            if (File.Exists(options.HistoryDatabasePath)) cleanup.WaitUntilFinished(CleanupTimeout);
        }
        catch (TimeoutException ex)
        {
            cleanupFailure = ex;
        }

        try
        {
            try { Services.Dispose(); }
            finally { directory.Dispose(); }
        }
        catch (Exception ex) when (cleanupFailure is not null)
        {
            throw new AggregateException(cleanupFailure, ex);
        }
        if (cleanupFailure is not null) ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
    }

    /// <summary>
    /// Tells when the history store's startup cleanup has finished, by the log entry it writes either way
    /// ("Combat history startup cleanup completed." or "... failed."). Upstream exposes no other signal. If an
    /// upstream release rewords those entries, <see cref="WaitUntilFinished"/> times out and names this class.
    /// </summary>
    private sealed class HistoryCleanupWatcher : ILoggerProvider
    {
        private static readonly string StoreCategory = typeof(CombatHistoryStore).FullName!;

        private readonly TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool storeCreated;

        // The store's logger is created with the store, so a store that was never resolved started no cleanup.
        public ILogger CreateLogger(string categoryName)
        {
            if (categoryName != StoreCategory) return NullLogger.Instance;
            storeCreated = true;
            return new StoreLogger(finished);
        }

        public void WaitUntilFinished(TimeSpan timeout)
        {
            if (storeCreated && !finished.Task.Wait(timeout))
                throw new TimeoutException(
                    "Upstream's history cleanup did not report finishing. Either the history store failed after " +
                    "creating its database (see the test's own failure), or an upstream release reworded its " +
                    "\"Combat history startup cleanup\" log entries: then update TestEngineHost.HistoryCleanupWatcher.");
        }

        public void Dispose()
        {
        }

        private sealed class StoreLogger(TaskCompletionSource finished) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (formatter(state, exception).StartsWith("Combat history startup cleanup", StringComparison.Ordinal))
                    finished.TrySetResult();
            }
        }
    }
}
