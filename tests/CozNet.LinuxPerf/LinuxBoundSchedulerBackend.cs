sealed class LinuxBoundSchedulerBackend : IBoundSchedulerBackend
{
    public string ClockDomain => "linux-clock-monotonic-nanoseconds";
    public ISchedulerEventSession StartCurrentThread() => new LinuxSchedulerEventSession();
    public ulong ReadClockNanoseconds() => LinuxSchedulerEventSession.Now();

    public static IBoundSchedulerBackend? FromEnvironment() =>
        Environment.GetEnvironmentVariable("COZNET_BOUND_SCHEDULER") switch
        {
            null or "0" => null,
            "1" => new LinuxBoundSchedulerBackend(),
            _ => throw new ArgumentException("COZNET_BOUND_SCHEDULER must be 0 or 1.")
        };
}
