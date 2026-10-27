using System.Buffers.Binary;
using ScarletMeter.Engine.Records;

namespace ScarletMeter.Engine.Abyss;

/// <summary>One war as read: its AbyssArtifactEvent id and its two times in Unix milliseconds, 0 for none.</summary>
internal readonly record struct ArtifactWarRead(int EventId, long SinceMs, long UntilMs);

/// <summary>One artifact as read: its AbyssArtifact id and the holder's byte (0 nobody, 1 Elyos, 2 Asmodian).</summary>
internal readonly record struct ArtifactRead(int Id, byte Holder);

/// <summary>What one artifact record says: its wars and its artifacts, in the record's order.</summary>
internal sealed record ArtifactRecord(IReadOnlyList<ArtifactWarRead> Wars, IReadOnlyList<ArtifactRead> Artifacts);

/// <summary>
/// Reads who holds each Abyss artifact from the game's records, as A2Tools reads them (taengu/A2Tools-DPS-Meter,
/// <c>src-tauri/src/capture/abyss_artifacts.rs</c>, added in 6837e55 and unchanged up to c3fcce3; GPL v3;
/// THIRD-PARTY-NOTICES.md): with the Abyss map when it is opened (bytes <c>07 e3</c>, both layers) and on entering the
/// Abyss (<c>05 e3</c>, the layer entered).
/// <code>
/// 07 e3 &lt;u16 0&gt; &lt;wars u8&gt; wars × &lt;war&gt; &lt;artifacts u8&gt; artifacts × &lt;artifact&gt;
/// 05 e3                  &lt;war&gt;               &lt;artifacts u8&gt; artifacts × &lt;artifact&gt;
/// war:      &lt;u8&gt; &lt;event id u32&gt; &lt;u8&gt; &lt;u8&gt; &lt;since u64 ms&gt; [&lt;until u64 ms&gt;]
/// artifact: &lt;artifact id u32&gt; &lt;u32&gt; &lt;holder u8&gt; &lt;8 bytes&gt;
/// </code>
/// A war a side holds carries when the holding began and when it resets; one nobody holds carries a single 0, so a war's
/// second time is there or not, and the reading taken is the one that accounts for every byte. A2Tools had one capture
/// of these records, so a record is read only when every byte falls into place; anything else in its shape is left
/// alone rather than guessed at. Pure; every read is bounds-checked.
/// </summary>
internal static class AbyssArtifactRecords
{
    /// <summary>The Abyss map's record (bytes <c>07 e3</c> after the length).</summary>
    public const ushort MapOpcode = 0xE307;

    /// <summary>The record on entering the Abyss (bytes <c>05 e3</c> after the length).</summary>
    public const ushort EntryOpcode = 0xE305;

    /// <summary>The most wars and artifacts one record is read with: the game has two layers of three.</summary>
    public const int MaxWars = 8;
    public const int MaxArtifacts = 32;

    /// <summary>An artifact's size.</summary>
    public const int ArtifactBytes = 17;

    // A war's size without its reset time, and the reset's.
    private const int WarBytes = 15;
    private const int ResetBytes = 8;

    // The times taken: 0, or from 2020 to before 2100, in Unix milliseconds.
    private const long EarliestMs = 1_577_836_800_000;
    private const long LatestMs = 4_102_444_800_000;

    /// <summary>Whether a record with this opcode is one of the two.</summary>
    public static bool IsArtifacts(ushort opcode) => opcode is MapOpcode or EntryOpcode;

    /// <summary>
    /// The wars and artifacts in one record (its length varint, two opcode bytes, then its body), or null when the record
    /// is neither kind or does not read whole.
    /// </summary>
    public static ArtifactRecord? Read(ReadOnlySpan<byte> record)
    {
        if (!GameRecords.TryOpen(record, out var opcode, out var body)) return null;
        if (opcode == EntryOpcode) return WarsThenArtifacts(body, 0, 1);
        if (opcode != MapOpcode || body.Length < 3 || body[0] != 0 || body[1] != 0) return null;
        var wars = body[2];
        return wars is 0 or > MaxWars ? null : WarsThenArtifacts(body, 3, wars);
    }

    // `wars` wars from `at`, then the artifacts to exactly the body's end. Each war is read with its reset and without,
    // the one its start suggests first (a holding that began has a reset), and kept when the rest reads whole.
    private static ArtifactRecord? WarsThenArtifacts(ReadOnlySpan<byte> body, int at, int wars)
    {
        if (wars == 0) return Artifacts(body, at) is { } artifacts ? new ArtifactRecord([], artifacts) : null;
        if (!TryId(body, at + 1, out var eventId) || !TryTime(body, at + 7, out var since)) return null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var withReset = (attempt == 0) == (since != 0);
            var until = 0L;
            if (withReset && !TryTime(body, at + WarBytes, out until)) continue;
            if (WarsThenArtifacts(body, at + WarBytes + (withReset ? ResetBytes : 0), wars - 1) is not { } rest) continue;
            return rest with { Wars = [new ArtifactWarRead(eventId, since, until), .. rest.Wars] };
        }
        return null;
    }

    // The artifacts from `at`, their count first, ending exactly at the body's end.
    private static List<ArtifactRead>? Artifacts(ReadOnlySpan<byte> body, int at)
    {
        if (at >= body.Length) return null;
        int count = body[at];
        if (count is 0 or > MaxArtifacts || body.Length != at + 1 + count * ArtifactBytes) return null;
        List<ArtifactRead> artifacts = new(count);
        for (var n = 0; n < count; n++)
        {
            var start = at + 1 + n * ArtifactBytes;
            if (!TryId(body, start, out var id)) return null;
            artifacts.Add(new ArtifactRead(id, body[start + 8]));
        }
        return artifacts;
    }

    // An id (u32) from 1 to below a million, as the game's tables number events and artifacts.
    private static bool TryId(ReadOnlySpan<byte> body, int at, out int id)
    {
        id = 0;
        if (at < 0 || at + 4 > body.Length) return false;
        var value = BinaryPrimitives.ReadUInt32LittleEndian(body[at..]);
        if (value is 0 or >= 1_000_000) return false;
        id = (int)value;
        return true;
    }

    // A time (u64, Unix milliseconds): 0, or one from 2020 to before 2100.
    private static bool TryTime(ReadOnlySpan<byte> body, int at, out long ms)
    {
        ms = 0;
        if (at < 0 || at + 8 > body.Length) return false;
        var value = BinaryPrimitives.ReadUInt64LittleEndian(body[at..]);
        if (value != 0 && value is < EarliestMs or >= LatestMs) return false;
        ms = (long)value;
        return true;
    }
}
