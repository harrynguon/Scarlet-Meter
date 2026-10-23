using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using ScarletMeter.App.Formatting;
using ScarletMeter.App.Settings;
using ScarletMeter.App.Windows;
using ScarletMeter.Engine;

namespace ScarletMeter.App.Pages;

/// <summary>
/// Saved boss fights: a filtered, paged list (newest first) and, for a chosen fight, its party with RATmeter's
/// Copy DPS line; a player opens in the details window. Queries run off the UI thread, because a saved fight
/// is decompressed from the history database.
/// </summary>
public partial class FightHistoryPage : IDisposable
{
    private const int PageSize = 50;

    private HistoryPage? results;
    private HistoryFight? fight;
    private string nameFilter = string.Empty;
    private DateTime? fromFilter;
    private DateTime? toFilter;
    private string? error;
    private bool copied;
    private bool hideNames;

    [Inject] private ICombatEngine Engine { get; set; } = default!;

    [Inject] private IMeterWindows Windows { get; set; } = default!;

    [Inject] private SettingsService Settings { get; set; } = default!;

    [Inject] private ILogger<FightHistoryPage> Logger { get; set; } = default!;

    private bool Filtered => nameFilter.Length > 0 || fromFilter is not null || toFilter is not null;

    private int PageCount => results is null ? 1 : Math.Max(1, (results.TotalCount + results.PageSize - 1) / results.PageSize);

    // Upstream's rule, as in the overlay: unnamed players appear once they hold over one percent of the damage.
    private IEnumerable<PlayerRow> VisiblePlayers => (fight?.Players ?? [])
        .Where(p => p.IsIdentified || p.DamageShare > 1)
        .OrderByDescending(p => p.TotalDamage);

    protected override async Task OnInitializedAsync()
    {
        hideNames = Settings.Current.Appearance.HideNicknames;
        Settings.Changed += OnSettingsChanged;
        await SearchAsync(1);
    }

    private string Name(PlayerRow player) => hideNames ? NicknameMask.Mask(player.Name) : player.Name;

    private async Task SearchAsync(int pageNumber)
    {
        error = null;
        try
        {
            // The "To" date covers that whole day.
            var query = new HistoryQuery(nameFilter, fromFilter, toFilter?.Date.AddDays(1).AddTicks(-1), pageNumber, PageSize).Validate();
            results = await Task.Run(() => Engine.GetHistory(query));
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Loading the history failed.");
            error = "The history could not be loaded. The log has the details.";
        }
    }

    private async Task ResetFiltersAsync()
    {
        nameFilter = string.Empty;
        fromFilter = toFilter = null;
        await SearchAsync(1);
    }

    private async Task OnFilterKey(KeyboardEventArgs e)
    {
        if (e.Key == "Enter") await SearchAsync(1);
    }

    private async Task OpenFightAsync(Guid fightId)
    {
        error = null;
        copied = false;
        try
        {
            fight = await Task.Run(() => Engine.GetHistoryFight(fightId));
            // Null: removed by retention, or a saved row the engine could not read (it logs which).
            if (fight is null) error = "That fight is no longer in the history, or could not be read. The log has the details.";
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Loading a saved fight failed.");
            error = "That fight could not be loaded. The log has the details.";
        }
    }

    private void CloseFight()
    {
        fight = null;
        copied = false;
    }

    private void CopyDpsLine()
    {
        if (fight is null) return;
        Windows.CopyText(CopyDps.Format(fight.Players, Name));
        copied = true;
    }

    private static string DateText(DateTime? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    private static DateTime? ParseDate(object? value) =>
        DateTime.TryParseExact(value?.ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date)
            ? DateTime.SpecifyKind(date, DateTimeKind.Local)
            : null;

    private void OnSettingsChanged(object? sender, MeterSettings changed)
    {
        hideNames = changed.Appearance.HideNicknames;
        _ = InvokeAsync(StateHasChanged);
    }

    public void Dispose() => Settings.Changed -= OnSettingsChanged;
}
