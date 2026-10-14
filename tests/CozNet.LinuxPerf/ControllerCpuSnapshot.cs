using System.Diagnostics;

internal sealed class ControllerCpuSnapshot
{
    public long BeforeTimestamp { get; }
    public long CpuNanoseconds { get; }
    public long AfterTimestamp { get; }

    private ControllerCpuSnapshot(long before, long cpu, long after) =>
        (BeforeTimestamp, CpuNanoseconds, AfterTimestamp) = (before, cpu, after);

    public static ControllerCpuSnapshot Capture(IThreadCpuClock clock)
    {
        long before = Stopwatch.GetTimestamp();
        long cpu = clock.ReadNanoseconds();
        long after = Stopwatch.GetTimestamp();
        if (cpu < 0 || after < before)
            throw new InvalidOperationException("The controller CPU clock returned an invalid observation.");
        return new(before, cpu, after);
    }
}
