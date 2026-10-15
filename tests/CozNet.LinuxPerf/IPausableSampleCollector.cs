sealed class CollectorPause(ulong sequence)
{
    public ulong Sequence { get; } = sequence;
}

interface IPausableSampleCollector : IDisposable
{
    Task<CollectorPause> RequestPause();
    void Resume(CollectorPause admission);
    void Complete();
}
