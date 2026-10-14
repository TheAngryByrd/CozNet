using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Runtime.CompilerServices;

if (args is ["--verify-scheduler-cost-startup", var schedulerCostCheckPath])
{
    SchedulerCostWorkloadChecks.FailedStartsRetainEvidence(schedulerCostCheckPath);
    return 0;
}

if (args is ["--scheduler-cost", var schedulerCostPath, var schedulerCostMode])
    return SchedulerCostWorkload.Run(schedulerCostPath, schedulerCostMode);

if (args is ["--verify-scheduler-records"])
{
    SchedulerRecordChecks.Run();
    return 0;
}

if (args is ["--verify-scheduler-shutdown", var schedulerCheckPath, var schedulerCheckMode])
    return SampledSignalAbortChecks.SchedulerShutdown(schedulerCheckPath, schedulerCheckMode);

if (args is ["--sampled-signal-scheduler", var schedulerPath, var schedulerPercent, var schedulerCadence,
    var schedulerStartup, var schedulerWait, var schedulerGc])
{
    var wait = SignalWaitMode.Parse(schedulerWait);
    var plan = SampledSignalPlan.Parse(schedulerPercent, schedulerCadence, schedulerStartup, schedulerGc);
    return SampledSignalWorkload.Run(schedulerPath, plan, () => new LinuxSignalBackend(wait),
        sink => new LinuxCpuEventSession(sink), new LinuxThreadCpuClock(),
        schedulerFactory: () => new LinuxSchedulerEventSession());
}

if (args is ["--verify-signal-trace-abort", var abortPath, var abortStartup])
    return SampledSignalAbortChecks.Run(abortPath, abortStartup);

if (args is ["--verify-signal-trace"])
{
    SampledSignalTraceChecks.Run();
    return 0;
}

if (args is [var traceMode, var tracePath, var tracePercent, var traceCadence, var traceStartup, var traceWait, var traceGc]
    && traceMode is "--sampled-signal-trace" or "--sampled-signal-target-cpu")
{
    var parsedWait = SignalWaitMode.Parse(traceWait);
    var plan = SampledSignalPlan.Parse(tracePercent, traceCadence, traceStartup, traceGc);
    bool targetCpu = traceMode == "--sampled-signal-target-cpu";
    return SampledSignalWorkload.Run(tracePath, plan,
        () => new LinuxSignalBackend(parsedWait, targetCpu), sink => new LinuxCpuEventSession(sink), new LinuxThreadCpuClock(),
        new SampledSignalTrace(), targetCpu);
}

if (args is ["--verify-sampled-gc"])
{
    SampledGcStressChecks.Run();
    return 0;
}

if (args is ["--verify-signal-wait"])
{
    SignalWaitModeChecks.Run();
    return 0;
}

if (args is ["--verify-peer-cadence"])
{
    PeerSignalCadenceChecks.Run();
    return 0;
}

if (args is ["--verify-ring-reader"])
{
    RingReaderChecks.Run();
    return 0;
}

if (args is ["--verify-signal-timing"])
{
    SignalPhaseTimingChecks.Run();
    return 0;
}

if (args is ["--verify-signal-cadence"])
{
    SelectedSignalCadenceChecks.Run();
    return 0;
}

if (args is ["--sampled-signal", var gcPath, var gcPercent, var gcCadence, var gcStartup, var gcWait, var sampledGc])
{
    var parsedWait = SignalWaitMode.Parse(gcWait);
    return SampledSignalWorkload.Run(gcPath, SampledSignalPlan.Parse(gcPercent, gcCadence, gcStartup, sampledGc),
        () => new LinuxSignalBackend(parsedWait), sink => new LinuxCpuEventSession(sink), new LinuxThreadCpuClock());
}

