sealed class BoundCpuEpochStart
{
    public CpuCounterReading DisabledBaseline { get; }
    public CpuCounterTransition Enable { get; }
    public ulong Epoch => Enable.Epoch;

    private BoundCpuEpochStart(CpuCounterReading baseline, CpuCounterTransition enable)
        => (DisabledBaseline, Enable) = (baseline, enable);

    public static (BoundCpuSession Session, BoundCpuEpochStart Start) Parse(
        BoundCpuSession disabled, CpuCounterSourceSnapshot enabled)
    {
        var session = disabled.Enable(enabled);
        return (session, new(disabled.Current.LastReading!.Value, session.Current.Transitions[^1]));
    }
}

sealed record BoundCpuEpochPrefix(BoundCpuEpochStart Start, int? FirstSampleIndex,
    CpuSample? FirstSample, ulong? EnabledPrefixCpuNanoseconds);

static class BoundCpuEpochPrefixes
{
    public static IReadOnlyList<BoundCpuEpochPrefix> Parse(IReadOnlyList<BoundCpuEpochStart> starts,
        IReadOnlyList<BoundEpochSample> samples, BoundCpuSession session, CpuCounterSourceSnapshot final)
    {
        final = session.Close(final, session.Current.LastReading!.Value.CpuNanoseconds!.Value);
        var last = BoundCpuEpoch.ReadValid(final);
        if (final.Enabled || final.Epoch != (ulong)starts.Count || starts.Count == 0
            || final.Transitions.Count != checked(starts.Count * 2))
            throw new InvalidDataException("Epoch prefixes require every completed source epoch.");
        var result = new BoundCpuEpochPrefix[starts.Count];
        int sampleIndex = 0;
        ulong previousCount = 0;
        for (int index = 0; index < starts.Count; index++)
        {
            var start = starts[index];
            ulong epoch = checked((ulong)index + 1);
            ulong baseline = start.DisabledBaseline.CpuNanoseconds!.Value;
            ulong end = index + 1 < starts.Count
                ? starts[index + 1].DisabledBaseline.CpuNanoseconds!.Value : last.CpuNanoseconds!.Value;
            if (start.Epoch != epoch || start.DisabledBaseline.SourceIdentity != final.SourceIdentity
                || start.Enable != final.Transitions[index * 2] || baseline < previousCount || end < baseline
                || index == 0 && baseline != 0)
                throw new InvalidDataException("Epoch prefixes changed source, order, or counter boundaries.");
            int first = sampleIndex;
            ulong previousSample = baseline;
            while (sampleIndex < samples.Count && samples[sampleIndex].EligibilityEpoch == epoch)
            {
                var sample = samples[sampleIndex].Sample;
                if (sample.CumulativeCpuNanoseconds is not ulong count || count < previousSample || count > end)
                    throw new InvalidDataException("An epoch sample escaped its disabled counter boundaries.");
                previousSample = count;
                sampleIndex++;
            }
            if (sampleIndex < samples.Count && samples[sampleIndex].EligibilityEpoch < epoch)
                throw new InvalidDataException("Epoch sample order regressed.");
            var firstSample = first == sampleIndex ? null : samples[first].Sample;
            result[index] = new(start, firstSample is null ? null : first, firstSample,
                firstSample is null ? null : firstSample.CumulativeCpuNanoseconds!.Value - baseline);
            previousCount = end;
        }
        if (sampleIndex != samples.Count)
            throw new InvalidDataException("A sample has no completed source epoch.");
        return Array.AsReadOnly(result);
    }
}
