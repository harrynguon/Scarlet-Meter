using ScarletMeter.Engine.Records;

namespace ScarletMeter.Engine.Entries;

/// <summary>
/// The player's own character record (<c>33 36</c>) as far as the entry list's owner needs it, in the layout upstream's
/// PlayerInfoProcessor reads (v1.11.5.341): <c>&lt;entity varint&gt; &lt;u32&gt; &lt;u8: bit 0, a name follows&gt;
/// &lt;name length varint&gt; &lt;UTF-8 name&gt; &lt;server id u16&gt; &lt;class code u32&gt; …</c>. Upstream decodes the
/// name itself; this only tells whether the record names a character at all, and reads the two bytes after the name that
/// upstream takes as the server id (and turns into a short name) and the four after them, the character's class, which
/// upstream does not read (A2Tools' <c>self_profile</c> in its stream_processor.rs and <c>JobClass::from_roster_class</c> in
/// its entity/job_class.rs, at 5c6f02c; THIRD-PARTY-NOTICES.md). A tutorial character's name is a placeholder, <c>$</c>
/// and a code, which upstream's decoding turns into a plain name by dropping the <c>$</c>; A2Tools treats it as no name
/// (<c>is_placeholder_name</c> in its stream_processor.rs, at b933531), and so does this. Pure; every read is
/// bounds-checked.
/// </summary>
internal static class SelfRecords
{
    /// <summary>
    /// The record's entity id, whether it carries no real name (no name flag, or a placeholder), and the server id and
    /// class code after a real name (each 0 when the record ends before it); false when the record has no entity id to
    /// read. A record cut before its name is not called unnamed: upstream then has no name for it either, which the tap
    /// learns by asking. The server id and class code are as the record has them: the reader checks the server id against
    /// upstream's, and <see cref="ClassIdOf"/> says which class a code is, if any.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> record, out int entityId, out bool unnamed, out int serverId, out uint classCode)
    {
        entityId = 0;
        unnamed = false;
        serverId = 0;
        classCode = 0;
        if (!GameRecords.TryOpen(record, out _, out var body) || !GameRecords.TryReadVarInt(body, 0, out var id, out var at)) return false;
        if (id is 0 or > int.MaxValue) return false;
        entityId = (int)id;
        if (at + 5 > body.Length) return true;
        if ((body[at + 4] & 0x01) == 0)
        {
            unnamed = true;
            return true;
        }
        if (!GameRecords.TryReadVarInt(body, at + 5, out var length, out var name) || length == 0 || name + length > body.Length) return true;
        unnamed = IsPlaceholder(body.Slice(name, (int)length));
        var end = name + (int)length;
        if (unnamed || end + 2 > body.Length) return true;
        serverId = body[end] | (body[end + 1] << 8);
        if (end + 6 <= body.Length) classCode = (uint)(body[end + 2] | (body[end + 3] << 8) | (body[end + 4] << 16) | (body[end + 5] << 24));
        return true;
    }

    /// <summary>
    /// Upstream's class id (its classes.json: 11 Gladiator, 12 Templar, 13 Assassin, 14 Ranger, 15 Sorcerer,
    /// 16 Elementalist, Global's Spiritmaster, 17 Cleric, 18 Chanter) for a self record's class code, or 0 for a code that
    /// is no class. The codes come four to a class, as A2Tools reads them (<c>JobClass::from_roster_class</c>; a
    /// Cleric's record held 30, 2026-10-04): 5 to 8 Gladiator, 9 to 12 Templar, 13 to 16 Ranger, 17 to 20 Assassin, 21 to
    /// 24 Spiritmaster, 25 to 28 Sorcerer, 29 to 32 Cleric, 33 to 36 Chanter. A2Tools saw other bytes there on one server
    /// (Nezekan, 2026-10-01), so a code outside these is no class rather than an error.
    /// </summary>
    public static int ClassIdOf(uint classCode) => classCode switch
    {
        >= 5 and <= 8 => 11,
        >= 9 and <= 12 => 12,
        >= 13 and <= 16 => 14,
        >= 17 and <= 20 => 13,
        >= 21 and <= 24 => 16,
        >= 25 and <= 28 => 15,
        >= 29 and <= 32 => 17,
        >= 33 and <= 36 => 18,
        _ => 0,
    };

    // "$" and at least four ASCII letters or digits, as A2Tools' is_placeholder_name.
    private static bool IsPlaceholder(ReadOnlySpan<byte> name)
    {
        if (name.Length < 5 || name[0] != (byte)'$') return false;
        foreach (var b in name[1..])
            if (!char.IsAsciiLetterOrDigit((char)b)) return false;
        return true;
    }
}
