using static CooperativeManagedChecks;

static class BoundSampleAggregateChecks
{
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    public static void Run()
    {
        FullValidatorParity();
        var aggregate = new BoundSampleAggregate();
        aggregate.Validate(new(0, 0, 0, 0, 0, 0, 0, 0, Array.Empty<string>()), 0);
        aggregate.Add(CpuSample.Create(1, 2, 1, 3, 100, OverheadWorkload.SelectedSymbol, 5));
        aggregate.Validate(new(1, 1, 3, 0, 0, 0, 1, 0, new[] { OverheadWorkload.SelectedSymbol }), 5);
        aggregate.Add(CpuSample.Create(1, 2, 2, 5, 100, OverheadWorkload.SelectedSymbol, 9));
        aggregate.Add(CpuSample.Create(1, 2, 3, 7, 200, null, 15));
        var valid = new SampleBatch(3, 2, 15, 0, 0, 0, 2, 1, new[] { OverheadWorkload.SelectedSymbol });
        aggregate.Validate(valid, 15);
        foreach (var invalid in new[]
        {
            valid with { Samples = 2 }, valid with { DistinctInstructionPointers = 3 },
            valid with { RepresentedCpuNanoseconds = 14 }, valid with { LostRecords = 1 },
            valid with { LostSamples = 1 }, valid with { Throttles = 1 },
            valid with { SelectedMethodSamples = 1 }, valid with { UnmappedSamples = 2 },
            valid with { SelectedSymbols = Array.Empty<string>() },
            valid with { SelectedSymbols = new[] { "foreign" } },
            valid with { SelectedSymbols = new[] { OverheadWorkload.SelectedSymbol, OverheadWorkload.SelectedSymbol } }
        }) Reject<InvalidDataException>(() => aggregate.Validate(invalid, 15));
        Reject<InvalidDataException>(() => aggregate.Validate(valid, 14));
        Reject<OverflowException>(() => aggregate.Add(CpuSample.Create(1, 2, 4, ulong.MaxValue, 300, "foreign", 16)));
        Reject<ArgumentNullException>(() => aggregate.Add(null!));
        aggregate.Validate(valid, 15);
        Require(valid.SelectedSymbols.Count == 1, "Aggregate validation changed source symbols.");
    }

    private static void FullValidatorParity()
    {
        var full = typeof(BoundSampleCollection).GetMethod("ValidateBatch",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .CreateDelegate<Action<SampleBatch, IReadOnlyList<CpuSample>, ulong>>();
        CpuSample[] samples =
        [
            CpuSample.Create(1, 2, 1, 3, 100, OverheadWorkload.SelectedSymbol, 5),
            CpuSample.Create(1, 2, 2, 5, 200, null, 9),
            CpuSample.Create(1, 2, 3, 7, 100, "z", 4),
            CpuSample.Create(1, 2, 4, 11, 300, "a")
        ];
        var aggregate = new BoundSampleAggregate();
        foreach (var sample in samples) aggregate.Add(sample);
        var batch = new SampleBatch(4, 3, 26, 0, 0, 0, 1, 1, new[] { "z", "a", OverheadWorkload.SelectedSymbol }.Order().ToArray());
        aggregate.Validate(batch, 9);
        full(batch, samples, 9);
        Reject<InvalidDataException>(() => aggregate.Validate(batch, 8));
        Reject<InvalidDataException>(() => full(batch, samples, 8));
        var reversed = batch with { SelectedSymbols = batch.SelectedSymbols.Reverse().ToArray() };
        Reject<InvalidDataException>(() => aggregate.Validate(reversed, 9));
        Reject<InvalidDataException>(() => full(reversed, samples, 9));
    }
}
