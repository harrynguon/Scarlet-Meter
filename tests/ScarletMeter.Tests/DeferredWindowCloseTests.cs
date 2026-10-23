using System.Windows.Threading;
using ScarletMeter.App;

namespace ScarletMeter.Tests;

// Close ordering for Blazor windows: content is released before the native WebView is destroyed,
// and never inside the call that requested the close. Ported unchanged, with DeferredWindowClose itself, from
// the harrynguon/AIon2-Dps-Meter fork (AionDpsMeter.Tests/DeferredWindowCloseTests.cs).
public sealed class DeferredWindowCloseTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    // The gate runs on its window's dispatcher, so its deferred turns wait until the caller yields. On the
    // thread pool they could run before the test's next statement (for example before MarkClosed), so each
    // test runs on its own WPF dispatcher thread.
    private static Task OnDispatcherAsync(Func<Task> body)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await body();
                    done.TrySetResult();
                }
                catch (Exception ex)
                {
                    done.TrySetException(ex);
                }
                finally
                {
                    dispatcher.InvokeShutdown();
                }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    [Fact]
    public Task FirstCloseWaitsForReleaseThenClosesOnce() => OnDispatcherAsync(async () =>
    {
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var releases = 0;
        var closes = 0;
        var gate = new DeferredWindowClose(
            () => { releases++; started.TrySetResult(); return new ValueTask(release.Task); },
            () => closes++);

        Assert.True(gate.ShouldCancelClose());
        Assert.True(gate.ShouldCancelClose());
        await started.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Equal(0, closes);

        release.SetResult();
        await gate.Completion.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.False(gate.ShouldCancelClose());
        Assert.Equal(1, releases);
        Assert.Equal(1, closes);
    });

    [Fact]
    public Task SynchronousReleaseStillRunsOutsideTheClosingCall() => OnDispatcherAsync(async () =>
    {
        var releases = 0;
        var closes = 0;
        var gate = new DeferredWindowClose(() => { releases++; return ValueTask.CompletedTask; }, () => closes++);

        // A JS-requested close must return (and reply) before the WebView can be disposed.
        Assert.True(gate.ShouldCancelClose());
        Assert.Equal(0, releases);

        await gate.Completion.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Equal(1, releases);
        Assert.Equal(1, closes);
        Assert.False(gate.ShouldCancelClose());
    });

    [Fact]
    public Task ReleaseFailureIsReportedAndStillCloses() => OnDispatcherAsync(async () =>
    {
        Exception? reported = null;
        var closes = 0;
        var gate = new DeferredWindowClose(
            async () => { await Task.Yield(); throw new InvalidOperationException("disposal failed"); },
            () => closes++,
            ex => reported = ex);

        Assert.True(gate.ShouldCancelClose());
        await gate.Completion.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.IsType<InvalidOperationException>(reported);
        Assert.Equal(1, closes);
        Assert.False(gate.ShouldCancelClose());
    });

    [Fact]
    public Task SynchronousReleaseFailureDoesNotBlockClosing() => OnDispatcherAsync(async () =>
    {
        Exception? reported = null;
        var closes = 0;
        var gate = new DeferredWindowClose(
            () => throw new InvalidOperationException("disposal failed"),
            () => closes++,
            ex => reported = ex);

        Assert.True(gate.ShouldCancelClose());
        await gate.Completion.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.NotNull(reported);
        Assert.Equal(1, closes);
    });

    [Fact]
    public Task ForcedCloseBeforeReleaseSkipsReleaseAndClose() => OnDispatcherAsync(async () =>
    {
        var releases = 0;
        var closes = 0;
        var gate = new DeferredWindowClose(() => { releases++; return ValueTask.CompletedTask; }, () => closes++);

        Assert.True(gate.ShouldCancelClose());
        // Application shutdown ignores the cancellation and destroys the WebView immediately.
        gate.MarkClosed();
        await gate.Completion.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Equal(0, releases);
        Assert.Equal(0, closes);
        Assert.False(gate.ShouldCancelClose());
    });

    [Fact]
    public Task ForcedCloseDuringReleaseIsNotRepeated() => OnDispatcherAsync(async () =>
    {
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var closes = 0;
        var gate = new DeferredWindowClose(
            () => { started.TrySetResult(); return new ValueTask(release.Task); },
            () => closes++);

        Assert.True(gate.ShouldCancelClose());
        await started.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        gate.MarkClosed();
        release.SetResult();
        await gate.Completion.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Equal(0, closes);
    });
}
