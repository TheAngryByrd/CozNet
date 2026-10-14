using System.Runtime.InteropServices;

sealed class LinuxSchedulerEventSession : ISchedulerEventSession
{
    [StructLayout(LayoutKind.Explicit, Size = 96)]
    private struct Attribute
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(4)] public uint Size;
        [FieldOffset(8)] public ulong Config;
        [FieldOffset(24)] public ulong SampleType;
        [FieldOffset(32)] public ulong ReadFormat;
        [FieldOffset(40)] public ulong Flags;
        [FieldOffset(92)] public int ClockId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Timespec { public long Seconds; public long Nanoseconds; }

    [DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
    private static extern long Open(long number, ref Attribute attribute, int pid, int cpu, int group, ulong flags);
    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    private static extern long Read(int fd, [Out] ulong[] values, nuint count);
    [DllImport("libc", EntryPoint = "clock_gettime", SetLastError = true)]
    private static extern int Clock(int id, out Timespec time);

    private readonly int fd;
    private readonly IntPtr mapping;
    private readonly nuint length;
    private readonly ulong ringSize;
    private readonly IntPtr ring;
    private readonly int pid;
    private readonly int tid;
    private readonly SchedulerRecord[] records = new SchedulerRecord[131072];
    private readonly byte[] scratch = new byte[40];
    private readonly Thread collector;
    private readonly ulong enableBefore;
    private readonly ulong enableAfter;
    private volatile bool stop;
    private int count;
    private ulong previous;
    private Exception? collectionError;
    private SchedulerCapture? result;
    private bool disposed;

    public LinuxSchedulerEventSession(int? targetThreadId = null)
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("Scheduler capture requires Linux x64.");
        pid = Environment.ProcessId;
        tid = targetThreadId ?? checked((int)Native.GetTid(186));
        if (tid <= 0) throw new ArgumentOutOfRangeException(nameof(targetThreadId));
        var attribute = new Attribute
        {
            Type = 1, Size = 96, Config = 9, SampleType = 6, ReadFormat = 16,
            Flags = 1UL | (1UL << 5) | (1UL << 6) | (1UL << 18) | (1UL << 25) | (1UL << 26),
            ClockId = 1
        };
        long descriptor = Open(298, ref attribute, tid, -1, -1, 8);
        if (descriptor < 0) throw Error("Scheduler perf_event_open");
        fd = checked((int)descriptor);
        length = checked((nuint)(65 * Environment.SystemPageSize));
        mapping = Native.Mmap(IntPtr.Zero, length, 3, 1, fd, 0);
        if (mapping == new IntPtr(-1))
        {
            var error = Error("Scheduler mmap");
            Native.Close(fd);
            throw error;
        }
        try
        {
            ulong offset = checked((ulong)Marshal.ReadInt64(mapping, 1040));
            ringSize = checked((ulong)Marshal.ReadInt64(mapping, 1048));
            if (offset != (ulong)Environment.SystemPageSize || ringSize != (ulong)(64 * Environment.SystemPageSize))
                throw new InvalidDataException("Unexpected scheduler ring layout.");
            ring = IntPtr.Add(mapping, checked((int)offset));
            enableBefore = previous = Now();
            if (Native.Ioctl(fd, 0x2400, 0) != 0) throw Error("Scheduler enable");
            enableAfter = Now();
            collector = new Thread(Collect) { IsBackground = true, Name = "CozNet scheduler capture" };
            collector.Start();
        }
        catch
        {
            Native.Ioctl(fd, 0x2401, 0);
            Native.Munmap(mapping, length);
            Native.Close(fd);
            throw;
        }
    }

    private static Exception Error(string operation) => new InvalidOperationException($"{operation} failed: {Marshal.GetLastPInvokeError()}.");

    private static ulong Now()
    {
        if (Clock(1, out var time) != 0) throw Error("Scheduler clock_gettime");
        if (time.Seconds < 0 || time.Nanoseconds < 0 || time.Nanoseconds >= 1000000000)
            throw new InvalidDataException("Invalid scheduler monotonic clock.");
        return checked((ulong)time.Seconds * 1000000000 + (ulong)time.Nanoseconds);
    }

    private void Collect()
    {
        try
        {
            while (!stop)
            {
                Drain();
                Thread.Sleep(1);
            }
        }
        catch (Exception error) { collectionError = error; }
    }

    private void Copy(ulong tail, int size)
    {
        int offset = checked((int)(tail % ringSize));
        int first = Math.Min(size, checked((int)ringSize) - offset);
        Marshal.Copy(IntPtr.Add(ring, offset), scratch, 0, first);
        if (first < size) Marshal.Copy(ring, scratch, first, size - first);
    }

    private void Drain()
    {
        ulong head = unchecked((ulong)Marshal.ReadInt64(mapping, 1024));
        Thread.MemoryBarrier();
        ulong tail = unchecked((ulong)Marshal.ReadInt64(mapping, 1032));
        if (head < tail || head - tail > ringSize) throw new InvalidDataException("Invalid scheduler ring distance.");
        while (tail < head)
        {
            if (head - tail < 8) throw new InvalidDataException("Truncated scheduler header.");
            Copy(tail, 8);
            int size = SchedulerRecordParser.Size(scratch.AsSpan(0, 8));
            if ((ulong)size > head - tail) throw new InvalidDataException("Truncated scheduler record.");
            Copy(tail, size);
            if (count == records.Length) throw new InvalidDataException("Scheduler record capacity exceeded.");
            var record = SchedulerRecordParser.Parse(scratch.AsSpan(0, size), pid, tid, previous);
            records[count++] = record;
            previous = record.TimestampNanoseconds;
            tail += (ulong)size;
        }
        Thread.MemoryBarrier();
        Marshal.WriteInt64(mapping, 1032, unchecked((long)tail));
    }

    public SchedulerCapture Finish()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (result is not null) return result;
        var failures = new List<string>();
        ulong before = 0, after = 0;
        try { before = Now(); }
        catch (Exception error) { failures.Add(error.ToString()); }
        if (Native.Ioctl(fd, 0x2401, 0) != 0) failures.Add(Error("Scheduler disable").ToString());
        try { after = Now(); }
        catch (Exception error) { failures.Add(error.ToString()); }
        stop = true;
        if (!collector.Join(TimeSpan.FromSeconds(2))) throw new TimeoutException("Scheduler collector did not stop.");
        if (collectionError is not null) failures.Add(collectionError.ToString());
        else
        {
            try { Drain(); }
            catch (Exception error) { failures.Add(error.ToString()); }
        }
        var values = new ulong[2];
        ulong? lost = null;
        if (Read(fd, values, 16) != 16) failures.Add(Error("Scheduler loss read").ToString());
        else
        {
            lost = values[1];
            if (lost != 0) failures.Add($"Scheduler capture lost {lost} records.");
        }
        if (previous > after) failures.Add("Scheduler record exceeds the disable boundary.");
        result = new(pid, tid, enableBefore, enableAfter, before, after, lost, records.AsSpan(0, count).ToArray(), failures.ToArray());
        return result;
    }

    public void Dispose()
    {
        if (disposed) return;
        Finish();
        Native.Munmap(mapping, length);
        Native.Close(fd);
        disposed = true;
    }
}
