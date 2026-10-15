static class MatchedCpuWorkersChecks
{
    public static void Run()
    {
        InvalidModeIsRejected();
        StopBeforeReleaseProducesNoProgress();
        BothWorkersPublishProgress();
        OnlySelectedWorkerUsesOptimizedArithmetic();
        WrongChecksumCannotPublishProgress();
    }

    private sealed class Calls { public int Count; }
    private readonly struct Arithmetic(Calls calls, MatchedCpuWorkers work, long result) : IChecksumArithmetic
    {
        public long Run()
        {
            calls.Count++;
            work.RequestStop();
            return result;
        }
    }

    private static void OnlySelectedWorkerUsesOptimizedArithmetic()
    {
        foreach (string mode in new[] { "baseline", "optimized" })
        foreach (int slot in new[] { 0, 1 })
        {
            using var work = new MatchedCpuWorkers(PhysicalWorkMode.Parse(mode));
            var original = new Calls();
            var optimized = new Calls();
            work.Release();
            work.Run(slot, new Arithmetic(original, work, 49995000), new Arithmetic(optimized, work, 49995000));
            bool expectedOptimized = mode == "optimized" && slot == 0;
            Require(original.Count == (expectedOptimized ? 0 : 1) && optimized.Count == (expectedOptimized ? 1 : 0)
                && work.Progress(slot) == 1 && work.Progress(1 - slot) == 0,
                $"Mode {mode}, slot {slot} must call only its declared arithmetic and publish one verified batch.");
            work.DisposeReady(0);
            work.DisposeReady(1);
        }
    }

    private static void WrongChecksumCannotPublishProgress()
    {
        using var work = new MatchedCpuWorkers(PhysicalWorkMode.Baseline);
        var calls = new Calls();
        work.Release();
        Exception? failure = null;
        try { work.Run(0, new Arithmetic(calls, work, 0), new OptimizedChecksumArithmetic()); }
        catch (InvalidDataException error) { failure = error; }
        Require(failure is not null && calls.Count == 1 && work.Progress(0) == 0 && work.Progress(1) == 0,
            "A wrong checksum must fail before any progress is published.");
        work.DisposeReady(0);
        work.DisposeReady(1);
    }

    private static void InvalidModeIsRejected()
    {
        Require(!PhysicalWorkMode.Parse("baseline").Optimized && PhysicalWorkMode.Parse("optimized").Optimized,
            "Physical modes must preserve the selected implementation.");
        try { PhysicalWorkMode.Parse("half"); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("An unsupported physical mode must be rejected.");
    }

    private static void StopBeforeReleaseProducesNoProgress()
    {
        using var work = new MatchedCpuWorkers(PhysicalWorkMode.Baseline);
        work.RequestStop();
        work.Run(0);
        work.Run(1);
        Require(work.Progress(0) == 0 && work.Progress(1) == 0,
            "Stopping before release must unblock both workers without completing any batch.");
        work.DisposeReady(0);
        work.DisposeReady(1);
    }

    private static void BothWorkersPublishProgress()
    {
        foreach (string mode in new[] { "baseline", "optimized" })
        {
            var work = new MatchedCpuWorkers(PhysicalWorkMode.Parse(mode));
            Exception?[] errors = new Exception?[2];
            Thread[] threads = Enumerable.Range(0, 2).Select(slot => new Thread(() =>
            {
                try { work.SignalReady(slot); work.Run(slot); }
                catch (Exception error) { Volatile.Write(ref errors[slot], error); }
            }) { IsBackground = true }).ToArray();
            bool[] joined = new bool[2];
            try
            {
                foreach (var thread in threads) thread.Start();
                Require(work.WaitReady(0, TimeSpan.FromSeconds(2)) && work.WaitReady(1, TimeSpan.FromSeconds(2)),
                    "Both matched workers must reach the release gate.");
                Require(work.Progress(0) == 0 && work.Progress(1) == 0,
                    "Workers must not publish progress before release.");
                work.Release();
                Require(SpinWait.SpinUntil(() => work.Progress(0) >= 2 && work.Progress(1) >= 2, TimeSpan.FromSeconds(5)),
                    "Both workers must publish checksum-verified progress after release.");
            }
            finally
            {
                work.RequestStop();
                for (int slot = 0; slot < 2; slot++)
                {
                    joined[slot] = threads[slot].Join(TimeSpan.FromSeconds(2));
                    if (joined[slot]) work.DisposeReady(slot);
                }
                if (joined.All(value => value)) work.Dispose();
            }
            Require(joined.All(value => value) && errors.All(error => error is null),
                "Matched workers must stop and join without checksum or gate failures.");
            var final = work.SnapshotProgress();
            Require(final[0] >= 2 && final[1] >= 2,
                "Joining must retain separate selected and peer progress.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
