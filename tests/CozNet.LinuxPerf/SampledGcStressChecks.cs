static class SampledGcStressChecks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Run()
    {
        ParserRejectsUnknownMode();
        CollectionMustCompleteInsideMeasurement();
        DurationBoundIncludesExactLimit();
        CancellationBeforeReleaseJoinsWithoutCollection();
    }

    private static void ParserRejectsUnknownMode()
    {
        Require(SampledSignalPlan.Parse("50", "qualified-sample", "warm", "gc").ForceGc, "The GC plan must enable stress.");
        Require(!SampledSignalPlan.Parse("0", "continuous", "cold").ForceGc, "Existing plans must retain the no-GC default.");
        bool rejected = false;
        try { SampledSignalPlan.Parse("50", "qualified-sample", "warm", "GC"); }
        catch (ArgumentException) { rejected = true; }
        Require(rejected, "An unknown GC mode must fail parsing.");
    }

    private static void CollectionMustCompleteInsideMeasurement()
    {
        Require(!SampledGcStress.ReachedMeasurement([], 100, 200), "No collection cannot prove GC reach.");
        Require(!SampledGcStress.ReachedMeasurement([new(99, 101, 1, [])], 100, 200), "A warmup collection cannot prove measurement reach.");
        Require(!SampledGcStress.ReachedMeasurement([new(199, 201, 1, [])], 100, 200), "An unfinished measurement collection cannot prove reach.");
        Require(!SampledGcStress.ReachedMeasurement([new(110, 109, 1, [])], 100, 200), "Backward timestamps cannot prove reach.");
        Require(SampledGcStress.ReachedMeasurement([new(100, 200, 1, [])], 100, 200), "A completed boundary collection must prove reach.");
    }

    private static void DurationBoundIncludesExactLimit()
    {
        Require(SampledGcStress.WithinBounds([new(100, 350, 1, [10])], 1000), "Exactly 250 ms must satisfy the call limit.");
        Require(!SampledGcStress.WithinBounds([new(100, 351, 1, [])], 1000), "A 251 ms collection must fail the call limit.");
        Require(!SampledGcStress.WithinBounds([new(100, 99, 1, [])], 1000), "Backward timestamps must fail.");
        Require(!SampledGcStress.WithinBounds([new(100, 101, 0, [])], 1000), "Missing collection identity must fail.");
        Require(!SampledGcStress.WithinBounds([new(100, 101, 1, [-1])], 1000), "Negative pause duration must fail.");
        Require(!SampledGcStress.WithinBounds([], 0), "An invalid clock frequency must fail.");
    }

    private static void CancellationBeforeReleaseJoinsWithoutCollection()
    {
        var worker = SampledGcStress.Start();
        try
        {
            bool rejected = false;
            try { worker.Snapshot(); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Observations require a joined worker.");
            Require(worker.StopAndJoin(TimeSpan.FromSeconds(1)), "A worker cancelled before release must join.");
            var result = worker.Snapshot();
            Require(result.Error is null && result.Observations.Length == 0, "Cancellation before release must not collect.");
            Require(worker.StopAndJoin(TimeSpan.Zero), "Repeated stop after join must succeed.");
        }
        finally { worker.StopAndJoin(TimeSpan.FromSeconds(1)); }
    }
}
