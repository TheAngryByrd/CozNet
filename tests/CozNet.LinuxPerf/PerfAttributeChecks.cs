using System.Buffers.Binary;
using System.Runtime.InteropServices;

static class PerfAttributeChecks
{
    public static void TaskClockUsesMonotonicAbi()
    {
        var attribute = PerfAttribute.TaskClock();
        var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref attribute, 1));
        Require(bytes.Length == 96 && Marshal.SizeOf<PerfAttribute>() == 96, "perf_event_attr must occupy 96 bytes.");
        Require(BinaryPrimitives.ReadUInt32LittleEndian(bytes[0..]) == 1, "The event type must be software.");
        Require(BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) == 96, "The declared attribute size must include clockid.");
        Require(BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]) == 1, "The event must count task-clock CPU.");
        Require(BinaryPrimitives.ReadUInt64LittleEndian(bytes[16..]) == 1000000, "The nominal period must remain one millisecond.");
        Require(BinaryPrimitives.ReadUInt64LittleEndian(bytes[24..]) == 263, "The sample must retain IP, TID, TIME, and PERIOD.");
        Require(BinaryPrimitives.ReadUInt64LittleEndian(bytes[32..]) == 0, "The counter read format must remain a single value.");
        Require(BinaryPrimitives.ReadUInt64LittleEndian(bytes[40..]) == 33554529, "The flags must select disabled, exclude_kernel, exclude_hv, and use_clockid.");
        Require(BinaryPrimitives.ReadUInt32LittleEndian(bytes[48..]) == 1, "Each event must trigger wakeup.");
        Require(bytes[52..92].IndexOfAnyExcept((byte)0) < 0, "Unused ABI fields must remain zero.");
        Require(BinaryPrimitives.ReadInt32LittleEndian(bytes[92..]) == 1, "clockid must select CLOCK_MONOTONIC.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
