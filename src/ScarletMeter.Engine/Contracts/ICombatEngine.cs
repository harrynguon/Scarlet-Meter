namespace ScarletMeter.Engine;

/// <summary>
/// Everything the app needs from combat: capture control, a snapshot of the current fight, per-player
/// details, boss-fight history and ping updates. The only implementation wraps upstream RATmeter's pipeline
/// (Upstream/UpstreamCombatEngine). Callers never see an upstream type, so an upstream change is contained in
/// the engine. Every method may be called from any thread.
/// </summary>
public interface ICombatEngine : IDisposable
{
    /// <summary>
    /// Discovers network adapters and starts listening for the game's traffic. An adapter that is found but
    /// fails to open is only logged by upstream, so Start can return with nothing listening;
    /// <see cref="CaptureState.AdapterName"/> then stays null. In <see cref="CaptureMode.Demo"/> it starts the
    /// synthetic feed instead; in <see cref="CaptureMode.None"/> it does nothing.
    /// </summary>
    /// <exception cref="CaptureStartException">Npcap could not be loaded, or no eligible adapter was found; in demo
    /// mode, the demo's skills, classes or bosses are no longer in upstream's game data.</exception>
    void Start();

    /// <summary>Stops capture, or the demo feed (its thread has ended when this returns). The current fight's numbers stay readable.</summary>
    void Stop();

    /// <summary>
    /// Drops the current fight: every target's session, the active target and buffered buffs. Known players,
    /// summon ownership and the captured stream are kept, so the next hit starts a fresh fight.
    /// </summary>
    void Reset();

    /// <summary>Whether capture runs and, once the game's connection is found, which adapter carries it.</summary>
    CaptureState Capture { get; }

    /// <summary>
    /// An immutable view of the active fight, cheap enough to poll several times a second. Training-dummy fights the
    /// user has not hit (other players' fights nearby) are ignored: while one is the active fight, the snapshot
    /// returned last is returned again, until a fight that counts or <see cref="Reset"/>. Until the meter knows which
    /// player is the user (from the character's info at login), every dummy fight counts.
    /// </summary>
    CombatSnapshot GetSnapshot();

    /// <summary>
    /// One player's breakdown of the active fight, or null when the player has dealt no damage in it (or no
    /// fight is active, or the active fight is a training-dummy fight <see cref="GetSnapshot"/> ignores). Like
    /// <see cref="GetSnapshot"/> it is a composition of consecutive reads, each under
    /// upstream's session lock: a hit landing between them can show in one part (say the skills) and not yet
    /// in another (the log). The active fight is compared before and after the reads, and a read during which
    /// it changed (the fight ended, or the party switched targets) is taken again; if three reads in a row
    /// straddle a change, the result is null and the next call reads afresh. The comparison is by identity, so a
    /// switch to another target and back between the two checks goes unnoticed: against several targets at once
    /// the parts can, rarely, mix two targets. Its cost grows with the fight's hits; polling it a few times a
    /// second for one player is fine.
    /// </summary>
    PlayerDetails? GetPlayerDetails(long playerId);

    /// <summary>
    /// A page of boss-fight history: the running boss fight first, then the saved ones, newest first. Upstream counts
    /// training dummies as bosses; dummy fights the user has not hit are neither listed while they run nor saved
    /// (see <see cref="GetSnapshot"/> for while the user is not known). Upstream
    /// runs the database query under its session lock, which holds up hit processing for that time, so read it
    /// on demand rather than on a timer. A damaged row in the history database does not make it throw: the
    /// fights that can be read are returned with <see cref="HistoryPage.IsComplete"/> false, and the damage is
    /// logged.
    /// </summary>
    /// <exception cref="ArgumentException">The query is invalid (see <see cref="HistoryQuery.Validate"/>).</exception>
    HistoryPage GetHistory(HistoryQuery query);

    /// <summary>
    /// A whole fight from history, saved or still running, or null when the id is unknown, its row cannot be
    /// read (logged) or it is a running fight <see cref="GetHistory"/> leaves out. A fight that has just ended can
    /// be missing for a moment: upstream saves it in the
    /// background. Saved fights are read from upstream's history store directly rather than through its
    /// session manager, which would hold its session lock while it deserialises every hit; that choice keeps
    /// loading a large fight from holding up live capture. Loading still takes a while (the store decompresses
    /// and deserialises every hit), so call it off the UI thread. Damaged saved data is left out (see
    /// <see cref="HistoryFight"/>).
    /// </summary>
    HistoryFight? GetHistoryFight(Guid fightId);

    /// <summary>Latest ping in milliseconds. Raised on a capture (or demo feed) thread, never on the UI thread.</summary>
    event EventHandler<int>? PingUpdated;

