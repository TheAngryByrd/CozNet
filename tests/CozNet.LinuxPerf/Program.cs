using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Runtime.CompilerServices;

CollectorChecks.Run();
if (args is ["--verify-collector"]) return 0;
if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
    throw new PlatformNotSupportedException("The syscall probe requires Linux x64.");
if (args.Length is < 1 or > 2) throw new ArgumentException("Pass a JSON evidence file path and optional speedup percentage.");
int speedup = args.Length == 2 ? int.Parse(args[1]) : 50;
if (speedup is not (0 or 50)) throw new ArgumentException("The capability workload supports zero or 50 percent.");
if (Marshal.SizeOf<PerfAttribute>() != 64) throw new Exception("Unexpected perf attribute layout.");
Thread.Sleep(1);
if (CpuWorkload.Run() != 49995000L) throw new Exception("The workload warmup returned an invalid checksum.");
long tid = Native.GetTid(186);
var attr = new PerfAttribute { Type = 1, Size = 64, Config = 1, SamplePeriod = 1000000,
    SampleType = 1 | 2 | 4 | 256, Flags = 1UL | (1UL << 5) | (1UL << 6), WakeupEvents = 1 };
long fd = Native.PerfEventOpen(298, ref attr, 0, -1, -1, 8);
if (fd < 0)
{
    File.WriteAllText(args[0], JsonSerializer.Serialize(new { available = false, errno = Marshal.GetLastPInvokeError() }));
    return 2;
}
int page = Environment.SystemPageSize;
nuint mappingLength = (nuint)(65 * page);
IntPtr mapping = Native.Mmap(IntPtr.Zero, mappingLength, 3, 1, (int)fd, 0);
if (mapping == new IntPtr(-1))
{
    int error = Marshal.GetLastPInvokeError();
    Native.Close((int)fd);
    throw new Exception($"mmap failed: {error}");
}
try
{
    ulong offset = (ulong)Marshal.ReadInt64(mapping, 1040);
    ulong size = (ulong)Marshal.ReadInt64(mapping, 1048);
    if (offset != (ulong)page || size != (ulong)(64 * page)) throw new Exception("Unexpected perf ring layout.");
    IInstructionMap codeMap = PerfMapSnapshot.Read($"/tmp/perf-{Environment.ProcessId}.map");
    const string selectedSymbol = "int64 [CozNet.LinuxPerf] CpuWorkload::Run()[Optimized]";

    var observations = new List<PhaseObservation>();
    ulong previousCpu = 0;
    long checksum = 0;
    long completedBatches = 0;
    foreach (bool busy in new[] { true, false })
    {
        using var intervention = busy ? new SampledIntervention(selectedSymbol, speedup) : null;
        intervention?.WaitUntilReady();
        var capture = new SampleCapture(intervention);
        var reader = new RingReader(mapping, offset, size, tid, codeMap, selectedSymbol, capture);
        using var collector = new LiveSampleCollector(reader.Drain);
        if (Native.Ioctl((int)fd, 0x2400, 0) != 0) throw new Exception($"Enable failed: {Marshal.GetLastPInvokeError()}");
        var elapsed = Stopwatch.StartNew();
        if (busy)
        {
            while (elapsed.ElapsedMilliseconds < 500)
            {
                checksum += CpuWorkload.Run();
                completedBatches++;
                intervention?.Probe();
            }
        }
        else Thread.Sleep(500);
        int liveSamples = collector.Latest?.Samples ?? 0;
        int liveDeliveredSamples = capture.Published;
        var liveIntervention = intervention?.ObserveLive();
        if (Native.Ioctl((int)fd, 0x2401, 0) != 0) throw new Exception($"Disable failed: {Marshal.GetLastPInvokeError()}");
        if (Native.Read((int)fd, out ulong cpu, 8) != 8) throw new Exception($"Read failed: {Marshal.GetLastPInvokeError()}");
        var records = collector.Complete();
        var delivered = capture.AfterCollectorJoin();
        var interventionResult = intervention?.Complete(liveIntervention!);
        if (delivered.Length != records.Samples
            || delivered.Count(sample => sample.Method == selectedSymbol) != records.SelectedMethodSamples
            || delivered.Aggregate(0UL, (total, sample) => checked(total + sample.PeriodNanoseconds)) != records.RepresentedCpuNanoseconds)
            throw new Exception("Delivered sample records do not match the ring totals.");
        if (cpu < previousCpu) throw new Exception("The task CPU clock decreased.");
        observations.Add(new PhaseObservation(busy, elapsed.Elapsed.TotalMilliseconds, cpu - previousCpu,
            liveSamples, liveDeliveredSamples, records, delivered, interventionResult));
        previousCpu = cpu;
    }
    File.WriteAllText(args[0], JsonSerializer.Serialize(new { available = true, runtime = RuntimeInformation.FrameworkDescription,
        os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
        requestedPeriodNanoseconds = attr.SamplePeriod, checksum, completedBatches, observations }, new JsonSerializerOptions {
            WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    var active = observations[0];
    var blocked = observations[1];
    if (completedBatches == 0 || checksum != checked(completedBatches * 49995000L))
        throw new Exception("The arithmetic result does not match completed work.");
    if (active.CpuNanoseconds == 0 || active.Records.Samples == 0) throw new Exception("The busy phase produced no CPU samples.");
    if (active.LiveSamples == 0) throw new Exception("No samples reached the target before collection stopped.");
    if (active.LiveDeliveredSamples == 0) throw new Exception("No individual samples reached the sink before collection stopped.");
    if (active.Records.SelectedMethodSamples == 0) throw new Exception("No CPU event resolved to the selected managed method.");
    if (blocked.CpuNanoseconds >= active.CpuNanoseconds || blocked.Records.Samples >= active.Records.Samples)
        throw new Exception("The blocked phase was not below the busy phase in both CPU time and sample count.");
    if (observations.Any(phase => phase.Records.LostRecords != 0 || phase.Records.LostSamples != 0 || phase.Records.Throttles != 0))
        throw new Exception("The run contains lost or throttled events. Inspect the evidence file.");
    return 0;
}
finally { Native.Munmap(mapping, mappingLength); Native.Close((int)fd); }

sealed record SampleBatch(int Samples, int DistinctInstructionPointers, ulong RepresentedCpuNanoseconds,
    int LostRecords, ulong LostSamples, int Throttles, int SelectedMethodSamples, int UnmappedSamples,
    IReadOnlyList<string> SelectedSymbols);
sealed record PhaseObservation(bool Busy, double WallMilliseconds, ulong CpuNanoseconds,
    int LiveSamples, int LiveDeliveredSamples, SampleBatch Records, IReadOnlyList<CpuSample> Delivered,
    InterventionResult? Intervention);

static class CpuWorkload
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static long Run()
    {
        long sum = 0;
        for (int i = 0; i < 10000; i++) sum += i;
        return sum;
    }
}

sealed class RingReader(IntPtr mapping, ulong offset, ulong size, long tid,
    IInstructionMap codeMap, string selectedSymbol, ICpuSampleSink sink)
{
    private ulong tail = (ulong)Marshal.ReadInt64(mapping, 1032);
    private ulong priorTime;
    private int samples = 0, lostRecords = 0, throttles = 0;
    private ulong lostSamples = 0, periods = 0;
    private readonly HashSet<ulong> ips = new();

    private readonly HashSet<string> selectedSymbols = new();
    private int selected = 0, unmapped = 0;

    private byte[] Copy(ulong position, int length)
    {
        var bytes = new byte[length];
        for (int i = 0; i < length; i++) bytes[i] = Marshal.ReadByte(mapping + checked((int)(offset + ((position + (ulong)i) % size))));
        return bytes;
    }
    public SampleBatch Drain()
    {
        ulong head = (ulong)Marshal.ReadInt64(mapping, 1024);
        Thread.MemoryBarrier();
        if (head < tail || head - tail > size) throw new Exception("Ring bounds are invalid.");
        while (tail < head)
        {
            if (head - tail < 8) throw new Exception("Truncated record header.");
            byte[] header = Copy(tail, 8);
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(header);
            ushort misc = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(4));
            ushort length = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6));
            if (length < 8 || length > head - tail) throw new Exception("Invalid record length.");
            byte[] record = Copy(tail, length);
            if (type == 9)
            {
                if (length != 40 || (misc & 7) != 2) throw new Exception("Unexpected sample layout or CPU mode.");
                ulong ip = BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(8));
                uint recordedPid = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(16));
                uint recordedTid = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(20));
                ulong timestamp = BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(24));
                ulong period = BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(32));
                if (ip == 0 || recordedPid != Environment.ProcessId || recordedTid != tid || timestamp < priorTime || period == 0)
                    throw new Exception("Invalid sample attribution metadata.");
                ips.Add(ip); priorTime = timestamp; periods += period; samples++;
                string? symbol = codeMap.Resolve(ip);
                sink.Publish(CpuSample.Create(recordedPid, recordedTid, timestamp, period, ip, symbol));

                if (symbol is null) unmapped++;
                else if (string.Equals(symbol, selectedSymbol, StringComparison.Ordinal)) { selected++; selectedSymbols.Add(symbol); }
            }
            else if (type == 2)
            {
                if (length != 24) throw new Exception("Unexpected lost-record layout.");
                lostRecords++; lostSamples += BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(16));
            }
            else if (type is 5 or 6) throttles++;
            else throw new Exception($"Unexpected perf record type {type}.");
            tail += length;
        }
        Thread.MemoryBarrier();
        Marshal.WriteInt64(mapping, 1032, (long)tail);
        return new SampleBatch(samples, ips.Count, periods, lostRecords, lostSamples, throttles,
            selected, unmapped, selectedSymbols.Order().ToArray());
    }
}

[StructLayout(LayoutKind.Sequential)]
struct PerfAttribute
{
    public uint Type, Size;
    public ulong Config, SamplePeriod, SampleType, ReadFormat, Flags;
    public uint WakeupEvents, BreakpointType;
    public ulong Config1;
}
static class Native
{
    [DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
    public static extern long PerfEventOpen(long number, ref PerfAttribute attr, int pid, int cpu, int group, ulong flags);
    [DllImport("libc", EntryPoint = "syscall")]
    public static extern long GetTid(long number);
    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    public static extern int Ioctl(int fd, ulong request, ulong arg);
    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    public static extern long Read(int fd, out ulong value, ulong length);
    [DllImport("libc", EntryPoint = "mmap", SetLastError = true)]
    public static extern IntPtr Mmap(IntPtr address, nuint length, int protection, int flags, int fd, long offset);
    [DllImport("libc", EntryPoint = "munmap")]
    public static extern int Munmap(IntPtr address, nuint length);
    [DllImport("libc", EntryPoint = "close")]
    public static extern int Close(int fd);
}
