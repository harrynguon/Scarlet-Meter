using System.IO;
using Microsoft.Extensions.Logging;
using ScarletMeter.Engine;

namespace ScarletMeter.App.Settings;

/// <summary>
/// The live settings: loaded once at startup, changed through <see cref="Update"/>, applied at once (the
/// engine's fight rules are mirrored on every change) and written to disk shortly after the last change,
/// so dragging a slider does not rewrite the file for every step. <see cref="Flush"/> writes any pending
/// change immediately; the app calls it on exit. The write happens outside the lock that <see cref="Current"/> and
/// <see cref="Update"/> take: the overlay reads the settings on every render (the update notice's switch,
/// UpdateNotice.Visible), the UI thread updates them (the overlay's placement after each fit), and the settings and roster
/// pages read them as they render, and a save (written through, flushed and replaced) must not stall them.
/// </summary>
public sealed class SettingsService : IDisposable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly SettingsStore store;
    private readonly EngineSettings engineSettings;
    private readonly ILogger<SettingsService> logger;
    private readonly Lock gate = new();
    // Held for a whole save, so saves happen one at a time, in order, and a Flush returns only once every change made
    // before it is written (Dispose's, on exit, waits out a save under way). Taken before `gate`, never inside it.
    private readonly Lock saveGate = new();
    private readonly Action<MeterSettings> save;
    private readonly Timer saveTimer;
    private MeterSettings current;
    private bool dirty;
    private bool disposed;

    public SettingsService(SettingsStore store, EngineSettings engineSettings, ILogger<SettingsService> logger)
        : this(store, engineSettings, logger, null)
    {
    }

    // For tests: `save` stands in for the store's write (to hold a save while the settings are read and changed); null
    // writes to `store`. Internal, so dependency injection never picks it.
    internal SettingsService(SettingsStore store, EngineSettings engineSettings, ILogger<SettingsService> logger, Action<MeterSettings>? save)
    {
        this.store = store;
        this.save = save ?? store.Save;
        this.engineSettings = engineSettings;
        this.logger = logger;
        LoadResult = store.Load();
        current = LoadResult.Settings;
        if (LoadResult.Notice is not null) logger.LogWarning("Settings: {Notice}", LoadResult.Notice);
        ApplyToEngine(current.Fight);
        saveTimer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>How the settings were loaded, for the settings page's notice.</summary>
    public SettingsLoadResult LoadResult { get; }

    /// <summary>True unless the file must not be overwritten (see <see cref="SettingsLoadResult.CanSave"/>).</summary>
    public bool CanSave => LoadResult.CanSave;

    /// <summary>The last save error shown to the player, or null after a successful save.</summary>
    public string? SaveError { get; private set; }

    public MeterSettings Current
    {
        get { lock (gate) return current; }
    }

    /// <summary>Raised after every applied change, on the thread that made it (the UI thread in this app).</summary>
    public event EventHandler<MeterSettings>? Changed;

    /// <summary>
    /// Applies a change. The result is normalized, so callers cannot store an out-of-range value. <paramref name="change"/>
    /// runs under the service's lock and must not call back into the service (a Flush there would take the save's lock
    /// inside it, against the order <see cref="Flush"/> takes them in).
    /// </summary>
    public void Update(Func<MeterSettings, MeterSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        MeterSettings next;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            next = SettingsNormalizer.Normalize(change(current)).Settings;
            if (next == current) return;
            current = next;
            dirty = true;
            if (CanSave) saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        }
        ApplyToEngine(next.Fight);
        Changed?.Invoke(this, next);
    }

    /// <summary>Raised when <see cref="SaveError"/> changes, usually on the save timer's thread.</summary>
    public event EventHandler? SaveStatusChanged;

    /// <summary>
    /// Writes a pending change now. Failures are logged and reported through <see cref="SaveError"/>, and the change stays
    /// pending, so the next save tries again. The settings are taken under the lock and written outside it; a change made
    /// meanwhile stays pending for the next save, as one made after a save does.
    /// </summary>
    public void Flush()
    {
        string? before, after;
        lock (saveGate)
        {
            MeterSettings pending;
            lock (gate)
            {
                if (!dirty || !CanSave) return;
                pending = current;
                dirty = false;
                before = SaveError;
            }
            var saved = false;
            try
            {
                save(pending);
                saved = true;
                SaveError = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogError(ex, "Saving settings to {Path} failed.", store.FilePath);
                SaveError = $"Settings could not be saved: {ex.Message}";
            }
            finally
            {
                // Any failure, an unexpected one included, leaves the change pending, as when it was written under the lock.
                if (!saved) lock (gate) dirty = true;
            }
            after = SaveError;
        }
        if (before != after) SaveStatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
        }
        saveTimer.Dispose();
        Flush();
    }

    private void ApplyToEngine(FightSettings fight)
    {
        engineSettings.BossOnlyCapture = fight.BossOnlyCapture;
        engineSettings.GroupSummonDamage = fight.GroupSummonDamage;
        engineSettings.OneMinuteDummyMode = fight.OneMinuteDummyMode;
        engineSettings.HistoryRetentionDays = fight.HistoryRetentionDays;
        engineSettings.PauseInAbyss = fight.PauseInAbyss;
    }
}
