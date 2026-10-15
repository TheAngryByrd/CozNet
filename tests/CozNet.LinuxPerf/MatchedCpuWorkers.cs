sealed class PhysicalWorkMode
{
    public static PhysicalWorkMode Baseline { get; } = new(false);
    public bool Optimized { get; }
    public string Name => Optimized ? "optimized" : "baseline";

    private PhysicalWorkMode(bool optimized) => Optimized = optimized;

    public static PhysicalWorkMode Parse(string value) => value switch
    {
        "baseline" => Baseline,
        "optimized" => new(true),
        _ => throw new ArgumentException("Select baseline or optimized physical work.")
    };
}

sealed class MatchedCpuWorkers : IDisposable
{
    public const long ExpectedChecksum = 49995000L;
    private readonly PhysicalWorkMode mode;
    private readonly long[] progress = new long[2];
    private readonly ManualResetEventSlim[] ready = [new(), new()];
    private readonly ManualResetEventSlim start = new();
    private int stop;

    public MatchedCpuWorkers(PhysicalWorkMode mode) => this.mode = mode;

    public static void Warmup()
    {
        Check(CpuWorkload.Run());
        Check(CpuWorkload.RunOptimized());
    }

    public void SignalReady(int slot) => ready[slot].Set();
    public bool WaitReady(int slot, TimeSpan timeout) => ready[slot].Wait(timeout);
    public void DisposeReady(int slot) => ready[slot].Dispose();
    public long Progress(int slot) => Interlocked.Read(ref progress[slot]);
    public long[] SnapshotProgress() => [Progress(0), Progress(1)];
    public void Release() => start.Set();

    public void RequestStop()
    {
        Volatile.Write(ref stop, 1);
        start.Set();
    }

    public void Run(int slot)
    {
        if (!start.Wait(TimeSpan.FromSeconds(5)))
            throw new InvalidDataException("The matched CPU worker start gate timed out.");
        if (slot == 0 && mode.Optimized)
        {
            while (Volatile.Read(ref stop) == 0)
            {
                Check(CpuWorkload.RunOptimized());
                Interlocked.Increment(ref progress[slot]);
            }
        }
        else
        {
            while (Volatile.Read(ref stop) == 0)
            {
                Check(CpuWorkload.Run());
                Interlocked.Increment(ref progress[slot]);
            }
        }
    }

    public void Dispose() => start.Dispose();

    private static void Check(long checksum)
    {
        if (checksum != ExpectedChecksum)
            throw new InvalidDataException("The matched CPU worker changed the checksum.");
    }
}
