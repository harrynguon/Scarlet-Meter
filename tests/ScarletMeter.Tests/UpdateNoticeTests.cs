using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ScarletMeter.App.Settings;
using ScarletMeter.App.Updates;
using ScarletMeter.App.Windows;
using ScarletMeter.Engine;
using static ScarletMeter.App.Updates.UpstreamReleaseCheckResult;

namespace ScarletMeter.Tests;

/// <summary>
/// The overlay's "newer RATmeter release" notice over a fake checker and clock: when it checks, what it shows,
/// dismissing, the off switch, and the allow-list for the release page the meter may open.
/// </summary>
public sealed class UpdateNoticeTests : IDisposable
{
    private static readonly Uri Page = new("https://github.com/Kuroukihime/AIon2-Dps-Meter/releases/tag/v1.11.6.350");
    private static readonly NewerAvailable Newer = new("v1.11.5.341", "v1.11.6.350", Page, null);

    private readonly TestDataDirectory directory = new();
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeChecker checker = new();

    [Fact]
    public void TheFirstCheckWaitsForStartUpAndANewerReleaseShows()
    {
        using var settings = NewSettings();
        using var notice = NewNotice(settings);
        checker.Answer = Newer;
        var changes = 0;
        notice.Changed += (_, _) => changes++;

        notice.Start();
        Assert.Equal(0, checker.Calls);
        clock.Advance(UpdateNotice.StartupDelay);
        Assert.Equal(1, checker.Calls);
        Assert.Equal(1, changes);
        Assert.Same(Newer, notice.Visible);
        Assert.Equal(clock.GetUtcNow(), notice.CheckedAt);
    }

    [Fact]
    public void ItChecksAgainWhenTheCheckersAnswerExpires()
    {
        using var settings = NewSettings();
        using var notice = NewNotice(settings);
        checker.Answer = new UpToDate("v1.11.5.341", "v1.11.5.341");
        notice.Start();
        clock.Advance(UpdateNotice.StartupDelay);
        Assert.Null(notice.Visible);

        clock.Advance(UpstreamReleaseChecker.CheckInterval - TimeSpan.FromSeconds(1));
        Assert.Equal(1, checker.Calls);
        checker.Answer = new Unknown(UpstreamReleaseCheckFailure.Network, "No connection.");
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(2, checker.Calls);

        // After no answer, the checker's retry interval applies.
        checker.Answer = Newer;
        clock.Advance(UpstreamReleaseChecker.RetryInterval + TimeSpan.FromSeconds(10));
        Assert.Equal(3, checker.Calls);
        Assert.Same(Newer, notice.Visible);
    }

    [Fact]
    public void ADismissedReleaseStaysHiddenUntilALaterOne()
    {
        using var settings = NewSettings();
        using var notice = NewNotice(settings);
        checker.Answer = Newer;
        notice.Start();
        clock.Advance(UpdateNotice.StartupDelay);

        notice.Dismiss(Newer);
        Assert.Equal(1, checker.Calls); // dismissing asks nothing
        Assert.Null(notice.Visible);
        Assert.Equal("v1.11.6.350", settings.Current.Updates.DismissedRelease);
        Assert.IsType<NewerAvailable>(notice.Result); // still shown in Settings, About

        var later = Newer with { Tag = "v1.11.7.360" };
        checker.Answer = later;
        clock.Advance(UpstreamReleaseChecker.CheckInterval + TimeSpan.FromMinutes(1));
        Assert.Same(later, notice.Visible);
    }

    [Fact]
    public void SwitchedOffItNeverAsksAndShowsNothing()
    {
        using var settings = NewSettings();
        settings.Update(s => s with { Updates = s.Updates with { CheckUpstreamReleases = false } });
        using var notice = NewNotice(settings);
        checker.Answer = Newer;
        notice.Start();
        clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(0, checker.Calls);
        Assert.Null(notice.Result);

        settings.Update(s => s with { Updates = s.Updates with { CheckUpstreamReleases = true } });
        Assert.Equal(1, checker.Calls); // switching on checks at once (the checker answers from its cache if it can)
        Assert.Same(Newer, notice.Visible);

        var changes = 0;
        notice.Changed += (_, _) => changes++;
        settings.Update(s => s with { Updates = s.Updates with { CheckUpstreamReleases = false } });
        Assert.Equal(1, changes); // the page re-renders without the line
        Assert.Null(notice.Visible);
        clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(1, checker.Calls);
    }

    [Fact]
    public void ACheckInFlightWhenTheSwitchGoesOffBooksNoOther()
    {
        using var settings = NewSettings();
        using var notice = NewNotice(settings);
        checker.Pending = new TaskCompletionSource<UpstreamReleaseCheckResult>();
        notice.Start();
        clock.Advance(UpdateNotice.StartupDelay);
        Assert.Equal(1, checker.Calls); // in flight

        settings.Update(s => s with { Updates = s.Updates with { CheckUpstreamReleases = false } });
        checker.Pending.SetResult(Newer);
        clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(1, checker.Calls);
        Assert.Null(notice.Visible);
    }

    [Fact]
    public void ACheckInFlightWhenTheMeterClosesBooksNoOther()
    {
        using var settings = NewSettings();
        var notice = NewNotice(settings);
        checker.Pending = new TaskCompletionSource<UpstreamReleaseCheckResult>();
        notice.Start();
        clock.Advance(UpdateNotice.StartupDelay);

        notice.Dispose();
        Assert.True(checker.LastToken.IsCancellationRequested);
        checker.Pending.SetResult(Newer);
        clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(1, checker.Calls);
    }