if (args is ["--sampled-signal", var modePath, var modePercent, var modeCadence, var modeStartup, var waitMode])
{
    var parsedWait = SignalWaitMode.Parse(waitMode);
    return SampledSignalWorkload.Run(modePath, SampledSignalPlan.Parse(modePercent, modeCadence, modeStartup),
        () => new LinuxSignalBackend(parsedWait), sink => new LinuxCpuEventSession(sink), new LinuxThreadCpuClock());
}

if (args is ["--signal-wait", var waitPath, var waitPlan, var waitGc, var waitKind])
{
    var parsedWait = SignalWaitMode.Parse(waitKind);
    bool forceGc = waitGc switch
    {
        "gc" => true,
        "no-gc" => false,
        _ => throw new ArgumentException("Select gc or no-gc signal measurement.")
    };
    return SignalWorkload.Run(waitPath, SignalPlan.Parse(waitPlan), forceGc, () => new LinuxSignalBackend(parsedWait));
}

if (args is ["--sampled-signal", var sampledSignalPath, var sampledSignalPercent])
    return SampledSignalWorkload.Run(sampledSignalPath, SampledSignalPlan.Parse(sampledSignalPercent),
        () => new LinuxSignalBackend(), sink => new LinuxCpuEventSession(sink), new LinuxThreadCpuClock());

if (args is ["--sampled-signal", var sampledCadencePath, var sampledCadencePercent, var sampledCadence])
    return SampledSignalWorkload.Run(sampledCadencePath, SampledSignalPlan.Parse(sampledCadencePercent, sampledCadence),
        () => new LinuxSignalBackend(), sink => new LinuxCpuEventSession(sink), new LinuxThreadCpuClock());

if (args is ["--sampled-signal", var sampledWarmPath, var sampledWarmPercent, var sampledWarmCadence, var sampledStartup])
    return SampledSignalWorkload.Run(sampledWarmPath, SampledSignalPlan.Parse(sampledWarmPercent, sampledWarmCadence, sampledStartup),
        () => new LinuxSignalBackend(), sink => new LinuxCpuEventSession(sink), new LinuxThreadCpuClock());

if (args is ["--verify-signal-targets"])
{
    SignalTargetChecks.Run();
    return 0;
}

if (args is ["--signal-transition-wait", var transitionWaitPath, var transitionWaitMode, var transitionWaitGc, var transitionWaitKind])
{
    var parsedWait = SignalWaitMode.Parse(transitionWaitKind);
    bool forceGc = transitionWaitGc switch
    {
        "gc" => true,
        "no-gc" => false,
        _ => throw new ArgumentException("Select gc or no-gc transition measurement.")
    };
    return SignalTransitionWorkload.Run(transitionWaitPath, SignalTransitionWorkload.Parse(transitionWaitMode), forceGc,
        () => new LinuxSignalBackend(parsedWait), () => new LinuxSignalTransitionOperations());
}

if (args is ["--signal-transition", var transitionPath, var transitionMode, var transitionGc])
{
    bool forceGc = transitionGc switch
    {
        "gc" => true,
        "no-gc" => false,
        _ => throw new ArgumentException("Select gc or no-gc transition measurement.")
    };
    return SignalTransitionWorkload.Run(transitionPath, SignalTransitionWorkload.Parse(transitionMode), forceGc,
        () => new LinuxSignalBackend(), () => new LinuxSignalTransitionOperations());
}

if (args is ["--signal", var signalPath, var signalMode, var signalGc])
{
    bool forceGc = signalGc switch
    {
        "gc" => true,
        "no-gc" => false,
        _ => throw new ArgumentException("Select gc or no-gc signal measurement.")
    };
    return SignalWorkload.Run(signalPath, SignalPlan.Parse(signalMode), forceGc, () => new LinuxSignalBackend());
}

