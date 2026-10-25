using System.Reflection;
using AionDpsMeter.Services.Services.Session.Persistence;
using AionDpsMeter.Services.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using ScarletMeter.Engine;
using ScarletMeter.Engine.Upstream;

namespace ScarletMeter.Tests;

/// <summary>
/// The test host's promise the history tests rely on: upstream's history store is handed out only once the
/// cleanup its constructor starts on the thread pool has finished, so that cleanup cannot delete rows a test
/// writes afterwards (LESSONS, "A late history cleanup").
/// </summary>
public sealed class TestEngineHostTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task TheHistoryStoreIsHandedOutOnlyOnceItsStartupCleanupHasFinished()
    {
        var token = TestContext.Current.CancellationToken;
        var retention = new HeldRetention();
        using var host = new TestEngineHost(CaptureMode.None, services => services.AddSingleton(provider =>
            retention.Wrap(ActivatorUtilities.CreateInstance<UpstreamSettingsBridge>(provider))));

        var resolving = Task.Run(() => host.Services.GetRequiredService<ICombatHistoryStore>(), token);
        try
        {
            // The store is built and its cleanup has started, held where it reads the retention period, as a
            // busy thread pool would hold it.
            Assert.True(retention.Entered.Wait(Patience, token));
            var first = await Task.WhenAny(resolving, Task.Delay(TimeSpan.FromMilliseconds(300), token));
            Assert.NotSame(resolving, first); // no test gets the store while its cleanup can still delete rows
        }
        finally
        {
            retention.Release();
        }
        Assert.IsType<CombatHistoryStore>(await resolving.WaitAsync(Patience, token));
    }

    /// <summary>
    /// Upstream's settings with a gate on the retention period, which only the history store's startup cleanup
    /// reads, before it deletes anything: holding the gate holds the cleanup. Everything else passes through.
    /// </summary>
    private sealed class HeldRetention
    {
        private readonly ManualResetEventSlim open = new();

        public ManualResetEventSlim Entered { get; } = new();

        public void Release() => open.Set();

        public IAppSettingsService Wrap(IAppSettingsService inner)
        {
            var proxy = DispatchProxy.Create<IAppSettingsService, Proxy>();
            ((Proxy)(object)proxy).Bind(inner, this);
            return proxy;
        }

        // DispatchProxy builds its type on an unsealed class with a public constructor.
        public class Proxy : DispatchProxy
        {
            private IAppSettingsService inner = null!;
            private HeldRetention gate = null!;

            public void Bind(IAppSettingsService inner, HeldRetention gate) => (this.inner, this.gate) = (inner, gate);

            protected override object? Invoke(MethodInfo? method, object?[]? args)
            {
                ArgumentNullException.ThrowIfNull(method);
                if (method.Name == "get_" + nameof(IAppSettingsService.HistoryRetantionPeriod))
                {
                    gate.Entered.Set();
                    gate.open.Wait(TimeSpan.FromSeconds(10), CancellationToken.None); // never hangs a test run for good
                }
                return method.Invoke(inner, args);
            }
        }
    }
}
