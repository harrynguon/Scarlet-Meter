using Microsoft.Data.Sqlite;

namespace ScarletMeter.Tests;

/// <summary>
/// The root Directory.Build.props overlays upstream's Services project with a fixed SQLite bundle
/// (GHSA-2m69-gcr7-jv3q). This checks that the native library actually loaded is the fixed one.
/// </summary>
public sealed class SqliteRuntimeTests
{
    [Fact]
    public void LoadedNativeSqliteIncludesCve20256965Fix()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "select sqlite_version()";
        var version = Version.Parse((string)command.ExecuteScalar()!);
        Assert.True(version >= new Version(3, 50, 2), $"Loaded SQLite {version}; 3.50.2 or later carries the CVE-2025-6965 fix.");
    }
}