CollectorChecks.Run();
TimingChecks.CadenceBoundaries();
InjectedProbeChecks.Run();
RawIlCandidateChecks.Run();
if (args is ["--verify-collector"]) return 0;
if (args is ["--verify-overhead"])
{
    OverheadChecks.Run();
    return 0;
}
if (args is ["--overhead", var overheadPath, var overheadStage, var overheadDuration])
{
    var plan = OverheadPlan.Parse(overheadStage);
    if (plan.Callbacks && (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64))
        throw new PlatformNotSupportedException("The current overhead CPU clock requires Linux x64.");
    OverheadWorkload.Run(overheadPath, plan, MeasurementWindow.Parse(overheadDuration),
        plan.Callbacks ? new LinuxThreadCpuClock() : null, plan.Collect ? sink => new LinuxCpuEventSession(sink) : null);
    return 0;
}
if (args.Length is 3 or 4 && args[0] == "--physical")
{
    PhysicalWorkload.Run(args[1], args[2], MeasurementWindow.Parse(args.Length == 4 ? args[3] : "500"));
    return 0;
}
if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
    throw new PlatformNotSupportedException("The syscall probe requires Linux x64.");
if (args.Length is < 1 or > 5) throw new ArgumentException("Pass a JSON evidence path, speedup percentage, wait backend, probe CPU milliseconds, and optional measurement milliseconds.");
var window = MeasurementWindow.Parse(args.Length == 5 ? args[4] : "500");
var probePlan = ProbePlan.Parse(Environment.GetEnvironmentVariable("COZNET_PROBE_MODE"),
    Environment.GetEnvironmentVariable("COZNET_PROBE_INTERVAL"));
var probeMode = probePlan.Mode;
var attributionMode = AttributionPlan.Parse(Environment.GetEnvironmentVariable("COZNET_ATTRIBUTION"), probePlan,
    Environment.GetEnvironmentVariable("COZNET_IL_MAP_MODE"), Environment.GetEnvironmentVariable("COZNET_PROBE_PLACEMENT"));
if (attributionMode != AttributionMode.Method && (Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") != "0" ||
    Environment.GetEnvironmentVariable("DOTNET_ReadyToRun") != "0"))
    throw new ArgumentException("Raw attribution requires disabled tiered compilation and ReadyToRun.");
