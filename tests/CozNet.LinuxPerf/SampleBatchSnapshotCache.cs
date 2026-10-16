sealed class SampleBatchSnapshotCache
{
    private SampleBatch? source;
    private SampleBatch? snapshot;

    public SampleBatch Capture(SampleBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(batch.SelectedSymbols);
        if (ReferenceEquals(batch, source) && SameSymbols(batch.SelectedSymbols, snapshot!.SelectedSymbols))
            return snapshot;
        var next = batch with { SelectedSymbols = Array.AsReadOnly(batch.SelectedSymbols.ToArray()) };
        source = batch;
        snapshot = next;
        return next;
    }

    private static bool SameSymbols(IReadOnlyList<string> current, IReadOnlyList<string> frozen)
    {
        if (current.Count != frozen.Count) return false;
        for (int index = 0; index < current.Count; index++)
            if (!string.Equals(current[index], frozen[index], StringComparison.Ordinal)) return false;
        return true;
    }
}
