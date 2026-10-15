using System.Runtime.InteropServices;

sealed class LinuxCpuEventSession : ICpuEventSession
{
    public ISampleClockEvidenceSource ClockEvidence { get; } = new LinuxSampleClockEvidenceSource();
    private readonly int fd;
    private IntPtr mapping;
    private readonly nuint length;
    private readonly RingReader reader;
    private bool enabled;
    private bool disposed;

    public LinuxCpuEventSession(ICpuSampleSink sink)
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("The CPU-event session requires Linux x64.");
        var map = PerfMapSnapshot.Read($"/tmp/perf-{Environment.ProcessId}.map");
        long tid = Native.GetTid(186);
        var attribute = PerfAttribute.TaskClock();
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
        if (enabled) throw new InvalidOperationException("The CPU-event session is already enabled.");
        if (Native.Ioctl(fd, 0x2400, 0) != 0) throw new InvalidOperationException($"Enable failed: {Marshal.GetLastPInvokeError()}.");
        enabled = true;
    }

    public ulong Disable()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!enabled) throw new InvalidOperationException("The CPU-event session is not enabled.");
        if (Native.Ioctl(fd, 0x2401, 0) != 0) throw new InvalidOperationException($"Disable failed: {Marshal.GetLastPInvokeError()}.");
        enabled = false;
        if (Native.Read(fd, out ulong cpu, 8) != 8) throw new InvalidOperationException($"CPU read failed: {Marshal.GetLastPInvokeError()}.");
        return cpu;
    }

    public SampleBatch Drain()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return reader.Drain();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (enabled) Native.Ioctl(fd, 0x2401, 0);
        if (mapping != IntPtr.Zero && mapping != new IntPtr(-1)) Native.Munmap(mapping, length);
        Native.Close(fd);
    }
}
