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
    private readonly TimeProvider elapsedClock;
    private readonly long elapsedFrequency;
    private readonly int owner = Environment.CurrentManagedThreadId;
    private long last;
    private long lastObserved;
    private long lastElapsed;
    private Int128 requiredElapsedTicks;
    public long CpuClockReads { get; private set; }

    public ProbeCadence(IThreadCpuClock clock, long intervalNanoseconds, TimeProvider? elapsedClock = null)
    {
        if (intervalNanoseconds < 0) throw new ArgumentOutOfRangeException(nameof(intervalNanoseconds));
        this.clock = clock;
        this.elapsedClock = elapsedClock ?? TimeProvider.System;
        elapsedFrequency = this.elapsedClock.TimestampFrequency;
        if (elapsedFrequency <= 0)
            throw new ArgumentException("The elapsed clock frequency must be positive.", nameof(elapsedClock));
        interval = intervalNanoseconds;
        requiredElapsedTicks = ToElapsedTicks(interval);
        lastElapsed = this.elapsedClock.GetTimestamp();
        last = clock.ReadNanoseconds();
        CpuClockReads = 1;
        lastObserved = last;
    }

    private Int128 ToElapsedTicks(long nanoseconds) => (Int128)nanoseconds * elapsedFrequency / 1000000000;

    public bool IsDue()
    {
        if (Environment.CurrentManagedThreadId != owner)
            throw new InvalidOperationException("Probe cadence belongs to another thread.");
        if (interval == 0) return true;
        long elapsed = elapsedClock.GetTimestamp();
        if (elapsed < lastElapsed) throw new InvalidOperationException("The elapsed clock decreased.");
        if ((Int128)elapsed - lastElapsed < requiredElapsedTicks) return false;
        long now = clock.ReadNanoseconds();
        CpuClockReads++;
        if (now < lastObserved) throw new InvalidOperationException("The thread CPU clock decreased.");
        lastObserved = now;
        lastElapsed = elapsed;
        if (now - last < interval)
        {
            requiredElapsedTicks = ToElapsedTicks(interval - (now - last));
            return false;
        }
        last = now;
        requiredElapsedTicks = ToElapsedTicks(interval);
        return true;
    }
}
