using System.Numerics;

sealed class CalibrationBatchSize
{
    public static CalibrationBatchSize Contiguous { get; } = new(50000);
    public static CalibrationBatchSize Interleaved { get; } = new(500);
    public int Calls { get; }
    private CalibrationBatchSize(int calls) => Calls = calls;
}

sealed class CompleteCalibrationSegment
{
    public PhysicalCalibration.Measurement Raw { get; }
    public long CpuNanoseconds { get; }

    private CompleteCalibrationSegment(PhysicalCalibration.Measurement raw, long cpuNanoseconds)
    {
        Raw = raw;
        CpuNanoseconds = cpuNanoseconds;
    }

    public static CompleteCalibrationSegment? TryCreate(PhysicalCalibration.Measurement? raw, CalibrationBatchSize size)
    {
        if (raw is null || raw.Error is not null || raw.CompletedBatches != size.Calls
            || raw.Checksum != (long)size.Calls * MatchedCpuWorkers.ExpectedChecksum
            || raw.Frequency <= 0 || raw.BeforeTimestamp is not { } before || raw.AfterTimestamp is not { } after
            || after <= before || raw.CpuBefore is not { } cpuBefore || cpuBefore < 0
            || raw.CpuAfter is not { } cpuAfter || cpuAfter <= cpuBefore) return null;
        long cpu = cpuAfter - cpuBefore;
        if ((BigInteger)cpu * raw.Frequency > ((BigInteger)after - before) * 1000000000) return null;
        return new(raw, cpu);
    }
}
