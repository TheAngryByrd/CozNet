sealed record CpuSample
{
    public uint ProcessId { get; }
    public uint ThreadId { get; }
    public ulong Timestamp { get; }
    public ulong PeriodNanoseconds { get; }
    public ulong InstructionPointer { get; }
    public string? Method { get; }

    private CpuSample(uint processId, uint threadId, ulong timestamp, ulong period,
        ulong instructionPointer, string? method)
    {
        ProcessId = processId;
        ThreadId = threadId;
        Timestamp = timestamp;
        PeriodNanoseconds = period;
        InstructionPointer = instructionPointer;
        Method = method;
    }

    public static CpuSample Create(uint processId, uint threadId, ulong timestamp, ulong period,
        ulong instructionPointer, string? method)
    {
        if (processId == 0 || threadId == 0 || period == 0 || instructionPointer == 0)
            throw new InvalidDataException("Invalid CPU sample identity, period, or instruction address.");
        return new CpuSample(processId, threadId, timestamp, period, instructionPointer, method);
    }
}

interface ICpuSampleSink
{
    void Publish(CpuSample sample);
}

sealed class SampleCapture(ICpuSampleSink? downstream = null) : ICpuSampleSink
{
    private readonly List<CpuSample> samples = new();
    private int published;

    public int Published => Volatile.Read(ref published);

    public void Publish(CpuSample sample)
    {
        if (samples.Count == 10000)
            throw new InvalidOperationException("The capability workload exceeded its sample capture limit.");
        downstream?.Publish(sample);
        samples.Add(sample);
        Volatile.Write(ref published, samples.Count);
    }

    public CpuSample[] AfterCollectorJoin() => samples.ToArray();
}
