namespace ScarletMeter.Engine.Upstream;

/// <summary>
/// A reader of game records that upstream does not decode, fed by <see cref="RecordTap"/>: every record of the
/// captured stream, plain or from an LZ4 bundle, whose opcode it takes.
/// </summary>
internal interface IRecordReader
{
    /// <summary>Whether the reader takes records with this opcode. Asked for every record, so it must be cheap.</summary>
    bool Reads(ushort opcode);

    /// <summary>
    /// Reads one record whose opcode it takes (its length varint, two opcode bytes, then its body), on the capture
    /// thread. Malformed bytes should give no value rather than an exception; one that escapes is contained by the tap,
    /// and the record still reaches the other readers.
    /// </summary>
    void Read(byte[] record, ushort opcode);

    /// <summary>
    /// The game has moved to a new connection (a server change: into or out of a dungeon or the Abyss), seen when the
    /// tap first reads a record of it, just before that record. The first records of a new connection can be lost to
    /// upstream's reassembly (see <see cref="RecordTap"/>), so a reader whose value belongs to where the player is
    /// should forget it here; one whose value belongs to the character keeps it.
    /// </summary>
    void ConnectionStarted();
}
