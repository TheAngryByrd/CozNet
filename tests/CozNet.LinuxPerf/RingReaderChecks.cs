using System.Buffers.Binary;
using System.Runtime.InteropServices;

static class RingReaderChecks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Run()
    {
        EmptyDrainsDoNotAllocate();
        NewSampleUpdatesCachedCounters();
        LostAndThrottleRecordsUpdateCachedCounters();
        WrappedSampleUpdatesCachedCounters();
        CorruptHeadIsRejectedAfterCachedDrain();
        SelectedSymbolsCannotBeChangedByCaller();
    }

    private static void EmptyDrainsDoNotAllocate()
    {
        using var ring = new Fixture();
        ring.Sample(0x1000, 1, 1000000);
        SampleBatch result = ring.Reader.Drain();
        for (int index = 0; index < 32; index++) result = ring.Reader.Drain();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 10000; index++) result = ring.Reader.Drain();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(allocated == 0, $"Empty drains allocated {allocated} managed bytes.");
        Require(result.Samples == 1 && result.SelectedMethodSamples == 1 && ring.Delivered.Count == 1,
            "Empty drains must retain counters without publishing duplicate samples.");
    }

    private static void NewSampleUpdatesCachedCounters()
    {
        using var ring = new Fixture();
        var empty = ring.Reader.Drain();
        ring.Sample(0x1000, 1, 1000000);
        var selected = ring.Reader.Drain();
        ring.Sample(0x2000, 2, 2000000);
        var other = ring.Reader.Drain();
        Require(empty.Samples == 0 && selected.Samples == 1 && selected.RepresentedCpuNanoseconds == 1000000,
            "A later drain must not change an earlier snapshot.");
        Require(other.Samples == 2 && other.SelectedMethodSamples == 1 && other.DistinctInstructionPointers == 2
            && other.RepresentedCpuNanoseconds == 3000000 && ring.Delivered.Count == 2,
            "New records must update cumulative counters and publish each sample once.");
        Require(ring.Delivered[1].Method == "other" && ring.Delivered[1].Timestamp == 2,
            "A record after an empty drain must retain its own attribution and timestamp.");
    }

    private static void LostAndThrottleRecordsUpdateCachedCounters()
    {
        using var ring = new Fixture();
        var empty = ring.Reader.Drain();
        var lost = Record(2, 24);
        BinaryPrimitives.WriteUInt64LittleEndian(lost.AsSpan(16), 7);
        ring.Append(lost);
        ring.Append(Record(5, 32));
        var result = ring.Reader.Drain();
        Require(result.LostRecords == 1 && result.LostSamples == 7 && result.Throttles == 1 && result.Samples == 0,
            "Non-sample records must invalidate the cached counters.");
        Require(empty.LostRecords == 0 && ring.Delivered.Count == 0,
            "Lost and throttle records must not alter old snapshots or publish samples.");
    }

    private static void WrappedSampleUpdatesCachedCounters()
    {
        using var ring = new Fixture();
        for (ulong time = 1; time <= 3; time++)
        {
            ring.Sample(0x1000, time, 1000000);
            ring.Reader.Drain();
        }
        ring.Sample(0x3000, 4, 1000000);
        var result = ring.Reader.Drain();
        Require(result.Samples == 4 && result.UnmappedSamples == 1 && result.SelectedMethodSamples == 3
            && result.RepresentedCpuNanoseconds == 4000000 && ring.Delivered.Count == 4,
            "A sample across the ring boundary must update the cached snapshot once.");
        Require(ring.Delivered[3].Method is null && ring.Delivered[3].Timestamp == 4,
            "A wrapped record must preserve unmapped attribution and its timestamp.");
    }

    private static void CorruptHeadIsRejectedAfterCachedDrain()
    {
        using var ring = new Fixture();
        ring.Sample(0x1000, 1, 1000000);
        ring.Reader.Drain();
        ring.SetHead(0);
        bool rejected = false;
        try { ring.Reader.Drain(); }
        catch (Exception error) when (error.Message == "Ring bounds are invalid.") { rejected = true; }
        Require(rejected, "A cached snapshot must not bypass ring-bound checks.");
    }

    private static void SelectedSymbolsCannotBeChangedByCaller()
    {
        using var ring = new Fixture();
        ring.Sample(0x1000, 1, 1000000);
        var result = ring.Reader.Drain();
        bool rejected = false;
        try { ((IList<string>)result.SelectedSymbols)[0] = "changed"; }
        catch (NotSupportedException) { rejected = true; }
        Require(rejected && ring.Reader.Drain().SelectedSymbols.Single() == "selected",
            "A caller must not mutate symbols retained by the cache.");
    }

    private static byte[] Record(uint type, ushort length)
    {
        var result = new byte[length];
        BinaryPrimitives.WriteUInt32LittleEndian(result, type);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6), length);
        return result;
    }

    private sealed class Fixture : IDisposable, IInstructionMap, ICpuSampleSink
    {
        private const int Offset = 4096, Size = 128;
        private readonly IntPtr mapping = Marshal.AllocHGlobal(Offset + Size);
        private ulong head;
        public List<CpuSample> Delivered { get; } = new();
        public RingReader Reader { get; }

        public Fixture()
        {
            Marshal.Copy(new byte[Offset + Size], 0, mapping, Offset + Size);
            Reader = new(mapping, Offset, Size, 42, this, "selected", this);
        }

        public string? Resolve(ulong address) => address switch { 0x1000 => "selected", 0x2000 => "other", _ => null };
        public void Publish(CpuSample sample) => Delivered.Add(sample);
        public void SetHead(ulong value) => Marshal.WriteInt64(mapping, 1024, checked((long)value));

        public void Sample(ulong address, ulong timestamp, ulong period)
        {
            var record = Record(9, 40);
            BinaryPrimitives.WriteUInt64LittleEndian(record.AsSpan(8), address);
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(16), checked((uint)Environment.ProcessId));
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(20), 42);
            BinaryPrimitives.WriteUInt64LittleEndian(record.AsSpan(24), timestamp);
            BinaryPrimitives.WriteUInt64LittleEndian(record.AsSpan(32), period);
            Append(record);
        }

        public void Append(byte[] record)
        {
            foreach (byte value in record)
            {
                Marshal.WriteByte(mapping, Offset + (int)(head % Size), value);
                head++;
            }
            SetHead(head);
        }

        public void Dispose() => Marshal.FreeHGlobal(mapping);
    }
}
