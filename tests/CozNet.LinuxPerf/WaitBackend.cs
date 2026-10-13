using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using CozNet;

interface IWaitBackend
{
    string Name { get; }
    void Wait(DelayTicks duration, CancellationToken cancellation);
}

sealed class ManagedWaitBackend : IWaitBackend
{
    public string Name => "managed";
    public void Wait(DelayTicks duration, CancellationToken cancellation) =>
        DelayRuntimeTimingModule.real.Wait.Invoke(duration).Invoke(cancellation);
}

sealed class LinuxNanoSleepBackend : IWaitBackend
{
    public string Name => "linux-nanosleep";

    public LinuxNanoSleepBackend()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("The nanosleep backend requires Linux x64.");
        if (Marshal.SizeOf<Timespec>() != 16)
            throw new PlatformNotSupportedException("Unexpected Linux x64 timespec layout.");
    }

    public void Wait(DelayTicks duration, CancellationToken cancellation)
    {
        long requested = DelayTicksModule.value(duration);
        long start = Stopwatch.GetTimestamp();
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            long remaining = requested - (Stopwatch.GetTimestamp() - start);
            if (remaining <= 0) return;
            var nanoseconds = ((BigInteger)remaining * 1000000000 + Stopwatch.Frequency - 1) / Stopwatch.Frequency;
            var request = new Timespec { Nanoseconds = new CLong((nint)BigInteger.Min(nanoseconds, 1000000)) };
            if (NanoSleep(in request, IntPtr.Zero) == 0) continue;
            int error = Marshal.GetLastPInvokeError();
            if (error != 4) throw new Win32Exception(error, "nanosleep failed.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Timespec
    {
        public CLong Seconds;
        public CLong Nanoseconds;
    }

    [DllImport("libc", EntryPoint = "nanosleep", SetLastError = true)]
    private static extern int NanoSleep(in Timespec request, IntPtr remaining);
}
