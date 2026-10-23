using System.IO;
using Microsoft.Data.Sqlite;

namespace ScarletMeter.Tests;

/// <summary>
/// A unique, absolute data directory under the system temp directory for one test, deleted on dispose.
/// Deletion retries briefly, because upstream's history store starts a background cleanup that can hold the
/// database for a moment, and then fails the test, so a leaked file handle surfaces instead of leaving the
/// directory behind.
/// </summary>
internal sealed class TestDataDirectory : IDisposable
{
    private static readonly TimeSpan DeleteTimeout = TimeSpan.FromSeconds(5);

    public string FullPath { get; } = Path.Combine(Path.GetTempPath(), "scarlet-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        var deadline = DateTime.UtcNow + DeleteTimeout;
        while (true)
        {
            // Microsoft.Data.Sqlite pools connections; release them so the database file can be deleted.
            SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(FullPath)) Directory.Delete(FullPath, recursive: true);
                return;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(100);
            }
        }
    }
}
