using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using AionDpsMeter.Core.GameData.Services;
using AionDpsMeter.Core.Models;
using AionDpsMeter.Services.PacketProcessing.Routing;
using AionDpsMeter.Services.Services.Entity;
using Microsoft.Extensions.Logging;
using ScarletMeter.Engine.Records;

namespace ScarletMeter.Engine.Upstream;

/// <summary>
/// Keeps a player's class with their own skills on the live meter. Upstream gives a player id the class of the first
/// skill it sees from it and never replaces it, so a player handed an id another class had used shows that class
/// (<see cref="ClassSkills"/>). <see cref="RecordTap"/> hands this the game's damage records once upstream has read them,
/// and it counts each player's hits with class skills (<see cref="ClassSkills.ClassOf"/>) by class: once more of them
/// name another class than name the one upstream tracks the player under, the player takes that class, and upstream
/// shows and saves it from then on. Heals, which upstream does not count, are left out, and nothing is read while the
/// meter is paused in the Abyss, where hundreds of players hit.
/// </summary>
/// <remarks>
/// <para>The count starts again whenever upstream gives the player another name, as it does when a reused id gets its
/// new player's identity; the class the player has then starts with one hit. So a reused id takes its new player's
/// class at their second class-skill hit, and a few hits with another class's skills, which players do make (in the
/// player's saved fights, a few had up to 3% of their damage from another class's skills, Gladiator ones mostly), never
/// change it.</para>
/// <para>Runs on the capture thread, inside upstream's PacketAccumulator callback, where upstream's own processors write
/// the same players (EntityTracker has no lock); a class is one reference, so a reader elsewhere sees the old or the new
/// one, as with upstream's own writes. A player upstream does not track is never created, and the counts live as long as
/// upstream keeps the player (<see cref="ConditionalWeakTable{TKey, TValue}"/>). The record is read as far as the skill,
/// with upstream's checks on that part (DamagePacketProcessor, v1.11.5.341): a switch value whose low four bits are 4
/// to 7, and an actor other than the target.</para>
/// </remarks>
internal sealed class ClassSkillTap : IRecordReader
{
    // Upstream's check on the record's switch value (DamagePacketProcessor.ProcessSwitchValue).
    private const uint MaxSwitchValue = 255;

    private readonly EntityTracker entities;
    private readonly PauseState pause;
    private readonly ILogger<ClassSkillTap> logger;
    private readonly GameDataProvider gameData = GameDataProvider.Instance;
    private readonly ConditionalWeakTable<Player, ClassHits> counts = [];

    public ClassSkillTap(EntityTracker entities, PauseState pause, ILogger<ClassSkillTap> logger)
    {
        this.entities = entities;
        this.pause = pause;
        this.logger = logger;
    }

    /// <summary>The damage record.</summary>
    public bool Reads(ushort opcode) => opcode == PacketOpcodes.Damage;

    /// <summary>Reads one damage record from the stream (<see cref="RecordTap"/>).</summary>
    public void Read(byte[] record, ushort opcode)
    {
        if (pause.Paused || !TryReadHit(record, out var actorId, out var skillCode)) return;
        var classId = ClassSkills.ClassOf(skillCode);
        if (classId == 0 || entities.GetPlayerEntity(actorId) is not { } player || gameData.IsHealingSkill(skillCode)) return;

        var current = (int)(player.CharacterClass?.Id ?? 0);
        var count = counts.GetOrCreateValue(player);
        if (!string.Equals(count.Name, player.Name, StringComparison.Ordinal)) count.Restart(player.Name, current);
        if (count.Add(classId) <= count.Of(current) || gameData.Classes.GetById(classId) is not { } characterClass) return;
        player.CharacterClass = characterClass;
        logger.LogInformation("Player {PlayerId} is now class {ClassId}, as most of their skills say; upstream had {Previous}.", actorId, classId, current);
    }

    /// <summary>Nothing to forget: a class belongs to the player, not to where they are.</summary>
    public void ConnectionStarted()
    {
    }

    /// <summary>
    /// The actor and skill code of a damage record (its length varint, two opcode bytes, then the target, the switch
    /// value, a flag and the actor as varints, then the skill code, a little-endian u32), or false when the record is
    /// another kind, too short, or fails upstream's checks on those fields. Pure; every read is bounds-checked.
    /// </summary>
    internal static bool TryReadHit(ReadOnlySpan<byte> record, out int actorId, out int skillCode)
    {
        actorId = 0;
        skillCode = 0;
        if (!GameRecords.TryOpen(record, out var opcode, out var body) || opcode != PacketOpcodes.Damage) return false;
        if (!GameRecords.TryReadVarInt(body, 0, out var target, out var at)) return false;
        if (!GameRecords.TryReadVarInt(body, at, out var switchValue, out at) || switchValue > MaxSwitchValue
            || (switchValue & 0x0F) is < 4 or > 7) return false;
        if (!GameRecords.TryReadVarInt(body, at, out _, out at)) return false;
        if (!GameRecords.TryReadVarInt(body, at, out var actor, out at) || actor == target) return false;
        if (body.Length < at + sizeof(uint)) return false;
        actorId = (int)actor;
        skillCode = (int)BinaryPrimitives.ReadUInt32LittleEndian(body[at..]);
        return true;
    }

    /// <summary>
    /// One player's class-skill hits by class since they got the name counted under. Touched only on the capture thread.
    /// </summary>
    private sealed class ClassHits
    {
        private readonly int[] hits = new int[ClassSkills.Classes];

        /// <summary>The player's name when the count started, or null before it has.</summary>
        public string? Name { get; private set; }

        /// <summary>Starts again for a player now called <paramref name="name"/>, their class <paramref name="classId"/> one hit ahead.</summary>
        public void Restart(string? name, int classId)
        {
            Name = name;
            Array.Clear(hits);
            if (ClassSkills.IsClass(classId)) hits[classId - ClassSkills.FirstClass] = 1;
        }

        /// <summary>Counts a hit for a class (<see cref="ClassSkills.IsClass"/>) and returns its count; it stops at int.MaxValue.</summary>
        public int Add(int classId)
        {
            ref var count = ref hits[classId - ClassSkills.FirstClass];
            if (count < int.MaxValue) count++;
            return count;
        }

        /// <summary>The hits counted for <paramref name="classId"/>, 0 for none or an id that is no player's class.</summary>
        public int Of(int classId) => ClassSkills.IsClass(classId) ? hits[classId - ClassSkills.FirstClass] : 0;
    }
}
