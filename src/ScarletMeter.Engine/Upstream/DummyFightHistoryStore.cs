using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.Services.Services.Session.Persistence;
using Microsoft.Extensions.Logging;

namespace ScarletMeter.Engine.Upstream;

/// <summary>
/// Upstream's history store, except that a fight <see cref="DummyFightFilter"/> leaves out (a training dummy fight
/// the player did not hit) is not saved. Upstream's session manager saves every finished boss fight, and counts every
/// dummy as a boss, through <see cref="ICombatHistoryStore.Save"/>; <c>AddScarletEngine</c> registers this store in
/// place of upstream's, wrapping the store upstream registered, so that call reaches it. Every other call passes
/// through unchanged.
/// </summary>
internal sealed class DummyFightHistoryStore(
    ICombatHistoryStore inner, DummyFightFilter dummies, ILogger<DummyFightHistoryStore> logger)
    : ICombatHistoryStore, IDisposable
{
    /// <summary>The store this one wraps, for the registration test.</summary>
    internal ICombatHistoryStore Inner => inner;

    public void Save(HistorySessionSnapshot snapshot)
    {
        if (dummies.Keeps(snapshot))
        {
            inner.Save(snapshot);
            return;
        }
        // The target's name is game data; no player is named.
        logger.LogInformation(
            "Fight {FightId} against {Target} was not saved: a training dummy the player did not hit.",
            snapshot.SessionId, snapshot.TargetName);
    }

    public void FlushPendingSaves() => inner.FlushPendingSaves();

    public IReadOnlyList<HistorySessionListItem> GetSessionList() => inner.GetSessionList();

    public int GetSessionCount(
        DateTime? dateFrom, DateTime? dateTo, string? bossNameContains, IReadOnlySet<Guid>? excludeSessionIds = null) =>
        inner.GetSessionCount(dateFrom, dateTo, bossNameContains, excludeSessionIds);

    public IReadOnlyList<HistorySessionListItem> GetSessionPage(
        DateTime? dateFrom, DateTime? dateTo, string? bossNameContains, int skip, int take,
        IReadOnlySet<Guid>? excludeSessionIds = null) =>
        inner.GetSessionPage(dateFrom, dateTo, bossNameContains, skip, take, excludeSessionIds);

    public HistorySessionSnapshot? GetSession(Guid sessionId) => inner.GetSession(sessionId);

    // The container disposes this store, not the one it wraps; upstream's holds nothing to dispose today.
    public void Dispose() => (inner as IDisposable)?.Dispose();
}
