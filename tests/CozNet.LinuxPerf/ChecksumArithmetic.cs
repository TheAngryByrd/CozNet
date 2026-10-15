interface IChecksumArithmetic
{
    long Run();
}

readonly struct OriginalChecksumArithmetic : IChecksumArithmetic
{
    public long Run() => CpuWorkload.Run();
}

readonly struct OptimizedChecksumArithmetic : IChecksumArithmetic
{
    public long Run() => CpuWorkload.RunOptimized();
}

readonly struct ControlChecksumArithmetic : IChecksumArithmetic
{
    public long Run() => ConstantChecksum();

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static long ConstantChecksum() => MatchedCpuWorkers.ExpectedChecksum;
}
