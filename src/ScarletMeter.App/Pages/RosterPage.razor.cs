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
    /// <summary>Most Kina, Kina and Kina (Bound) together, first.</summary>
    MostKina,
    /// <summary>By name.</summary>
    Name,
    /// <summary>Played most recently first.</summary>
    LastSeen,
}

/// <summary>
/// One character as the page shows it at an instant: its estimates, when its energy is full, its Artifact Corridor
/// entries since the last Abyss Siege, and how many corridors its side can enter (null while not known).
/// </summary>
public sealed record RosterRow(
    RosterCharacter Character, bool IsCurrent, int Cap, Estimate? Odyle, DateTimeOffset? FullAt, Estimate? Nightmare, Estimate? Ascension,
    int Corridors, int? CorridorsOpen = null)
{
    public RosterKey Key => Character.Key;
}

/// <summary>
/// The character roster: every character the meter has seen the player log in on, with Odyle energy, when basic energy
/// is full again, and Nightmare and Ascension Trial entries left, each as the game last said plus the recharges since
/// (<see cref="RosterRules"/>, marked ≈), the Artifact Corridors entered since the last Abyss Siege, as the meter saw
/// them, out of those its side can enter, the Kina it holds, as its inventory last said, with the totals across
/// characters, and the player's note on it. Re-reads the roster when it changes and every half minute, for the estimates.
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

    /// <summary>
    /// The rows at <paramref name="now"/>, in the chosen order, with the artifact holders by server id for the corridors
    /// each character's side can enter. Pure apart from its arguments: tests call it.
    /// </summary>
    public static IReadOnlyList<RosterRow> Rows(
        IEnumerable<RosterCharacter> characters, RosterKey? current, DateTimeOffset now, RechargeCalendar calendar, RosterSort sort,
        IReadOnlyDictionary<int, RosterArtifacts>? artifacts = null)
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
                character.Ascension is { } ascension ? RosterRules.ContentNow(RosterRules.Ascension, ascension, now, calendar) : null,
                RosterRules.CorridorRunsSinceSiege(character.CorridorRuns, now, calendar),
                RosterRules.CorridorsOpen(character.ServerId, artifacts?.GetValueOrDefault(character.ServerId), now, calendar));
        });
        return sort switch
        {
            RosterSort.FullSoonest => [.. rows.OrderBy(r => r.Odyle is null).ThenBy(r => r.Odyle is { Full: true } ? DateTimeOffset.MinValue : r.FullAt ?? DateTimeOffset.MaxValue).ThenBy(r => r.Character.Name, StringComparer.CurrentCultureIgnoreCase)],
            RosterSort.MostKina => [.. rows.OrderByDescending(r => r.Character.Kina is { } kina ? (kina.Kina ?? 0) + (kina.Bound ?? 0) : -1).ThenBy(r => r.Character.Name, StringComparer.CurrentCultureIgnoreCase)],
            RosterSort.Name => [.. rows.OrderBy(r => r.Character.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.Character.Server, StringComparer.Ordinal)],
            RosterSort.LastSeen => [.. rows.OrderByDescending(r => r.Character.LastSeen)],
            _ => [.. rows.OrderByDescending(r => r.Odyle?.Value ?? -1).ThenByDescending(r => r.Character.Odyle?.Second ?? -1).ThenBy(r => r.Character.Name, StringComparer.CurrentCultureIgnoreCase)],
        };
    }

    /// <summary>
    /// The Kina across the rows: each item summed over the characters that know it, and how many characters know neither
    /// (shown as not known, rather than counted as none).
    /// </summary>
    public static (long Kina, long Bound, int Unknown) KinaTotals(IReadOnlyList<RosterRow> rows) => (
        rows.Sum(row => row.Character.Kina?.Kina ?? 0),
        rows.Sum(row => row.Character.Kina?.Bound ?? 0),
        rows.Count(row => row.Character.Kina is null or { Kina: null, Bound: null }));

    private DateTimeOffset Now => Clock.GetUtcNow();

    /// <summary>What the corridors cell says when pointed at: the runs out of what, and where that comes from.</summary>
    public static string CorridorsTitle(RosterRow row) => row.CorridorsOpen switch
    {
        null => $"Artifact Corridors entered since the last Abyss Siege, out of {RosterRules.CorridorsPerSiege}: which artifacts your side holds is not known on this server since the siege. Open the Abyss map in the game on this character to read it.",
        0 => "Your side holds none of the corridors' artifacts until the next Abyss Siege, as the game last said on this server.",
        { } open => $"Artifact Corridors entered since the last Abyss Siege, out of the {open} whose artifact your side holds, as the game last said on this server.",
    };

    /// <summary>
    /// What a "?" the game has not filled yet says when pointed at (the gear score, the bound Kina): both usually come at
    /// login, so a character played only since the meter started mid-session has neither until its next login. "Usually":
    /// a gear score also waits on a server id that checked out, and a Kina listing read long before the login's list is
    /// not taken (deep audit on #58).
    /// </summary>
    public const string NotReadYet = "not read yet: the game usually sends it when you log in on this character, so log in on it with the meter running";

    /// <summary>What the gear score says when pointed at: what it is, and why it is "?" while not read.</summary>
    public static string GearScoreTitle(RosterCharacter character) =>
        "Gear score (the equipment's item level)" + (character.GearScore > 0 ? string.Empty : ": " + NotReadYet);

    /// <summary>What the bound Kina says when pointed at while it is "?"; null (no tip) once read.</summary>
    public static string? BoundKinaTitle(RosterCharacter character) =>
        character.Kina?.Bound is null ? "Kina (Bound): " + NotReadYet : null;

    /// <summary>
    /// The cap list's choice for a character: 560 when the player chose it, else the default 840, which a saved choice of
    /// 840 (<see cref="OdyleCapChoice.Membership"/>, from before 840 was the default) shows as too; it stays saved until changed.
    /// </summary>
    public static OdyleCapChoice CapShown(RosterCharacter character) =>
        character.OdyleCap == OdyleCapChoice.Standard ? OdyleCapChoice.Standard : OdyleCapChoice.Automatic;

    /// <summary>What the note box says when pointed at: the note whole (a long one is cut in its box), or how to write one.</summary>
    public static string NoteTitle(RosterCharacter character) =>
        character.Note.Length > 0 ? character.Note : "Your note on this character, kept on this PC; Enter or leaving the box saves it";

    private RechargeCalendar Calendar => RechargeCalendar.ForServerOffsetHours(Settings.Current.Alerts.ServerOffsetHours);

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

    // The note is cleaned and kept by the service. The box shows the kept note at the next render when it changed; when the
    // cleaned note is the one already kept (only spaces added at an end), the box keeps what was typed until the window opens again.
    private void OnNoteChanged(RosterKey key, ChangeEventArgs e) => Roster.SetNote(key, e.Value?.ToString());

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
