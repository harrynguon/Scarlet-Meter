using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using ScarletMeter.App.Formatting;
using ScarletMeter.App.Settings;
using ScarletMeter.App.Windows;
using ScarletMeter.Engine;

namespace ScarletMeter.App.Pages;

/// <summary>
/// One player's breakdown: header stats, skills, buffs, damage graph and combat log. A live subject is read
/// from the engine twice a second (off the UI thread: building a breakdown walks every hit); a saved fight is
/// loaded once. When a live player drops out of the active fight the last breakdown stays, marked as stale.
/// </summary>
public partial class PlayerDetailsPage : IAsyncDisposable
{
    private static readonly string[] Tabs = ["Skills", "Buffs", "Graph", "Log"];
    private static readonly TimeSpan LiveRefresh = TimeSpan.FromMilliseconds(500);

    private readonly CancellationTokenSource stopping = new();
    private Task? refreshLoop;
    private DetailsSource? source;
    private HistoryFight? fight;
    private PlayerDetails? details;
    private ChartData? chart;
    private string current = Tabs[0];
    private bool stale;
    private bool icons = true;
    private bool hideNames;
    private bool showDeaths = true;
    private bool loadFailed;

    [Inject] private ICombatEngine Engine { get; set; } = default!;

    [Inject] private DetailsSelection Selection { get; set; } = default!;

    [Inject] private SettingsService Settings { get; set; } = default!;

    [Inject] private ILogger<PlayerDetailsPage> Logger { get; set; } = default!;

    private string EmptyText => loadFailed ? "This fight could not be loaded. The log has the details."
        : source is null ? "Click a player in the meter or in a saved fight."
        : source.IsLive ? "Waiting for this player's hits..."
        : "This player's details are missing from the saved fight, or could not be read.";

    private string SourceLabel => source?.IsLive != false
        ? "Live"
        : fight is null ? "Saved fight" : $"{fight.Entry.TargetName} · {DetailFormat.When(fight.Entry.Start)}";

    protected override void OnInitialized()
    {
        ApplySettings(Settings.Current);
        Settings.Changed += OnSettingsChanged;
        Selection.Changed += OnSelectionChanged;
        source = Selection.Current;
        refreshLoop = RefreshLoopAsync();
    }

    private string Name(PlayerRow player) => hideNames ? NicknameMask.Mask(player.Name) : player.Name;

    private async Task RefreshLoopAsync()
    {
        using var timer = new PeriodicTimer(LiveRefresh);
        try
        {
            await LoadAsync();
            while (await timer.WaitForNextTickAsync(stopping.Token))
            {
                if (source?.IsLive == true) await LoadAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Disposing.
        }
    }

    // Loads the subject; a selection that changed while loading wins (the result is dropped).
    private async Task LoadAsync()
    {
        var subject = source;
        if (subject is null) return;
        try
        {
            PlayerDetails? loaded;
            if (subject.IsLive)
            {
                loaded = await Task.Run(() => Engine.GetPlayerDetails(subject.PlayerId), stopping.Token);
                if (subject != source) return;
                stale = loaded is null && details is not null;
                if (loaded is not null) SetDetails(loaded);
            }
            else
            {
                // Loaded into a local first: a slower load for an earlier selection must not label this one.
                var saved = fight?.Entry.FightId == subject.FightId
                    ? fight
                    : await Task.Run(() => Engine.GetHistoryFight(subject.FightId!.Value), stopping.Token);
                if (subject != source) return;
                fight = saved;
                loadFailed = fight is null; // the engine logs and returns null for a saved fight it cannot read
                stale = false;
                loaded = fight?.Details.GetValueOrDefault(subject.PlayerId);
                if (loaded is not null) SetDetails(loaded);
                else details = null;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "Loading player details failed.");
            if (!subject.IsLive) loadFailed = true;
        }
        await RenderAsync();
    }

    private void SetDetails(PlayerDetails loaded)
    {
        // Each read builds new records, so the chart is rebuilt only when the graph's shape moved on.
        var previous = details?.Graph;
        if (chart is null || previous is null || previous.TotalSeconds != loaded.Graph.TotalSeconds
            || previous.Points.Count != loaded.Graph.Points.Count || previous.Buffs.Count != loaded.Graph.Buffs.Count)
            chart = DetailFormat.Chart(loaded.Graph);
        details = loaded;
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        source = Selection.Current;
        details = null;
        chart = null;
        stale = false;
        loadFailed = false;
        _ = LoadAsync();
    }

    private void OnSettingsChanged(object? sender, MeterSettings changed)
    {
        ApplySettings(changed);
        _ = RenderAsync();
    }

    private void ApplySettings(MeterSettings settings)
    {
        icons = settings.Appearance.SkillIcons;
        hideNames = settings.Appearance.HideNicknames;
        showDeaths = settings.Appearance.ShowDeaths;
    }

    private async Task RenderAsync()
    {
        if (stopping.IsCancellationRequested) return;
        try
        {
            await InvokeAsync(StateHasChanged);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        Settings.Changed -= OnSettingsChanged;
        Selection.Changed -= OnSelectionChanged;
        await stopping.CancelAsync();
        if (refreshLoop is not null) await refreshLoop;
        stopping.Dispose();
    }
}
