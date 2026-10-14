using System.Buffers.Binary;

static class SchedulerRecordChecks
{
    public static void Run()
    {
        PreservesSwitchDirectionAndPreemption();
        PreservesLossCount();
        RejectsMalformedEnvelope();
        RejectsIdentityAndTimestampChanges();
        RejectsInvalidSwitchFlags();
    }

    private static byte[] Packet(uint type = 14, ushort flags = 0, ulong timestamp = 100)
    {
        byte[] bytes = new byte[type == 2 ? 40 : 24];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, type);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), flags);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), (ushort)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(bytes.Length - 16), 10);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(bytes.Length - 12), 20);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(bytes.Length - 8), timestamp);
        return bytes;
    }

    private static SchedulerRecord Parse(byte[] bytes, ulong previous = 99) => SchedulerRecordParser.Parse(bytes, 10, 20, previous);

    private static void Reject(byte[] bytes, string boundary, ulong previous = 99)
    {
        try { Parse(bytes, previous); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException($"Scheduler parser accepted {boundary}.");
    }

    private static void PreservesSwitchDirectionAndPreemption()
    {
        foreach (ushort flags in new ushort[] { 0, 1, 2, 1 << 13, (1 << 13) | (1 << 14) })
        {
            var result = Parse(Packet(flags: flags), 100);
            var expected = (flags & (1 << 13)) == 0 ? SchedulerRecordKind.SwitchIn : SchedulerRecordKind.SwitchOut;
            if (result != new SchedulerRecord(expected, flags, 100, 0))
                throw new InvalidOperationException($"Scheduler switch flags {flags} did not preserve direction, flags, or timestamp.");
        }
    }

    private static void PreservesLossCount()
    {
        byte[] loss = Packet(2);
        BinaryPrimitives.WriteUInt64LittleEndian(loss.AsSpan(8), 777);
        BinaryPrimitives.WriteUInt64LittleEndian(loss.AsSpan(16), 123);
        if (Parse(loss) != new SchedulerRecord(SchedulerRecordKind.Loss, 0, 100, 123))
            throw new InvalidOperationException("Scheduler loss parsing confused the event ID and loss count.");
        BinaryPrimitives.WriteUInt64LittleEndian(loss.AsSpan(16), 0);
        Reject(loss, "an empty loss record");
    }

    private static void RejectsMalformedEnvelope()
    {
        foreach (uint type in new uint[] { 14, 2 })
        {
            byte[] packet = Packet(type);
            for (int length = 0; length < packet.Length; length++) Reject(packet[..length], $"truncation at {length}");
            Reject([.. packet, 0], "a trailing byte");
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6), 8);
            Reject(packet, "a mismatched record size");
        }
        Reject(Packet(15), "a CPU-wide switch record");
        Reject(Packet(9), "a sample record");
    }

    private static void RejectsIdentityAndTimestampChanges()
    {
        foreach (int offset in new[] { 8, 12 })
        {
            byte[] packet = Packet();
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(offset), 21);
            Reject(packet, $"an identity change at {offset}");
        }
        Reject(Packet(timestamp: 98), "a timestamp before the previous record");
        if (Parse(Packet(timestamp: 99)).TimestampNanoseconds != 99)
            throw new InvalidOperationException("Equal scheduler timestamps must remain valid.");
    }

    private static void RejectsInvalidSwitchFlags()
    {
        Reject(Packet(flags: 1 << 14), "preemption on switch-in");
        Reject(Packet(flags: 1 << 15), "an unknown switch flag");
    }
}
