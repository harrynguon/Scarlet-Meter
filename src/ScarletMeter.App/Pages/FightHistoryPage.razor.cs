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
    private bool showDeaths = true;
    private int listRequest;
    private int fightRequest;

    // Cancelled when the window closes; not disposed, since a load's continuation may still check it.
    private readonly CancellationTokenSource stopping = new();

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
        showDeaths = Settings.Current.Appearance.ShowDeaths;
        Settings.Changed += OnSettingsChanged;
        await SearchAsync(1);
    }

    private string Name(PlayerRow player) => hideNames ? NicknameMask.Mask(player.Name) : player.Name;

    // Only the latest search applies: a slower, older one (Next clicked twice, Search during a slow query) is
    // dropped, as is any result arriving after the window closed. A page past the end, left by retention
    // shrinking the history since the page count was shown, is replaced by the last page.
    private async Task SearchAsync(int pageNumber)
    {
        error = null;
        var request = ++listRequest;
        try
        {
            // The "To" date covers that whole day.
            var query = new HistoryQuery(nameFilter, fromFilter, toFilter?.Date.AddDays(1).AddTicks(-1), pageNumber, PageSize).Validate();
            var page = await Task.Run(() => Engine.GetHistory(query), stopping.Token);
            if (request != listRequest || stopping.IsCancellationRequested) return;
            var last = Math.Max(1, (page.TotalCount + page.PageSize - 1) / page.PageSize);
            if (page.Entries.Count == 0 && page.Page > last)
            {
                await SearchAsync(last);
                return;
            }
            results = page;
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // Closing.
        }
        catch (ArgumentException ex)
        {
            if (request == listRequest) error = ex.Message;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Loading the history failed.");
            if (request == listRequest) error = "The history could not be loaded. The log has the details.";
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
        var request = ++fightRequest;
        try
        {
            var loaded = await Task.Run(() => Engine.GetHistoryFight(fightId), stopping.Token);
            // A later click, going back to the list, or closing the window wins over this late result.
            if (request != fightRequest || stopping.IsCancellationRequested) return;
            fight = loaded;
            // Null: removed by retention, or a saved row the engine could not read (it logs which).
            if (loaded is null) error = "That fight is no longer in the history, or could not be read. The log has the details.";
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // Closing.
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Loading a saved fight failed.");
            if (request == fightRequest) error = "That fight could not be loaded. The log has the details.";
        }
    }

    private void CloseFight()
    {
        fightRequest++; // a fight still loading must not reopen
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
        showDeaths = changed.Appearance.ShowDeaths;
        _ = InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        Settings.Changed -= OnSettingsChanged;
        stopping.Cancel(); // loads still running drop their results; the token is not read after this
    }
}