    [Fact]
    public void OtherSettingsChangesNeitherAskNorRedraw()
    {
        using var settings = NewSettings();
        using var notice = NewNotice(settings);
        checker.Answer = Newer;
        notice.Start();
        clock.Advance(UpdateNotice.StartupDelay);
        var changes = 0;
        notice.Changed += (_, _) => changes++;

        settings.Update(s => s with { Appearance = s.Appearance with { BackgroundOpacity = 0.5 } });
        Assert.Equal(1, checker.Calls);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void AThrowingCheckerIsRetriedAndStartingTwiceOrAfterDisposalDoesNothing()
    {
        using var settings = NewSettings();
        var notice = NewNotice(settings);
        checker.Throw = true;
        notice.Start();
        notice.Start();
        clock.Advance(UpdateNotice.StartupDelay);
        Assert.Equal(1, checker.Calls);

        checker.Throw = false;
        checker.Answer = Newer;
        clock.Advance(UpstreamReleaseChecker.RetryInterval);
        Assert.Equal(2, checker.Calls);
        Assert.Same(Newer, notice.Visible);

        notice.Dispose();
        notice.Start();
        clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(2, checker.Calls);
    }

    [Theory]
    [InlineData("https://github.com/Kuroukihime/AIon2-Dps-Meter/releases/tag/v1.11.6.350", true)]
    [InlineData("https://GITHUB.com/kuroukihime/aion2-dps-meter/releases/latest", true)]
    [InlineData("http://github.com/Kuroukihime/AIon2-Dps-Meter/releases/tag/v1.11.6.350", false)]
    [InlineData("https://github.com.evil.example/Kuroukihime/AIon2-Dps-Meter/releases/tag/v1", false)]
    [InlineData("https://github.com:8443/Kuroukihime/AIon2-Dps-Meter/releases/tag/v1", false)]
    [InlineData("https://user@github.com/Kuroukihime/AIon2-Dps-Meter/releases/tag/v1", false)]
    [InlineData("https://github.com/Kuroukihime/AIon2-Dps-Meter/releases/../../other/repo", false)]
    [InlineData("https://github.com/Someone/AIon2-Dps-Meter/releases/tag/v1", false)]
    [InlineData("https://github.com/Kuroukihime/AIon2-Dps-Meter/releases/tag/v1?x=1", false)]
    [InlineData("https://github.com/Kuroukihime/AIon2-Dps-Meter/releases/tag/v1#notes", false)]
    [InlineData("https://@github.com/Kuroukihime/AIon2-Dps-Meter/releases/tag/v1", false)]
    [InlineData("https://github.com/Kuroukihime/AIon2-Dps-Meter/releases/%2e%2e/%2e%2e/evil/repo", false)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    public void OnlyUpstreamReleasePagesMayBeOpened(string address, bool allowed) =>
        Assert.Equal(allowed, MeterWindows.IsUpstreamReleasePage(new Uri(address)));

    [Fact]
    public void ADismissedReleaseMustBeAReleaseTag()
    {
        var (kept, keptRepairs) = SettingsNormalizer.Normalize(new MeterSettings { Updates = new UpdateSettings { DismissedRelease = "v1.11.6.350" } });
        Assert.Equal("v1.11.6.350", kept.Updates.DismissedRelease);
        Assert.Empty(keptRepairs);

        var (cleared, repairs) = SettingsNormalizer.Normalize(new MeterSettings { Updates = new UpdateSettings { DismissedRelease = "<script>" } });
        Assert.Null(cleared.Updates.DismissedRelease);
        Assert.Contains(repairs, r => r.Contains("dismissed release"));
        Assert.True(SettingsNormalizer.Normalize(new MeterSettings { Updates = null! }).Settings.Updates.CheckUpstreamReleases);
    }

    private SettingsService NewSettings() =>
        new(new SettingsStore(directory.FullPath, NullLogger<SettingsStore>.Instance), new EngineSettings(), NullLogger<SettingsService>.Instance);

    private UpdateNotice NewNotice(SettingsService settings) => new(checker, settings, clock, NullLogger<UpdateNotice>.Instance);

    public void Dispose() => directory.Dispose();

    // Answers at once with whatever the test set, stays in flight until the test completes Pending, or throws
    // (which the real checker never does). Pending's continuations run synchronously, so tests stay deterministic.
    private sealed class FakeChecker : IUpstreamReleaseChecker
    {
        public UpstreamReleaseCheckResult Answer { get; set; } = new Unknown(UpstreamReleaseCheckFailure.Network, "Not set.");

        public TaskCompletionSource<UpstreamReleaseCheckResult>? Pending { get; set; }

        public bool Throw { get; set; }

        public int Calls { get; private set; }

        public CancellationToken LastToken { get; private set; }

        public UpstreamReleaseCheckResult? LastResult { get; private set; }

        public Task<UpstreamReleaseCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            LastToken = cancellationToken;
            if (Throw) throw new InvalidOperationException("checker");
            if (Pending is { } pending) return pending.Task;
            LastResult = Answer;
            return Task.FromResult(Answer);
        }
    }
}