    /// <summary>
    /// The player's Odyle energy as the game last sent it, or null until it sends one: it does at login, after a
    /// server transfer and when the value changes (a raid, a recharge item), not on a teleport, so a meter started
    /// mid-session has none until then. A login's list without it, or the game naming another character with no list
    /// between, makes it null again. Kept across Stop and Reset.
    /// </summary>
    OdyleEnergy? OdyleEnergy { get; }

    /// <summary>The Odyle energy changed to a value. Raised on a capture (or demo feed) thread, never on the UI thread.</summary>
    event EventHandler<OdyleEnergy>? OdyleEnergyUpdated;

    /// <summary>
    /// The Shugo Festival keys as the game last sent them, or null until it sends them: in the same list as the Odyle
    /// energy, so at login, after a server transfer and when they change, and null again when the counts are (a login's
    /// list without them, another character named with no list between). As with the energy, when a move to a new
    /// connection loses its list, the keys from before show until a new list comes or the game names another character.
    /// Kept across Stop and Reset.
    /// </summary>
    ShugoKeys? ShugoKeys { get; }

    /// <summary>
    /// The Dimensional Invasion reward keys as the game last sent them, or null until it sends them: in the same list as
    /// the Shugo Festival keys, by the same rules (<see cref="ShugoKeys"/>).
    /// </summary>
    InvasionKeys? InvasionKeys { get; }

    /// <summary>
    /// The character being played, from the character's own record that upstream reads at login (and on loads after it),
    /// or null while that is not sure: until one is named, after a login's list until the record naming who logged in,
    /// and after a move to a new connection (a server change, or a switch whose first records were lost) until a record
    /// names the character again. What happens meanwhile (an Artifact Corridor entered) is then not put on anyone. Kept
    /// across Stop and Reset.
    /// </summary>
    CharacterIdentity? Character { get; }

    /// <summary>
    /// The player's entry counts (Odyle energy, Nightmare, Ascension Trial and the rest, by the game's entry id; see
    /// <see cref="EntryIds"/>) with whose they are. A login's list comes before the record naming the character, so its
    /// counts are raised once that record names them, and every update after that is raised as it comes, always with
    /// every count read since the list. Counts whose character is never named are never raised. Raised on a capture (or
    /// demo feed) thread, never on the UI thread.
    /// </summary>
    event EventHandler<CharacterProgress>? ProgressObserved;

    /// <summary>
    /// The id of the map the game last loaded (an id from the game's Map table; the Abyss's lower layer is 20), or null
    /// while it is not known. The game names the map on every zone load, a teleport inside an instance naming the
    /// instance again, but the load right after a server change (into or out of a dungeon or the Abyss) can be lost
    /// to upstream's reassembly, so the map is forgotten when the game moves to a new connection and is null until a
    /// load is read on it; a meter started mid-session has none until then either. Kept across Stop and Reset. The
    /// Abyss pause follows it (<see cref="PausedInAbyss"/>).
    /// </summary>
    int? MapId { get; }

    /// <summary>
    /// The map changed, or became unknown (null); a load of the same map again raises nothing. Raised on a capture
    /// thread, never on the UI thread.
    /// </summary>
    event EventHandler<int?>? MapChanged;

    /// <summary>
    /// Who holds the Abyss's artifacts, each time the game says it: when the player opens the Abyss map (both layers) or
    /// enters the Abyss (the layer entered). It is the world's on the server it was read on; the record does not say which
    /// server, so a subscriber puts it on <see cref="Character"/>'s. Raised on a capture thread, never on the UI thread.
    /// </summary>
    event EventHandler<AbyssArtifacts>? ArtifactsObserved;

    /// <summary>
    /// The game moved to a new connection: a server change (into or out of a dungeon or the Abyss) or a switch of
    /// character. Raised on a capture thread just before the first record read of the new connection, once the engine's
    /// own values have taken the move (<see cref="MapId"/> forgotten, <see cref="Character"/> unconfirmed): whatever a
    /// subscriber holds from the connection left, waiting to be put on a character, was seen there and nowhere else. Also
    /// raised for the meter's first connection, and for one last seen more than 16 connections before. Subscribers run
    /// under upstream's reassembly lock, so they must be quick; one that throws is logged and keeps the move from no
    /// other. The demo has no connections and never raises it.
    /// </summary>
    event EventHandler? ConnectionStarted;

    /// <summary>
    /// True while the meter is paused because the player is in the Abyss (<see cref="MapId"/> is one of its maps) and
    /// <see cref="EngineSettings.PauseInAbyss"/> is on. Meanwhile hits are dropped and nothing is kept or saved:
    /// <see cref="GetSnapshot"/> is empty and <see cref="GetPlayerDetails"/> null. It ends when the map changes or
    /// becomes unknown (leaving the Abyss is a server change), or the setting is turned off.
    /// </summary>
    bool PausedInAbyss { get; }
}
