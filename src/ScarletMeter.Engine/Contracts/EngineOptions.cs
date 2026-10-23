namespace ScarletMeter.Engine;

/// <summary>How packets reach the engine.</summary>
public enum CaptureMode
{
    /// <summary>Live capture through Npcap on this machine's adapters.</summary>
    Live,

    /// <summary>No capture at all: for UI work, tests and machines without Npcap.</summary>
    None,
}

/// <summary>
/// Host policy the engine takes from the application: where its data lives and how it captures. Paths are
/// absolute and derived from the platform's per-user data directory, never from the working directory, so
/// the same build behaves the same for anyone on any machine (AGENTS.md, "Paths").
/// </summary>
public sealed class EngineOptions
{
    /// <summary>The per-user data directory, %LocalAppData%\ScarletMeter on Windows.</summary>
    public static string DefaultDataDirectory
    {
        get
        {
            var root = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
            if (!Path.IsPathFullyQualified(root))
                throw new InvalidOperationException("The platform did not provide a per-user data directory.");
            return Path.Combine(root, "ScarletMeter");
        }
    }

    private string dataDirectory = DefaultDataDirectory;

    /// <summary>Absolute directory for the history database and logs. A relative path is rejected.</summary>
    public string DataDirectory
    {
        get => dataDirectory;
        set => dataDirectory = Path.IsPathFullyQualified(value)
            ? value
            : throw new ArgumentException("DataDirectory must be an absolute path.", nameof(value));
    }

    /// <summary>SQLite file in which upstream's history store keeps completed boss fights.</summary>
    public string HistoryDatabasePath => Path.Combine(DataDirectory, "combat-history.db");

    public CaptureMode Capture { get; set; } = CaptureMode.Live;
}
