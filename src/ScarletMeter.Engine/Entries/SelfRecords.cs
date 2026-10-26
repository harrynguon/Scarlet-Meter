using ScarletMeter.Engine.Records;

namespace ScarletMeter.Engine.Entries;

/// <summary>
/// The player's own character record (<c>33 36</c>) as far as the entry list's owner needs it, in the layout upstream's
/// PlayerInfoProcessor reads (v1.11.5.341): <c>&lt;entity varint&gt; &lt;u32&gt; &lt;u8: bit 0, a name follows&gt;
/// &lt;name length varint&gt; &lt;UTF-8 name&gt; …</c>. Upstream decodes the name itself; this only tells whether the
/// record names a character at all. A tutorial character's name is a placeholder, <c>$</c> and a code, which upstream's
/// decoding turns into a plain name by dropping the <c>$</c>; A2Tools treats it as no name (<c>is_placeholder_name</c>
/// in its stream_processor.rs, at b933531), and so does this. Pure; every read is bounds-checked.
/// </summary>
internal static class SelfRecords
{
    /// <summary>
    /// The record's entity id, and whether it carries no real name (no name flag, or a placeholder); false when the
    /// record has no entity id to read. A record cut before its name is not called unnamed: upstream then has no name
    /// for it either, which the tap learns by asking.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> record, out int entityId, out bool unnamed)
    {
        entityId = 0;
        unnamed = false;
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
        return true;
    }

    // "$" and at least four ASCII letters or digits, as A2Tools' is_placeholder_name.
    private static bool IsPlaceholder(ReadOnlySpan<byte> name)
    {
        if (name.Length < 5 || name[0] != (byte)'$') return false;
        foreach (var b in name[1..])
            if (!char.IsAsciiLetterOrDigit((char)b)) return false;
        return true;
    }
}
