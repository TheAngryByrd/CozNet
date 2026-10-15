sealed class BoundCpuEpoch
{
    public CpuCounterSourceSnapshot Initial { get; }
    private BoundCpuEpoch(CpuCounterSourceSnapshot initial) => Initial = initial;
    internal static CpuCounterReading ReadValid(CpuCounterSourceSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.SourceIdentity) || snapshot.Units != "task-clock-nanoseconds"
            || snapshot.Overflowed || snapshot.UnrecordedCleanup || snapshot.FailedReads != 0 || snapshot.FirstReadFailure is not null
            || snapshot.LastReading is not { } reading || reading.SourceIdentity != snapshot.SourceIdentity
            || reading.Error is not null || reading.CpuNanoseconds is null || reading.TimestampFrequency <= 0
            || reading.Before < 0 || reading.After < reading.Before || reading.Enabled != snapshot.Enabled || reading.Epoch != snapshot.Epoch)
            throw new InvalidDataException("The bound CPU epoch lacks valid counter evidence.");
        return reading;
    }
    public static BoundCpuEpoch ParseFresh(CpuCounterSourceSnapshot initial)
    {
        initial = initial with { Transitions = CpuCounterHistory.Freeze(initial.Transitions) };
        var reading = ReadValid(initial);
        if (initial.Enabled || initial.Epoch != 0 || initial.Transitions.Count != 0 || reading.CpuNanoseconds != 0)
            throw new InvalidDataException("A bound CPU epoch requires a new disabled source with a zero counter.");
        return new(initial);
    }
    public CpuCounterSourceSnapshot Close(CpuCounterSourceSnapshot final, ulong cpuNanoseconds)
    {
        final = final with { Transitions = CpuCounterHistory.Freeze(final.Transitions) };
        var reading = ReadValid(final);
        if (final.SourceIdentity != Initial.SourceIdentity || final.Enabled || final.Epoch != 1
            || final.Transitions.Count != 2 || reading.CpuNanoseconds != cpuNanoseconds)
            throw new InvalidDataException("The bound CPU epoch changed source or enable/disable ownership.");
        long previous = Initial.LastReading!.Value.After;
        long frequency = Initial.LastReading.Value.TimestampFrequency;
        for (int index = 0; index < 2; index++)
        {
            var transition = final.Transitions[index];
            if (transition.Epoch != 1 || transition.Enabling != (index == 0) || !transition.Completed || transition.Error is not null
                || transition.TimestampFrequency != frequency || transition.Before < previous || transition.After < transition.Before)
                throw new InvalidDataException("The bound CPU epoch has an invalid transition journal.");
            previous = transition.After;
        }
        if (reading.TimestampFrequency != frequency || reading.Before < previous)
            throw new InvalidDataException("The final counter reading precedes disable completion.");
        return final;
    }
}
