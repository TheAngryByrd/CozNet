using System.Collections;
using static CooperativeManagedChecks;

static class SampleBatchSnapshotCacheChecks
{
    private static SampleBatch Batch(IReadOnlyList<string> symbols) => new(1, 1, 1000, 0, 0, 0, 1, 0, symbols);
    private sealed class Symbols : IReadOnlyList<string>
    {
        public bool Fail;
        public readonly IOException Failure = new("Controlled symbol read failure.");
        public int Count => 1;
        public string this[int index] => Fail ? throw Failure : "selected";
        public IEnumerator<string> GetEnumerator()
        {
            if (Fail) throw Failure;
            return ((IEnumerable<string>)new[] { "selected" }).GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private static Exception? Error(Action action)
    {
        try { action(); return null; }
        catch (Exception error) { return error; }
    }
    public static void Run()
    {
        UnchangedBatchReusesFrozenSnapshotWithoutAllocation();
        MutableSymbolsCannotChangeEarlierSnapshots();
        SymbolOrderAndCaseRequireFreshSnapshots();
        ChangedMetadataCannotReuseAnEarlierSnapshot();
        SnapshotFailurePreservesThePriorEntry();
    }
    private static void UnchangedBatchReusesFrozenSnapshotWithoutAllocation()
    {
        foreach (string[] symbols in new[] { Array.Empty<string>(), new[] { "selected" } })
        {
            var cache = new SampleBatchSnapshotCache();
            var input = Batch(symbols);
            var expected = cache.Capture(input);
            for (int index = 0; index < 1000; index++) cache.Capture(input);
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool same = true;
            for (int index = 0; index < 10000; index++) same &= ReferenceEquals(expected, cache.Capture(input));
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Require(same && allocated == 0 && !ReferenceEquals(input, expected)
                && expected.SelectedSymbols.SequenceEqual(symbols),
                $"Stable batch snapshots must reuse frozen ownership without allocation; observed {allocated} bytes.");
        }
    }
    private static void MutableSymbolsCannotChangeEarlierSnapshots()
    {
        var symbols = new List<string> { "selected", "peer" };
        var input = Batch(symbols);
        var cache = new SampleBatchSnapshotCache();
        var first = cache.Capture(input);
        symbols[0] = "changed";
        var replaced = cache.Capture(input);
        symbols.RemoveAt(1);
        var shortened = cache.Capture(input);
        symbols.Clear();
        var empty = cache.Capture(input);
        symbols.Add("later");
        var grown = cache.Capture(input);
        Require(first.SelectedSymbols.SequenceEqual(new[] { "selected", "peer" })
            && replaced.SelectedSymbols.SequenceEqual(new[] { "changed", "peer" })
            && shortened.SelectedSymbols.SequenceEqual(new[] { "changed" }) && empty.SelectedSymbols.Count == 0
            && grown.SelectedSymbols.SequenceEqual(new[] { "later" }) && !ReferenceEquals(first, replaced)
            && ReferenceEquals(grown, cache.Capture(input)),
            "Source replacement, shrink, empty, or growth must not alter or reuse an obsolete frozen symbol list.");
        Require(Error(() => ((IList<string>)first.SelectedSymbols)[0] = "escaped") is NotSupportedException,
            "Published symbols must reject caller mutation.");
    }
    private static void SymbolOrderAndCaseRequireFreshSnapshots()
    {
        var symbols = new List<string> { "selected", "peer" };
        var input = Batch(symbols);
        var cache = new SampleBatchSnapshotCache();
        var first = cache.Capture(input);
        symbols.Reverse();
        var reversed = cache.Capture(input);
        Require(!ReferenceEquals(first, reversed)
            && first.SelectedSymbols.SequenceEqual(new[] { "selected", "peer" })
            && reversed.SelectedSymbols.SequenceEqual(new[] { "peer", "selected" }),
            "Reordering the same symbols must publish the new order and preserve the prior snapshot.");
        symbols[1] = "Selected";
        var recased = cache.Capture(input);
        Require(!ReferenceEquals(reversed, recased)
            && reversed.SelectedSymbols.SequenceEqual(new[] { "peer", "selected" })
            && recased.SelectedSymbols.SequenceEqual(new[] { "peer", "Selected" }),
            "A case-only symbol change must publish the exact spelling and preserve the prior snapshot.");
    }

    private static void ChangedMetadataCannotReuseAnEarlierSnapshot()
    {
        var input = Batch(new[] { "selected" });
        var cache = new SampleBatchSnapshotCache();
        var first = cache.Capture(input);
        var changed = input with { Samples = 2, LostRecords = 1, LostSamples = 3, Throttles = 1 };
        var next = cache.Capture(changed);
        Require(!ReferenceEquals(first, next) && next.Samples == 2 && next.LostRecords == 1
            && next.LostSamples == 3 && next.Throttles == 1 && first.Samples == 1 && first.LostSamples == 0,
            "A new source batch must retain changed counters, loss, and throttling without mutating prior evidence.");
    }
    private static void SnapshotFailurePreservesThePriorEntry()
    {
        var cache = new SampleBatchSnapshotCache();
        var symbols = new Symbols();
        var input = Batch(symbols);
        var first = cache.Capture(input);
        symbols.Fail = true;
        Require(ReferenceEquals(Error(() => cache.Capture(input)), symbols.Failure)
            && ReferenceEquals(Error(() => cache.Capture(Batch(symbols))), symbols.Failure),
            "Comparison and copy failures must preserve the original symbol exception.");
        Require(Error(() => cache.Capture(null!)) is ArgumentNullException
            && Error(() => cache.Capture(Batch(null!))) is ArgumentNullException,
            "Missing batches or symbol collections must reject before replacing cached state.");
        symbols.Fail = false;
        Require(ReferenceEquals(first, cache.Capture(input)) && first.SelectedSymbols.SequenceEqual(new[] { "selected" }),
            "Failed snapshots must retain the prior successful cache entry.");
    }
}
