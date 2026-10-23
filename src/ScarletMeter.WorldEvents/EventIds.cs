namespace ScarletMeter.WorldEvents;

/// <summary>
/// The nine catalog ids, the same strings the harrynguon/AIon2-Dps-Meter fork's World Companion uses, so a host
/// can name rules (for example in its set of enabled alerts) without repeating string literals.
/// </summary>
public static class EventIds
{
    /// <summary>Watcher Kaira, Abandoned Island.</summary>
    public const string Kaira = "kaira";

    /// <summary>Guardian Lord Nahma, Reshanta's Root.</summary>
    public const string Nahma = "nahma";

    /// <summary>Executors Tamasa and Argo.</summary>
    public const string Executors = "executors";

    /// <summary>Spacetime Rift.</summary>
    public const string Rift = "rift";

    /// <summary>Artifact Siege.</summary>
    public const string Siege = "siege";

    /// <summary>Dimensional Invasion.</summary>
    public const string Invasion = "invasion";

    /// <summary>Shugo Festival.</summary>
    public const string Shugo = "shugo";

    /// <summary>Daily reset.</summary>
    public const string Daily = "daily";

    /// <summary>Weekly (Wednesday) reset.</summary>
    public const string Weekly = "weekly";
}
