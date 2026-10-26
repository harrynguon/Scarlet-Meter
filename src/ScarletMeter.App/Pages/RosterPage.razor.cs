using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using ScarletMeter.App.Formatting;
using ScarletMeter.App.Roster;
using ScarletMeter.App.Settings;

namespace ScarletMeter.App.Pages;

/// <summary>How the roster is ordered.</summary>
public enum RosterSort
{
    /// <summary>Most basic Odyle energy (as estimated now) first.</summary>
    MostOdyle,
    /// <summary>Full soonest first; the ones already full at the top, since every refill from then on is lost.</summary>
    FullSoonest,
    /// <summary>By name.</summary>
    Name,
    /// <summary>Played most recently first.</summary>
    LastSeen,
}

/// <summary>One character as the page shows it at an instant: its estimates and when its energy is full.</summary>
public sealed record RosterRow(
    RosterCharacter Character, bool IsCurrent, int Cap, Estimate? Odyle, DateTimeOffset? FullAt, Estimate? Nightmare, Estimate? Ascension)
{
    public RosterKey Key => Character.Key;
}

/// <summary>
/// The character roster: every character the meter has seen the player log in on, with Odyle energy, when basic energy
/// is full again, and Nightmare and Ascension Trial entries left, each as the game last said plus the recharges since
/// (<see cref="RosterRules"/>, marked ≈). Re-reads the roster when it changes and every half minute, for the estimates.
/// </summary>
public partial class RosterPage : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

    /// <summary>How long a removal waits for its second click.</summary>
    public static readonly TimeSpan RemoveConfirmWindow = TimeSpan.FromSeconds(4);

    // Cancelled when the window closes; not disposed, since the loop's last wait may still check it.
    private readonly CancellationTokenSource stopping = new();
    private RosterSort sort = RosterSort.MostOdyle;
    private (RosterKey Key, DateTimeOffset At)? armedRemoval;

    [Inject] private RosterService Roster { get; set; } = default!;

    [Inject] private SettingsService Settings { get; set; } = default!;

    [Inject] private TimeProvider Clock { get; set; } = default!;

    [Inject] private ILogger<RosterPage> Logger { get; set; } = default!;

    protected override void OnInitialized()
    {
        Roster.Changed += OnRosterChanged;
        Settings.Changed += OnSettingsChanged;
        _ = RefreshLoopAsync();
    }

    /// <summary>The rows at <paramref name="now"/>, in the chosen order. Pure apart from its arguments: tests call it.</summary>
    public static IReadOnlyList<RosterRow> Rows(IEnumerable<RosterCharacter> characters, RosterKey? current, DateTimeOffset now, RechargeCalendar calendar, RosterSort sort)
    {
        var rows = characters.Select(character =>
        {
            var cap = RosterRules.OdyleCap(character);
            return new RosterRow(
                character,
                character.Key == current,
                cap,
                character.Odyle is { } odyle ? RosterRules.OdyleNow(odyle, cap, now, calendar) : null,
                character.Odyle is { } seen ? RosterRules.OdyleFullAt(seen, cap, calendar) : null,
                character.Nightmare is { } nightmare ? RosterRules.ContentNow(RosterRules.Nightmare, nightmare, now, calendar) : null,
                character.Ascension is { } ascension ? RosterRules.ContentNow(RosterRules.Ascension, ascension, now, calendar) : null);
        });
        return sort switch
        {
            RosterSort.FullSoonest => [.. rows.OrderBy(r => r.Odyle is null).ThenBy(r => r.Odyle is { Full: true } ? DateTimeOffset.MinValue : r.FullAt ?? DateTimeOffset.MaxValue).ThenBy(r => r.Character.Name, StringComparer.CurrentCultureIgnoreCase)],
            RosterSort.Name => [.. rows.OrderBy(r => r.Character.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.Character.Server, StringComparer.Ordinal)],
            RosterSort.LastSeen => [.. rows.OrderByDescending(r => r.Character.LastSeen)],
            _ => [.. rows.OrderByDescending(r => r.Odyle?.Value ?? -1).ThenByDescending(r => r.Character.Odyle?.Second ?? -1).ThenBy(r => r.Character.Name, StringComparer.CurrentCultureIgnoreCase)],
        };
    }

    private DateTimeOffset Now => Clock.GetUtcNow();

    private RechargeCalendar Calendar => RechargeCalendar.Default(TimeSpan.FromMinutes(Math.Round(Settings.Current.Alerts.ServerOffsetHours * 60)));

    private string Name(RosterCharacter character) =>
        Settings.Current.Appearance.HideNicknames ? NicknameMask.Mask(character.Name) : character.Name;

    private bool IsArmed(RosterKey key) => armedRemoval is { } armed && armed.Key == key && Now - armed.At < RemoveConfirmWindow;

    // First click arms the removal, a second within the window removes; a click after it arms again.
    private void ClickRemove(RosterKey key)
    {
        if (IsArmed(key))
        {
            armedRemoval = null;
            Roster.Remove(key);
            return;
        }
        armedRemoval = (key, Now);
    }

    private void OnSortChanged(ChangeEventArgs e)
    {
        if (Enum.TryParse<RosterSort>(e.Value?.ToString(), out var chosen) && Enum.IsDefined(chosen)) sort = chosen;
    }

    private void OnCapChanged(RosterKey key, ChangeEventArgs e)
    {
        if (Enum.TryParse<OdyleCapChoice>(e.Value?.ToString(), out var choice) && Enum.IsDefined(choice)) Roster.SetOdyleCap(key, choice);
    }

    private void OnRosterChanged(object? sender, EventArgs e) => _ = InvokeAsync(StateHasChanged);

    private void OnSettingsChanged(object? sender, MeterSettings changed) => _ = InvokeAsync(StateHasChanged);

    // Re-renders for the estimates; a failure is logged and the loop goes on, cancellation from disposal ends it.
    private async Task RefreshLoopAsync()
    {
        using var timer = new PeriodicTimer(RefreshInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stopping.Token))
            {
                try
                {
                    await InvokeAsync(StateHasChanged);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Logger.LogWarning(ex, "Refreshing the character roster failed.");
                }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
        }
    }

    public void Dispose()
    {
        Roster.Changed -= OnRosterChanged;
        Settings.Changed -= OnSettingsChanged;
        stopping.Cancel();
        GC.SuppressFinalize(this);
    }
}
