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
/// and it counts each player's uses of class skills (<see cref="ClassSkills.ClassOf"/>) by class: once more of them
/// name another class than name the one upstream tracks the player under, the player takes that class, and upstream
/// shows and saves it from then on. Heals, which upstream does not count, are left out, and nothing is read while the
/// meter is paused in the Abyss, where hundreds of players hit.
/// </summary>
/// <remarks>
/// <para>A use is a run of one player's hits with one skill: the game sends a record for each target a skill lands on
/// and each time it hits, so one skill on five targets is five records and one use. The count starts again whenever
/// upstream gives the player another name, as it does when a reused id gets its new player's identity, with the class
/// the player has one use ahead. So a reused id takes its new player's class at their second class skill. Players do use
/// a few of another class's skills (in the player's saved fights, a few had up to 3% of their damage from them, Gladiator
/// ones mostly): one such skill, on any number of targets, never moves the class; two different ones in a row right
/// after a name change are the least that does, and two of the player's own move it back.</para>
/// <para>Runs on the capture thread, inside upstream's PacketAccumulator callback, where upstream's own processors write
/// the same players (EntityTracker has no lock); a class is one reference, so a reader elsewhere sees the old or the new
/// one, as with upstream's own writes. A player upstream does not track is never created, and the counts live as long as
/// upstream keeps the player (<see cref="ConditionalWeakTable{TKey, TValue}"/>). A record counts only when it reads whole
/// as upstream reads it (DamagePacketProcessor, v1.11.5.341), with its checks: a switch value whose low four bits are 4
/// to 7, and an actor other than the target. The first four varints are read strictly (one past 32 bits is refused,
/// where upstream wraps it), so this counts no record upstream drops.</para>
/// </remarks>
internal sealed class ClassSkillTap : IRecordReader
{
    // Upstream's check on the record's switch value (DamagePacketProcessor.ProcessSwitchValue).
    private const uint MaxSwitchValue = 255;

    private readonly EntityTracker entities;
    private readonly PauseState pause;
    private readonly ILogger<ClassSkillTap> logger;
    private readonly GameDataProvider gameData = GameDataProvider.Instance;
    private readonly ConditionalWeakTable<Player, ClassUses> counts = [];

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
        if (!count.Use(classId, skillCode, out var uses) || uses <= count.Of(current)
            || gameData.Classes.GetById(classId) is not { } characterClass) return;
        player.CharacterClass = characterClass;
        logger.LogInformation("Player {PlayerId} is now class {ClassId}, as most of their skills say; upstream had {Previous}.", actorId, classId, current);
    }

    /// <summary>Nothing to forget: a class belongs to the player, not to where they are.</summary>
    public void ConnectionStarted()
    {
    }

    /// <summary>
    /// The actor and skill code of a damage record, or false when the record is another kind or does not read whole as
    /// upstream's DamagePacketProcessor reads one: its length varint and two opcode bytes, then the target, the switch
    /// value, a flag and the actor as varints, the skill code (a little-endian u32), a byte, the damage type (a varint),
    /// the special block (eight bytes for switch value 4, at least twelve of which eleven are read for 5 to 7), a varint
    /// and the damage (a varint), with upstream's checks on the switch value and the actor. Pure; every read is
    /// bounds-checked.
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
        var skill = BinaryPrimitives.ReadUInt32LittleEndian(body[at..]);
        at += sizeof(uint) + 1;
        if (!TrySkipVarInt(body, ref at)) return false;
        // Upstream also asks for eight (or twelve) bytes left before the block; the two varints after it need more.
        at += (switchValue & 0x0F) == 4 ? 8 : 11;
        if (!TrySkipVarInt(body, ref at) || !TrySkipVarInt(body, ref at)) return false;
        actorId = (int)actor;
        skillCode = (int)skill;
        return true;
    }

    // Steps over a varint as upstream's PacketReader.ReadVarInt reads one: at most five bytes, whatever they hold.
    private static bool TrySkipVarInt(ReadOnlySpan<byte> data, ref int at)
    {
        for (var n = 0; n < 5 && at < data.Length; n++)
            if ((data[at++] & 0x80) == 0) return true;
        return false;
    }

    /// <summary>
    /// One player's uses of class skills by class since they got the name counted under, a run of hits with one skill
    /// counting once. Touched only on the capture thread.
    /// </summary>
    private sealed class ClassUses
    {
        private readonly int[] uses = new int[ClassSkills.Classes];
        private int lastSkill;

        /// <summary>The player's name when the count started, or null before it has.</summary>
        public string? Name { get; private set; }

        /// <summary>Starts again for a player now called <paramref name="name"/>, their class <paramref name="classId"/> one use ahead.</summary>
        public void Restart(string? name, int classId)
        {
            Name = name;
            Array.Clear(uses);
            lastSkill = 0;
            if (ClassSkills.IsClass(classId)) uses[classId - ClassSkills.FirstClass] = 1;
        }

        /// <summary>
        /// Counts a hit with <paramref name="skillCode"/> of a class (<see cref="ClassSkills.IsClass"/>) as a use, unless
        /// it goes on a run of hits with that skill (false); <paramref name="classUses"/> is then the class's uses. A
        /// count stops at int.MaxValue.
        /// </summary>
        public bool Use(int classId, int skillCode, out int classUses)
        {
            ref var count = ref uses[classId - ClassSkills.FirstClass];
            var counted = skillCode != lastSkill;
            lastSkill = skillCode;
            if (counted && count < int.MaxValue) count++;
            classUses = count;
            return counted;
        }

        /// <summary>The uses counted for <paramref name="classId"/>, 0 for none or an id that is no player's class.</summary>
        public int Of(int classId) => ClassSkills.IsClass(classId) ? uses[classId - ClassSkills.FirstClass] : 0;
    }
}
