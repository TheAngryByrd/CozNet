sealed class TimerPathPreparation : ITimerTransport
{
    private readonly TimerObservation?[] observations = new TimerObservation?[2];
    private readonly ulong[] generations = new ulong[2];
    public (int Status, ulong Generation)? LastSubmission { get; private set; }
    public TimerNativeCapture? LastRaw { get; private set; }

    public ulong Submit(uint slot, ulong command, ulong budget)
    {
        if (observations[slot] is not null) throw new InvalidOperationException("The synthetic timer slot is occupied.");
        ulong generation = ++generations[slot];
        ulong wait = budget + (slot == 1 ? 30000UL : 0);
        var raw = new TargetLeaseRawChild(1, Math.Min(50000UL, budget), 1000, 2000, 2000 + wait, wait, 100, 0, true);
        var receipt = new TargetLeaseReceivedChild(raw, raw.Finished + 1, raw.Finished + 2,
            ServiceReceiptTiming.Parse(raw.Armed, raw.Finished, raw.Finished + 1, raw.Finished + 2, 0));
        var child = new TimerNativeChild { Ordinal = 1, Requested = raw.Requested, Armed = raw.Armed, Started = raw.Started,
            Finished = raw.Finished, Waited = wait, Ip = 100, Valid = 1 };
        var header = new TimerNativeSnapshot { Generation = generation, Command = command, Budget = budget, Children = 1,
            Measured = wait, Visibility = 3, Stop = 1, Captured = raw.Finished + 2 };
        observations[slot] = new(new(header, [child], new(0, header.Captured + 1)), [receipt]);
        LastSubmission = (0, generation);
        return generation;
    }
    public TimerNativeSnapshot Probe(uint slot) => Current(slot).Raw.Header;
    public TimerObservation Read(uint slot)
    {
        var observation = Current(slot);
        LastRaw = observation.Raw;
        return observation;
    }
    public void Cancel(uint slot) => throw new NotSupportedException("Synthetic completed timers do not support cancellation.");
    public void ReleaseAfterRetiredCopy(uint slot)
    {
        Current(slot);
        observations[slot] = null;
    }
    private TimerObservation Current(uint slot) => observations[slot] ?? throw new InvalidOperationException("The synthetic timer slot is empty.");
}
