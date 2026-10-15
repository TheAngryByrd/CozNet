static class PausableCollectorChecks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private static T Await<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
    private static void Await(Task task) => task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
    private static void Wait(ManualResetEventSlim signal) => Require(signal.Wait(TimeSpan.FromSeconds(5)), "Controlled collector operation timed out.");
    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidDataException("An invalid collector transition was accepted.");
    }

    public static void Run()
    {
        PauseAcknowledgesFinalDrain();
        StopSupersedesPendingPause();
        DrainFailureReachesPauseAndCompletion();
        FinalDrainFailureCannotGrantPause();
        PauseIdentityAndOwnerAreRequired();
    }

    private static void PauseAcknowledgesFinalDrain()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int drains = 0, admissionEpoch = 1;
        var assignments = new System.Collections.Concurrent.ConcurrentQueue<int>();
        using var collector = new PausableSampleCollector(() =>
        {
            if (Interlocked.Increment(ref drains) == 1) { entered.Set(); Wait(release); }
            assignments.Enqueue(Volatile.Read(ref admissionEpoch));
        });
        PausableSampleCollector.Pause pause;
        try
        {
            Wait(entered);
            var pending = collector.RequestPause();
            Require(!pending.IsCompleted, "Pause acknowledged while a drain remained in flight.");
            Reject(() => collector.RequestPause());
            release.Set();
            pause = Await(pending);
            Require(drains == 2 && assignments.SequenceEqual(new[] { 1, 1 }), "Pause did not finish the old epoch's final drain.");
            Volatile.Write(ref admissionEpoch, 2);
            collector.Resume(pause);
            var second = Await(collector.RequestPause());
            Require(second.Sequence == pause.Sequence + 1 && assignments.Last() == 2, "Resume did not admit the next epoch.");
            Reject(() => collector.Resume(pause));
            int pausedDrains = drains;
            Await(collector.RequestStop());
            Require(drains == pausedDrains, "Stopping an acknowledged pause drained a closed epoch.");
        }
        finally { release.Set(); }
    }

    private static void StopSupersedesPendingPause()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var collector = new PausableSampleCollector(() => { entered.Set(); Wait(release); });
        try
        {
            Wait(entered);
            var pending = collector.RequestPause();
            var stopped = collector.RequestStop();
            Reject(() => Await(pending));
            Require(!stopped.IsCompleted, "Stop completed while a drain still owned the source.");
            release.Set();
            Await(stopped);
            Reject(() => collector.RequestPause());
        }
        finally { release.Set(); }
    }

    private static void DrainFailureReachesPauseAndCompletion()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var failure = new InvalidDataException("Controlled drain failure.");
        var collector = new PausableSampleCollector(() => { entered.Set(); Wait(release); throw failure; });
        try
        {
            Wait(entered);
            var pending = collector.RequestPause();
            release.Set();
            try { Await(pending); throw new Exception("Failed drain acknowledged a pause."); }
            catch (InvalidDataException observed) { Require(ReferenceEquals(observed, failure), "Pause replaced the drain failure."); }
            try { collector.Complete(); throw new Exception("Completion discarded the drain failure."); }
            catch (InvalidDataException observed) { Require(ReferenceEquals(observed, failure), "Completion replaced the drain failure."); }
        }
        finally
        {
            release.Set();
            try { collector.Dispose(); }
            catch (InvalidDataException error) when (ReferenceEquals(error, failure)) { }
        }
    }

    private static void PauseIdentityAndOwnerAreRequired()
    {
        using var first = new PausableSampleCollector(() => { });
        using var second = new PausableSampleCollector(() => { });
        Await(Task.Run(() => Reject(() => first.RequestPause())));
        var firstPause = Await(first.RequestPause());
        var secondPause = Await(second.RequestPause());
        Reject(() => first.Resume(secondPause));
        Await(Task.Run(() => Reject(() => first.Resume(firstPause))));
        Await(Task.Run(() => Reject(() => first.RequestStop())));
        first.Resume(firstPause);
        Reject(() => first.Resume(firstPause));
    }

    private static void FinalDrainFailureCannotGrantPause()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var failure = new InvalidDataException("Final drain rejected its source records.");
        int calls = 0;
        var collector = new PausableSampleCollector(() =>
        {
            if (Interlocked.Increment(ref calls) == 1) { entered.Set(); Wait(release); }
            else throw failure;
        });
        try
        {
            Wait(entered);
            var pending = collector.RequestPause();
            release.Set();
            try { Await(pending); throw new Exception("A failed final drain granted pause admission."); }
            catch (InvalidDataException observed) { Require(ReferenceEquals(observed, failure), "Final drain failure was replaced."); }
            try { collector.Complete(); throw new Exception("Completion accepted a failed final drain."); }
            catch (InvalidDataException observed) { Require(ReferenceEquals(observed, failure), "Completion lost the final drain failure."); }
        }
        finally
        {
            release.Set();
            try { collector.Dispose(); }
            catch (InvalidDataException error) when (ReferenceEquals(error, failure)) { }
        }
    }
}
