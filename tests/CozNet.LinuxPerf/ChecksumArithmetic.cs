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
