using System.Buffers.Binary;

static class SchedulerRecordParser
{
    public static int Size(ReadOnlySpan<byte> header)
    {
        if (header.Length < 8) throw new InvalidDataException("Truncated scheduler header.");
        uint type = BinaryPrimitives.ReadUInt32LittleEndian(header);
        int expected = type switch { 14 => 24, 2 => 40, _ => throw new InvalidDataException("Unexpected scheduler record type.") };
        if (BinaryPrimitives.ReadUInt16LittleEndian(header[6..]) != expected)
            throw new InvalidDataException("Invalid scheduler record size.");
        return expected;
    }

    public static SchedulerRecord Parse(ReadOnlySpan<byte> record, int pid, int tid, ulong previous)
    {
        int size = Size(record);
        if (record.Length != size) throw new InvalidDataException("Scheduler record length differs from its header.");
        uint type = BinaryPrimitives.ReadUInt32LittleEndian(record);
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(record[4..]);
        var trailer = record[(size - 16)..];
        if (BinaryPrimitives.ReadUInt32LittleEndian(trailer) != pid || BinaryPrimitives.ReadUInt32LittleEndian(trailer[4..]) != tid)
            throw new InvalidDataException("Unexpected scheduler record identity.");
        ulong timestamp = BinaryPrimitives.ReadUInt64LittleEndian(trailer[8..]);
        if (timestamp < previous) throw new InvalidDataException("Scheduler timestamps decreased.");
        ulong lost = type == 2 ? BinaryPrimitives.ReadUInt64LittleEndian(record[16..]) : 0;
        bool isOut = (flags & (1 << 13)) != 0;
        if (type == 14 && ((flags & ~(7 | (1 << 13) | (1 << 14))) != 0 || (!isOut && (flags & (1 << 14)) != 0)))
            throw new InvalidDataException("Invalid scheduler switch flags.");
        if (type == 2 && lost == 0) throw new InvalidDataException("Empty scheduler loss record.");
        return new(type == 2 ? SchedulerRecordKind.Loss : isOut ? SchedulerRecordKind.SwitchOut : SchedulerRecordKind.SwitchIn,
            flags, timestamp, lost);
    }
}
