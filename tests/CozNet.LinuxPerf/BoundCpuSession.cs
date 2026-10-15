sealed class BoundCpuSession
{
    public CpuCounterSourceSnapshot Initial { get; }
    public CpuCounterSourceSnapshot Current { get; }

    private BoundCpuSession(CpuCounterSourceSnapshot initial, CpuCounterSourceSnapshot current)
        => (Initial, Current) = (initial, current);

    public static BoundCpuSession ParseFresh(CpuCounterSourceSnapshot initial)
    {
        var fresh = BoundCpuEpoch.ParseFresh(initial).Initial;
        return new(fresh, fresh);
    }

    public BoundCpuSession Enable(CpuCounterSourceSnapshot snapshot) => Advance(snapshot, true, null);
    public BoundCpuSession Disable(CpuCounterSourceSnapshot snapshot, ulong cpuNanoseconds) => Advance(snapshot, false, cpuNanoseconds);

    private BoundCpuSession Advance(CpuCounterSourceSnapshot snapshot, bool enabling, ulong? expectedCpu)
    {
        snapshot = snapshot with { Transitions = Array.AsReadOnly(snapshot.Transitions.ToArray()) };
        var reading = BoundCpuEpoch.ReadValid(snapshot);
        var previous = Current.LastReading!.Value;
        ulong nextEpoch = enabling ? checked(Current.Epoch + 1) : Current.Epoch;
        if (Current.Enabled == enabling || snapshot.SourceIdentity != Initial.SourceIdentity || snapshot.Enabled != enabling
            || snapshot.Epoch != nextEpoch || snapshot.Transitions.Count != Current.Transitions.Count + 1
            || !snapshot.Transitions.Take(Current.Transitions.Count).SequenceEqual(Current.Transitions)
            || reading.TimestampFrequency != previous.TimestampFrequency || reading.CpuNanoseconds < previous.CpuNanoseconds
            || (expectedCpu is { } expected && reading.CpuNanoseconds != expected))
            throw new InvalidDataException("The bound CPU session changed source, counter, or transition ownership.");
        var transition = snapshot.Transitions[^1];
        if (transition.Epoch != nextEpoch || transition.Enabling != enabling || !transition.Completed || transition.Error is not null
            || transition.TimestampFrequency != previous.TimestampFrequency || transition.Before < previous.After
            || transition.After < transition.Before || reading.Before < transition.After)
            throw new InvalidDataException("The bound CPU session has an invalid transition boundary.");
        return new(Initial, snapshot);
    }

    public CpuCounterSourceSnapshot Close(CpuCounterSourceSnapshot snapshot, ulong cpuNanoseconds)
    {
        snapshot = snapshot with { Transitions = Array.AsReadOnly(snapshot.Transitions.ToArray()) };
        var reading = BoundCpuEpoch.ReadValid(snapshot);
        var previous = Current.LastReading!.Value;
        if (Current.Enabled || Current.Epoch == 0 || snapshot.Enabled || snapshot.Epoch != Current.Epoch
            || snapshot.SourceIdentity != Initial.SourceIdentity || !snapshot.Transitions.SequenceEqual(Current.Transitions)
            || reading.TimestampFrequency != previous.TimestampFrequency || reading.CpuNanoseconds != previous.CpuNanoseconds
            || reading.CpuNanoseconds != cpuNanoseconds || (reading != previous && reading.Before < previous.After))
            throw new InvalidDataException("The bound CPU session did not close its final disabled epoch.");
        return snapshot;
    }
}
