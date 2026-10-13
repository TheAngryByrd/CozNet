using System.ComponentModel;
using System.Runtime.InteropServices;

interface IThreadCpuClock
{
    long ReadNanoseconds();
}

sealed class LinuxThreadCpuClock : IThreadCpuClock
{
    public LinuxThreadCpuClock()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("The thread CPU clock requires Linux x64.");
        if (Marshal.SizeOf<Timespec>() != 16)
            throw new PlatformNotSupportedException("Unexpected Linux x64 timespec layout.");
    }

    public long ReadNanoseconds()
    {
        if (GetTime(3, out var value) != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot read the thread CPU clock.");
        return checked((long)value.Seconds.Value * 1000000000 + (long)value.Nanoseconds.Value);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Timespec
    {
        public CLong Seconds;
        public CLong Nanoseconds;
    }

    [DllImport("libc", EntryPoint = "clock_gettime", SetLastError = true)]
    private static extern int GetTime(int clockId, out Timespec value);
}

sealed class ProbeCadence
{
    private readonly IThreadCpuClock clock;
    private readonly long interval;
    private readonly int owner = Environment.CurrentManagedThreadId;
    private long last;
    private long lastObserved;

    public ProbeCadence(IThreadCpuClock clock, long intervalNanoseconds)
    {
        if (intervalNanoseconds < 0) throw new ArgumentOutOfRangeException(nameof(intervalNanoseconds));
        this.clock = clock;
        interval = intervalNanoseconds;
        last = clock.ReadNanoseconds();
        lastObserved = last;
    }

    public bool IsDue()
    {
        if (Environment.CurrentManagedThreadId != owner)
            throw new InvalidOperationException("Probe cadence belongs to another thread.");
        if (interval == 0) return true;
        long now = clock.ReadNanoseconds();
        if (now < lastObserved) throw new InvalidOperationException("The thread CPU clock decreased.");
        lastObserved = now;
        if (now - last < interval) return false;
        last = now;
        return true;
    }
}
