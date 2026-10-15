sealed class BoundPauseTiming
{
    public long? Started { get; internal set; }
    public long? ProducerStopped { get; internal set; }
    public long? DisabledCountersAccepted { get; internal set; }
    public long? DrainAcknowledged { get; internal set; }
    public long? BatchValidated { get; internal set; }
    public long? SnapshotReady { get; internal set; }
    public long? SamplesPublished { get; internal set; }
    public long? CallbackReturned { get; internal set; }
    public long? SourceEnabled { get; internal set; }
    public long? EnabledCountersAccepted { get; internal set; }
    public long? CollectorResumed { get; internal set; }
    public long? Completed { get; internal set; }
}
