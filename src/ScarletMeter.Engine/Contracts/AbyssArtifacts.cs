namespace ScarletMeter.Engine;

/// <summary>Who holds an Abyss artifact, as the game says; also a side, as a character's server makes it (<see cref="AbyssArtifacts.SideOf"/>).</summary>
public enum ArtifactHolder
{
    /// <summary>Not known: the game has not said, or said a value the meter cannot name.</summary>
    Unknown,
    /// <summary>Nobody holds it.</summary>
    Nobody,
    /// <summary>The Elyos.</summary>
    Elyos,
    /// <summary>The Asmodians.</summary>
    Asmodian,
}

/// <summary>
/// One layer's Artifact Siege: the game's AbyssArtifactEvent id (<see cref="AbyssArtifacts.LowerLayer"/> or 2001, the
/// upper layer) and, while a side holds the layer's artifacts, when the holding began and when it resets (the next
/// siege); null when the game sent none.
/// </summary>
public readonly record struct ArtifactWar(int EventId, DateTimeOffset? Since, DateTimeOffset? Until);

/// <summary>One artifact: the game's AbyssArtifact id (1001 to 1003 the lower layer's, 2001 to 2003 the upper's) and who holds it.</summary>
public readonly record struct AbyssArtifact(int Id, ArtifactHolder Holder);

/// <summary>
/// Who holds the Abyss's artifacts, as the game told the player when they opened the Abyss map (both layers) or entered
/// the Abyss (the layer entered), and when it was read (<see cref="ICombatEngine.ArtifactsObserved"/>). It is the
/// world's, not a character's: the same for every character on the server it was read on. The lower layer's three
/// artifacts are the Artifact Corridors' (A2Tools: Reshanta's Root, Sulfur Tree Island and the Archipelago of Siel's
/// Wings); a side can run the corridors of those it holds.
/// </summary>
public sealed record AbyssArtifacts(IReadOnlyList<ArtifactWar> Wars, IReadOnlyList<AbyssArtifact> Artifacts, DateTimeOffset SeenAt)
{
    /// <summary>The lower layer's AbyssArtifactEvent id, which is also its first artifact's id.</summary>
    public const int LowerLayer = 1001;

    /// <summary>How many artifacts, and so Artifact Corridors, the lower layer has: ids 1001 to 1003.</summary>
    public const int Corridors = 3;

    /// <summary>Who holds an artifact, or <see cref="ArtifactHolder.Unknown"/> when this reading does not list it (the last listing wins).</summary>
    public ArtifactHolder HolderOf(int id)
    {
        var holder = ArtifactHolder.Unknown;
        foreach (var artifact in Artifacts)
            if (artifact.Id == id) holder = artifact.Holder;
        return holder;
    }

    /// <summary>A layer's war, or null when this reading does not carry it (the last one wins).</summary>
    public ArtifactWar? War(int eventId)
    {
        ArtifactWar? war = null;
        foreach (var each in Wars)
            if (each.EventId == eventId) war = each;
        return war;
    }

    /// <summary>
    /// The side of a server, by its id as the game numbers servers (upstream's ServerMap): 1001 and up Elyos, 2001 and up
    /// Asmodian, as A2Tools reads them; <see cref="ArtifactHolder.Unknown"/> for anything else, 0 (not known) among them.
    /// </summary>
    public static ArtifactHolder SideOf(int serverId) => serverId switch
    {
        > 1000 and < 2000 => ArtifactHolder.Elyos,
        > 2000 and < 3000 => ArtifactHolder.Asmodian,
        _ => ArtifactHolder.Unknown,
    };
}