int speedup = args.Length >= 2 ? int.Parse(args[1]) : 50;
if (speedup is not (0 or 50)) throw new ArgumentException("The capability workload supports zero or 50 percent.");
IWaitBackend waitBackend = (args.Length >= 3 ? args[2] : "managed") switch
{
    "managed" => new ManagedWaitBackend(),
    "linux-nanosleep" => new LinuxNanoSleepBackend(),
    _ => throw new ArgumentException("Select managed or linux-nanosleep waits.")
};
int probeCpuMilliseconds = args.Length >= 4 ? int.Parse(args[3]) : 10;
if (probeCpuMilliseconds is not (0 or 10)) throw new ArgumentException("Select zero or 10 CPU milliseconds between probes.");
IThreadCpuClock cpuClock = new LinuxThreadCpuClock();
TimingChecks.WaitDurationAndCancellation(waitBackend);
if (Marshal.SizeOf<PerfAttribute>() != 64) throw new Exception("Unexpected perf attribute layout.");
Thread.Sleep(1);
long beforeWarmupCalls = InjectedProbes.Calls;
if (CpuWorkload.Run() != 49995000L) throw new Exception("The workload warmup returned an invalid checksum.");
if (probeMode == ProbeMode.Injected) InjectedProbes.VerifyRewrite(probePlan, InjectedProbes.Calls - beforeWarmupCalls);
long initialInjectedCalls = InjectedProbes.Calls;
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
    if (attributionMode != AttributionMode.Method)
        codeMap = RawIlCandidateMap.Read(codeMap, selectedSymbol, attributionMode == AttributionMode.RawExcludeCandidates);

    var observations = new List<PhaseObservation>();
    ulong previousCpu = 0;
    long checksum = 0;
    long completedBatches = 0;
    foreach (bool busy in new[] { true, false })
    {
        using var intervention = busy ? new SampledIntervention(selectedSymbol, speedup, waitBackend,
            cpuClock, probeCpuMilliseconds * 1000000L, probePlan) : null;
        using var injectedBinding = busy && probeMode == ProbeMode.Injected ? InjectedProbes.Bind(intervention!.Probe) : null;
        intervention?.WaitUntilReady();
        var capture = new SampleCapture(intervention);
        var reader = new RingReader(mapping, offset, size, tid, codeMap, selectedSymbol, capture);
        using var collector = new LiveSampleCollector(reader.Drain);
        if (Native.Ioctl((int)fd, 0x2400, 0) != 0) throw new Exception($"Enable failed: {Marshal.GetLastPInvokeError()}");
        var elapsed = Stopwatch.StartNew();
        if (busy)
        {
            while (elapsed.ElapsedMilliseconds < window.Milliseconds)
            {
                checksum += CpuWorkload.Run();
                completedBatches++;
                if (probeMode == ProbeMode.Manual) intervention?.Probe();
            }
        }
        else Thread.Sleep(500);
        int liveSamples = collector.Latest?.Samples ?? 0;
        int liveDeliveredSamples = capture.Published;
        var liveIntervention = intervention?.ObserveLive();
        double experimentMilliseconds = elapsed.Elapsed.TotalMilliseconds;
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
        observations.Add(new PhaseObservation(busy, experimentMilliseconds, elapsed.Elapsed.TotalMilliseconds, cpu - previousCpu,
            liveSamples, liveDeliveredSamples, records, delivered, interventionResult));
        previousCpu = cpu;
    }
    File.WriteAllText(args[0], JsonSerializer.Serialize(new { available = true, runtime = RuntimeInformation.FrameworkDescription,
        os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
        requestedPeriodNanoseconds = attr.SamplePeriod, measurementMilliseconds = window.Milliseconds,
        checksum, completedBatches, probeMode = probeMode.ToString(), probeBackedgeInterval = probePlan.BackedgeInterval,
        injectedCalls = InjectedProbes.Calls - initialInjectedCalls,
        attributionMode = attributionMode.ToString(), rawAttribution = (codeMap as RawIlCandidateMap)?.Evidence,
        observations }, new JsonSerializerOptions {
            WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    var active = observations[0];
    if (attributionMode == AttributionMode.RawExcludeCandidates && !active.Delivered.Any(x => x.Method == RawIlCandidateMap.CandidateSymbol))
        throw new Exception("The attribution diagnostic captured no excluded candidate sample.");
    if (InjectedProbes.Calls - initialInjectedCalls != checked(completedBatches * probePlan.CallsPerBatch))
        throw new Exception("Injected probe counts do not match completed work.");
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
sealed record PhaseObservation(bool Busy, double ExperimentMilliseconds, double WallMilliseconds, ulong CpuNanoseconds,
    int LiveSamples, int LiveDeliveredSamples, SampleBatch Records, IReadOnlyList<CpuSample> Delivered,
    InterventionResult? Intervention);

static class CpuWorkload
{
    public const int Iterations = 10000;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Probe() => InjectedProbes.Hit();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static long Run()
    {
        long sum = 0;
        for (int i = 0; i < Iterations; i++) sum += i;
        return sum;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static long RunOptimized()
    {
        long sum = 37497500;
        for (int i = 0; i < 5000; i++) sum += i;
        return sum;
    }
}

sealed class RingReader(IntPtr mapping, ulong offset, ulong size, long tid,
    IInstructionMap codeMap, string selectedSymbol, ICpuSampleSink sink)
{
    private SampleBatch? snapshot;
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
        if (head == tail && snapshot is not null) return snapshot;
        snapshot = null;
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
        snapshot = new SampleBatch(samples, ips.Count, periods, lostRecords, lostSamples, throttles,
            selected, unmapped, Array.AsReadOnly(selectedSymbols.Order().ToArray()));
        return snapshot;
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
