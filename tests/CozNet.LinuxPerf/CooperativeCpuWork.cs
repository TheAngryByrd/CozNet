using System.Globalization;

sealed class CooperativeWorkPlan
{
    public int Batches { get; }
    public PhysicalWorkMode Mode { get; }

    private CooperativeWorkPlan(int batches, PhysicalWorkMode mode) => (Batches, Mode) = (batches, mode);

    public static CooperativeWorkPlan Parse(PhysicalWorkMode mode, string batches)
    {
        ArgumentNullException.ThrowIfNull(mode);
        if (!int.TryParse(batches, NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count <= 0)
            throw new ArgumentException("Select a positive fixed selected batch count.");
        return new(count, mode);
    }
}

sealed class CooperativeCpuWork
{
    public const string Protocol = "cooperative-checksum-v1";
    public const long ExpectedChecksum = 49995000;
    private readonly PhysicalWorkMode mode;
    private long selectedProgress, peerProgress, selectedChecksum, peerChecksum;
    private int peerStop;
    public long SelectedProgress => Interlocked.Read(ref selectedProgress);
    public long PeerProgress => Interlocked.Read(ref peerProgress);
    public long SelectedChecksum => Interlocked.Read(ref selectedChecksum);
    public long PeerChecksum => Interlocked.Read(ref peerChecksum);

    public CooperativeCpuWork(PhysicalWorkMode mode)
    {
        ArgumentNullException.ThrowIfNull(mode);
        this.mode = mode;
    }

    public static void Warmup()
    {
        Check(CpuWorkload.Run());
        Check(CpuWorkload.RunOptimized());
    }

    public void RunSelected() => RunSelected(new OriginalChecksumArithmetic(), new OptimizedChecksumArithmetic());

    internal void RunSelected<TOriginal, TOptimized>(TOriginal original, TOptimized optimized)
        where TOriginal : struct, IChecksumArithmetic where TOptimized : struct, IChecksumArithmetic
    {
        long result = mode.Optimized ? optimized.Run() : original.Run();
        Check(result);
        selectedChecksum = checked(selectedChecksum + result);
        Interlocked.Increment(ref selectedProgress);
    }

    public void RunPeer() => RunPeer(new OriginalChecksumArithmetic());

    internal void RunPeer<T>(T arithmetic) where T : struct, IChecksumArithmetic
    {
        while (Volatile.Read(ref peerStop) == 0)
        {
            long result = arithmetic.Run();
            Check(result);
            peerChecksum = checked(peerChecksum + result);
            Interlocked.Increment(ref peerProgress);
        }
    }

    public void RequestPeerStop() => Volatile.Write(ref peerStop, 1);

    private static void Check(long result)
    {
        if (result != ExpectedChecksum) throw new InvalidDataException("Cooperative checksum work changed its result.");
    }
}
