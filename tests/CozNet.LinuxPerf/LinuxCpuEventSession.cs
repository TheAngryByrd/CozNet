using System.Runtime.InteropServices;

sealed class LinuxCpuEventSession : ICpuEventSession, ICpuCounterEvidenceSource
{
    public ISampleClockEvidenceSource ClockEvidence { get; } = new LinuxSampleClockEvidenceSource();
    public ICpuCounterEvidenceSource CounterEvidence => this;
    private readonly int fd;
    private IntPtr mapping;
    private readonly nuint length;
    private readonly RingReader reader;
    private readonly CpuCounterJournal counters;
    private readonly Action enableEvent, disableEvent;
    private bool disposed;

    public LinuxCpuEventSession(ICpuSampleSink sink, IInstructionMap? instructionMap = null)
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("The CPU-event session requires Linux x64.");
        var map = instructionMap ?? PerfMapSnapshot.Read($"/tmp/perf-{Environment.ProcessId}.map");
        long tid = Native.GetTid(186);
        var attribute = PerfAttribute.TaskClock();
        counters = new(TimeProvider.System, ReadRawCounter);
        enableEvent = EnableRaw;
        disableEvent = DisableRaw;
        long descriptor = Native.PerfEventOpen(298, ref attribute, 0, -1, -1, 8);
        if (descriptor < 0) throw new InvalidOperationException($"perf_event_open failed: {Marshal.GetLastPInvokeError()}.");
        fd = checked((int)descriptor);
        int page = Environment.SystemPageSize;
        length = (nuint)(65 * page);
        try
        {
            mapping = Native.Mmap(IntPtr.Zero, length, 3, 1, fd, 0);
            if (mapping == new IntPtr(-1)) throw new InvalidOperationException($"mmap failed: {Marshal.GetLastPInvokeError()}.");
            ulong offset = (ulong)Marshal.ReadInt64(mapping, 1040);
            ulong size = (ulong)Marshal.ReadInt64(mapping, 1048);
            if (offset != (ulong)page || size != (ulong)(64 * page)) throw new InvalidDataException("Unexpected perf ring layout.");
            reader = new RingReader(mapping, offset, size, tid, map, OverheadWorkload.SelectedSymbol, sink);
        }
        catch { Dispose(); throw; }
    }

    public void Enable()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (counters.Enabled) throw new InvalidOperationException("The CPU-event session is already enabled.");
        counters.Transition(true, enableEvent);
    }

    public ulong Disable()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!counters.Enabled) throw new InvalidOperationException("The CPU-event session is not enabled.");
        counters.Transition(false, disableEvent);
        return counters.ReadCounter(propagateFailure: true).CpuNanoseconds!.Value;
    }

    private ulong ReadRawCounter()
    {
        if (Native.Read(fd, out ulong cpu, 8) != 8) throw new InvalidOperationException($"CPU read failed: {Marshal.GetLastPInvokeError()}.");
        return cpu;
    }

    private void EnableRaw()
    {
        if (Native.Ioctl(fd, 0x2400, 0) != 0) throw new InvalidOperationException($"Enable failed: {Marshal.GetLastPInvokeError()}.");
    }

    private void DisableRaw()
    {
        if (Native.Ioctl(fd, 0x2401, 0) != 0) throw new InvalidOperationException($"Disable failed: {Marshal.GetLastPInvokeError()}.");
    }

    public CpuCounterReading ReadCounter()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return counters.ReadCounter();
    }

    public CpuCounterSourceSnapshot DescribeCounters() => counters.Snapshot();

    public SampleBatch Drain()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return reader.Drain();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (counters.Enabled)
        {
            counters.MarkUnrecordedCleanup();
            Native.Ioctl(fd, 0x2401, 0);
        }
        if (mapping != IntPtr.Zero && mapping != new IntPtr(-1)) Native.Munmap(mapping, length);
        Native.Close(fd);
    }
}
