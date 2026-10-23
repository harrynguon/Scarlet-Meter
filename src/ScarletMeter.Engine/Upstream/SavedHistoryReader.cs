using System.Globalization;
using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.Services.Services.Session.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ScarletMeter.Engine.Upstream;

/// <summary>
/// Reads the saved history list without trusting its columns, for when upstream's own query fails on a damaged
/// row. Upstream's store parses each row's dates while it materialises the page, so one unparsable value fails
/// the whole list. Here every list column is read as text and parsed in C#: rows that do not parse are skipped
/// and counted, and the others are filtered, ordered and paged as upstream does (name contained, any case; end
/// time within the range, both bounds inclusive; newest end first).
/// </summary>
/// <remarks>
/// Only the list columns are read, never the per-player JSON, so this stays cheap enough to run over the whole
/// table. It reads upstream's schema (table CombatSessions, its entity's column names) directly; a schema change
/// shows as HistoryTests' damaged-row test failing. Running fights are not included: they live in upstream's
/// session manager, whose list is the query that failed.
/// </remarks>
internal sealed class SavedHistoryReader(IDbContextFactory<CombatHistoryDbContext> contexts)
{
    private const string ListQuery =
        "SELECT CAST(SessionId AS TEXT) AS SessionId, CAST(TargetId AS TEXT) AS TargetId, " +
        "CAST(TargetName AS TEXT) AS TargetName, CAST(TargetHpTotal AS TEXT) AS TargetHpTotal, " +
        "CAST(SessionStart AS TEXT) AS SessionStart, CAST(SessionEnd AS TEXT) AS SessionEnd, " +
        "CAST(State AS TEXT) AS State, CAST(TotalDamage AS TEXT) AS TotalDamage, " +
        "CAST(PlayerCount AS TEXT) AS PlayerCount FROM CombatSessions";

    /// <summary>The page the query asks for among the rows that parse, how many such rows match, and how many rows were skipped.</summary>
    public (IReadOnlyList<HistorySessionListItem> Items, int TotalCount, int Skipped) ReadPage(HistoryQuery query)
    {
        using var db = contexts.CreateDbContext();
        var rows = db.Database.SqlQueryRaw<RawRow>(ListQuery).ToList();

        var items = new List<HistorySessionListItem>(rows.Count);
        foreach (var row in rows)
        {
            if (TryParse(row) is { } item) items.Add(item);
        }

        var name = query.TargetNameContains;
        var matching = items
            .Where(item => name is null || item.TargetName.Contains(name, StringComparison.OrdinalIgnoreCase))
            .Where(item => query.From is not { } from || item.SessionEnd >= from)
            .Where(item => query.To is not { } to || item.SessionEnd <= to)
            .OrderByDescending(item => item.SessionEnd)
            .ToList();
        // HistoryQuery.Validate bounds the page, so the skip stays inside int.
        var page = matching.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList();
        return (page, matching.Count, rows.Count - items.Count);
    }

    // A row whose every column parses as upstream writes it, or null.
    private static HistorySessionListItem? TryParse(RawRow row)
    {
        var invariant = CultureInfo.InvariantCulture;
        if (!Guid.TryParse(row.SessionId, out var id)
            || !int.TryParse(row.TargetId, NumberStyles.Integer, invariant, out var targetId)
            || !long.TryParse(row.TargetHpTotal, NumberStyles.Integer, invariant, out var hpTotal)
            || !DateTime.TryParse(row.SessionStart, invariant, DateTimeStyles.None, out var start)
            || !DateTime.TryParse(row.SessionEnd, invariant, DateTimeStyles.None, out var end)
            || !int.TryParse(row.State, NumberStyles.Integer, invariant, out var state)
            || !Enum.IsDefined((SessionState)state)
            || !long.TryParse(row.TotalDamage, NumberStyles.Integer, invariant, out var totalDamage)
            || !int.TryParse(row.PlayerCount, NumberStyles.Integer, invariant, out var playerCount))
            return null;

        return new HistorySessionListItem
        {
            SessionId = id,
            TargetId = targetId,
            TargetName = row.TargetName ?? string.Empty,
            TargetHpTotal = hpTotal,
            SessionStart = start,
            SessionEnd = end,
            State = (SessionState)state,
            TotalDamage = totalDamage,
            PlayerCount = playerCount,
        };
    }

    /// <summary>One row's list columns, as text.</summary>
    private sealed class RawRow
    {
        public string? SessionId { get; set; }
        public string? TargetId { get; set; }
        public string? TargetName { get; set; }
        public string? TargetHpTotal { get; set; }
        public string? SessionStart { get; set; }
        public string? SessionEnd { get; set; }
        public string? State { get; set; }
        public string? TotalDamage { get; set; }
        public string? PlayerCount { get; set; }
    }
}
