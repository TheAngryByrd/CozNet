static class OwnerCollectorChecks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private static Exception Fails(Action action)
    {
        try { action(); }
        catch (Exception error) { return error; }
        throw new InvalidDataException("The invalid owner collector operation succeeded.");
    }

    public static void Run()
    {
        ExplicitDrainsAndPauseIdentity();
        ForeignThreadCannotDrainOrControl();
        RecursiveOperationsAreRejected();
        FailurePreservesOriginalException();
        CompletionDrainsOnlyRunningCollectors();
    }

    private static void ExplicitDrainsAndPauseIdentity()
    {
        int drains = 0, owner = Environment.CurrentManagedThreadId;
        using var collector = new OwnerSampleCollector(() =>
        {
            Require(Environment.CurrentManagedThreadId == owner, "Drain left its owner thread.");
            drains++;
        });
        Require(drains == 0, "Construction performed an unsolicited drain.");
        collector.Drain();
        var first = collector.RequestPause().GetAwaiter().GetResult();
        Require(drains == 2, "Pause did not perform exactly one final drain.");
        Require(Fails(collector.Drain) is InvalidOperationException, "Paused draining was accepted.");
        Require(Fails(() => collector.Resume(new(first.Sequence))) is InvalidOperationException,
            "A forged pause token resumed collection.");
        collector.Resume(first);
        var second = collector.RequestPause().GetAwaiter().GetResult();
        Require(second.Sequence == first.Sequence + 1, "Pause sequence did not advance.");
        Require(Fails(() => collector.Resume(first)) is InvalidOperationException, "A stale pause resumed collection.");
        collector.Complete();
        Require(drains == 3, "Completion drained a paused collector.");
        Require(Fails(() => collector.Resume(second)) is InvalidOperationException, "Completion retained pause authority.");
    }

    private static void ForeignThreadCannotDrainOrControl()
    {
        int drains = 0;
        using var collector = new OwnerSampleCollector(() => drains++);
        var pause = collector.RequestPause().GetAwaiter().GetResult();
        Task.Run(() =>
        {
            foreach (Action action in new Action[] { collector.Drain, () => collector.RequestPause(),
                () => collector.Resume(pause), collector.Complete, collector.Dispose })
                Require(Fails(action) is InvalidOperationException, "A foreign thread controlled collection.");
        }).GetAwaiter().GetResult();
        Require(drains == 1, "A foreign thread drained the source.");
        collector.Resume(pause);
    }

    private static void RecursiveOperationsAreRejected()
    {
        OwnerSampleCollector? collector = null;
        collector = new(() =>
        {
            foreach (Action action in new Action[] { collector!.Drain, () => collector.RequestPause(),
                collector.Complete, collector.Dispose })
                Require(Fails(action) is InvalidOperationException, "Recursive collector control succeeded.");
        });
        collector.Drain();
        collector.Complete();
    }

    private static void FailurePreservesOriginalException()
    {
        var original = new IOException("Controlled owner drain failure.");
        int attempts = 0;
        var collector = new OwnerSampleCollector(() => { attempts++; throw original; });
        Require(ReferenceEquals(Fails(() => collector.RequestPause()), original), "Pause replaced the drain failure.");
        foreach (Action action in new Action[] { collector.Drain, () => collector.RequestPause(),
            () => collector.Resume(new(1)), collector.Complete, collector.Dispose })
            Require(ReferenceEquals(Fails(action), original), "Collector replaced its original failure.");
        Require(attempts == 1, "Failed collection retried a drain.");
    }

    private static void CompletionDrainsOnlyRunningCollectors()
    {
        int drains = 0;
        var collector = new OwnerSampleCollector(() => drains++);
        collector.Complete();
        collector.Complete();
        collector.Dispose();
        Require(drains == 1, "Completion repeated the final drain.");
        Require(Fails(collector.Drain) is InvalidOperationException, "Stopped collection drained again.");
    }
}
