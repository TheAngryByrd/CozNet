sealed class BoundSampleAggregate
{
    private readonly HashSet<ulong> pointers = [];
    private readonly HashSet<string> symbols = new(StringComparer.Ordinal);
    private int count, selected, unmapped;
    private ulong represented, maximumCounter;

    public void Add(CpuSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        int nextCount = checked(count + 1);
        ulong nextRepresented = checked(represented + sample.PeriodNanoseconds);
        int nextSelected = checked(selected + (sample.Method == OverheadWorkload.SelectedSymbol ? 1 : 0));
        int nextUnmapped = checked(unmapped + (sample.Method is null ? 1 : 0));
        pointers.Add(sample.InstructionPointer);
        if (sample.Method is not null) symbols.Add(sample.Method);
        count = nextCount;
        represented = nextRepresented;
        selected = nextSelected;
        unmapped = nextUnmapped;
        maximumCounter = Math.Max(maximumCounter, sample.CumulativeCpuNanoseconds ?? 0);
    }

    public void Validate(SampleBatch batch, ulong cpuNanoseconds)
    {
        if (batch.LostRecords != 0 || batch.LostSamples != 0 || batch.Throttles != 0
            || batch.Samples != count || batch.SelectedMethodSamples != selected || batch.UnmappedSamples != unmapped
            || batch.DistinctInstructionPointers != pointers.Count || batch.RepresentedCpuNanoseconds != represented
            || maximumCounter > cpuNanoseconds || !batch.SelectedSymbols.SequenceEqual(symbols.Order()))
            throw new InvalidDataException("The bound sample epoch lost records or changed sample accounting.");
    }
}
