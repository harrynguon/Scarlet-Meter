using System.Data.Common;
using System.Globalization;
using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.Services.Services.Session.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ScarletMeter.Engine.Upstream;

/// <summary>
/// Reads the saved history list row by row, for when upstream's own query fails on a damaged row: its store
/// reads a whole page in one go, so one row that does not read fails every page it falls on. Here each row's
/// list columns are read with the same SQLite provider calls EF Core makes for upstream's store (GetGuid,
/// GetInt32, GetInt64, GetString, GetDateTime), so a row is skipped exactly when upstream could not read it,
/// and reads the same when it can: text in a number column reads as 0, and any State number is accepted, as
/// upstream reads them. The rows read are filtered, ordered and paged as upstream's query does, with two
/// differences that only rows upstream would not write can show: dates are compared as read rather than as the
/// stored text SQL compares, and so is the newest-first order.
/// </summary>
/// <remarks>
/// The name filter matches upstream's: the stored name lowered by SQLite's lower(), which folds ASCII letters
/// only, contains the search lowered in the current culture, compared ordinally. Only the list columns are
/// read, never the per-player JSON, so this stays cheap enough to run over the whole table. It reads upstream's
/// schema (table CombatSessions, its entity's column names) directly; a schema change shows as HistoryTests'
/// damaged-row tests failing. Running fights are not included: they live in upstream's session manager, whose
/// list is the query that failed.
/// </remarks>
internal sealed class SavedHistoryReader(IDbContextFactory<CombatHistoryDbContext> contexts)
{
    private const string ListQuery =
        "SELECT SessionId, TargetId, TargetName, TargetHpTotal, SessionStart, SessionEnd, State, TotalDamage, PlayerCount " +
        "FROM CombatSessions";

    /// <summary>The page the query asks for among the rows that read, how many such rows match, and how many rows were skipped.</summary>
    public (IReadOnlyList<HistorySessionListItem> Items, int TotalCount, int Skipped) ReadPage(HistoryQuery query)
    {
        var (items, skipped) = ReadAll();
        // Upstream lowers the search in C# (current culture) and the stored name in SQL (ASCII only).
        var search = query.TargetNameContains?.ToLower(CultureInfo.CurrentCulture);
        var matching = items
            .Where(item => search is null || AsciiLower(item.TargetName).Contains(search, StringComparison.Ordinal))
            .Where(item => query.From is not { } from || item.SessionEnd >= from)
            .Where(item => query.To is not { } to || item.SessionEnd <= to)
            .OrderByDescending(item => item.SessionEnd)
            .ToList();
        // HistoryQuery.Validate bounds the page, so the skip stays inside int.
        var page = matching.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList();
        return (page, matching.Count, skipped);
    }

    // Every row that reads, and how many did not. A failure to query at all (no table, a locked file) throws.
    private (List<HistorySessionListItem> Items, int Skipped) ReadAll()
    {
        using var db = contexts.CreateDbContext();
        db.Database.OpenConnection();
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = ListQuery;
        using var reader = command.ExecuteReader();

        var items = new List<HistorySessionListItem>();
        var skipped = 0;
        while (reader.Read())
        {
            if (TryRead(reader) is { } item) items.Add(item);
            else skipped++;
        }
        return (items, skipped);
    }

    // One row read as EF Core reads it for upstream's store, or null where that read throws.
    private static HistorySessionListItem? TryRead(DbDataReader row)
    {
        try
        {
            return new HistorySessionListItem
            {
                SessionId = row.GetGuid(0),
                TargetId = row.GetInt32(1),
                TargetName = row.GetString(2),
                TargetHpTotal = row.GetInt64(3),
                SessionStart = row.GetDateTime(4),
                SessionEnd = row.GetDateTime(5),
                State = (SessionState)row.GetInt32(6),
                TotalDamage = row.GetInt64(7),
                PlayerCount = row.GetInt32(8),
            };
        }
        catch (Exception)
        {
            // The provider throws a range of types for a value it cannot read (format, cast, overflow, null);
            // whichever it is, upstream's read of this row would have failed the same way.
            return null;
        }
    }

    // SQLite's built-in lower(): ASCII letters only, everything else unchanged.
    private static string AsciiLower(string value) =>
        string.Create(value.Length, value, static (chars, source) =>
        {
            for (var i = 0; i < source.Length; i++)
                chars[i] = source[i] is >= 'A' and <= 'Z' ? (char)(source[i] + 32) : source[i];
        });
}
