static class CooperativeDispatchTimingChecks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Run()
    {
        CompletedDispatchRetainsThreePhasesAndIdentity();
        FailuresPreserveExceptionAndIncompleteSnapshot();
        RecentHistoryIsBoundedAndMaximaSurviveEviction();
        PrefixSurvivesEvictionWithoutExposingMutableStorage();
        DistinctPhaseMaximaUseTheirOwnIntervals();
        ActiveTimeoutRetainsStartedWithoutLateCompletion();
        WorkerFaultKeepsItsExistingExceptionShape();
        DisabledAndInvalidModesDoNotCreateCapture();
        ForeignThreadCannotDispatchOrChangeEvidence();
    }

    private static void CompletedDispatchRetainsThreePhasesAndIdentity()
    {
        var ticks = new Queue<long>(new long[] { 10, 20, 40, 70 });
        var timing = new CooperativeDispatchTiming(ticks.Dequeue);
        int result = timing.Run(CooperativeDispatchOperation.PeerRead, 7, 9, () => 42, action => action());
        var expected = new CooperativeDispatchObservation(1, CooperativeDispatchOperation.PeerRead, 7, 9, 10, 20, 40, 70, false);
        Require(result == 42 && timing.Recent.Single() == expected && timing.Calls == 1 && timing.Failed == 0
            && timing.Incomplete == 0 && timing.Longest == expected && timing.LongestQueue == expected
            && timing.LongestAction == expected && timing.LongestReturn == expected,
            "Dispatch timing changed its result, phases, request, or maxima.");
    }

    private static void FailuresPreserveExceptionAndIncompleteSnapshot()
    {
        long tick = 0;
        var timing = new CooperativeDispatchTiming(() => ++tick);
        var original = new TimeoutException("dispatch timed out");
        Func<int>? delayed = null;
        Exception? observed = null;
        try { timing.Run(CooperativeDispatchOperation.PeerHeader, 3, 4, () => 5, action => { delayed = action; throw original; }); }
        catch (Exception error) { observed = error; }
        var frozen = timing.Recent.Single();
        Require(ReferenceEquals(original, observed) && frozen.Started is null && frozen.Finished is null
            && frozen.Failed && timing.Incomplete == 1 && timing.Failed == 1 && timing.LongestQueue is null,
            "A dispatch failure changed its exception or claimed controller execution.");
        Require(delayed!() == 5 && timing.Recent.Single() == frozen,
            "Late controller execution changed the frozen failure snapshot.");
        var actionError = new InvalidDataException("action failed");
        observed = null;
        try { timing.Run<int>(CooperativeDispatchOperation.SelectedRead, 8, 9, () => throw actionError, action => action()); }
        catch (Exception error) { observed = error; }
        Require(ReferenceEquals(actionError, observed) && timing.Recent[^1].Finished is not null
            && timing.Recent[^1].Failed && timing.Calls == 2 && timing.Failed == 2 && timing.Incomplete == 1,
            "An action failure changed its exception or lost its completed timing bracket.");
    }

    private static void RecentHistoryIsBoundedAndMaximaSurviveEviction()
    {
        long tick = 0;
        var timing = new CooperativeDispatchTiming(() => tick++);
        timing.Run(CooperativeDispatchOperation.PeerRead, 1, 1, () => { tick += 1000; return 0; }, action => action());
        var longest = timing.Recent.Single();
        for (int index = 0; index < 130; ++index)
            timing.Run(CooperativeDispatchOperation.PeerHeader, 2, 2, () => 0, action => action());
        Require(timing.Calls == 131 && timing.Recent.Count == 128 && timing.Recent[0].Sequence == 4
            && timing.Recent[^1].Sequence == 131 && timing.Longest == longest && timing.LongestAction == longest,
            "Recent history exceeded its bound, lost order, or evicted a maximum.");
    }

    private static void PrefixSurvivesEvictionWithoutExposingMutableStorage()
    {
        long tick = 0;
        var timing = new CooperativeDispatchTiming(() => ++tick);
        Require(timing.PrefixEvidenceVersion == 1 && timing.Prefix.Count == 0,
            "New dispatch capture did not expose an empty versioned prefix.");
        var original = new TimeoutException("prefix dispatch failed");
        try
        {
            timing.Run<int>(CooperativeDispatchOperation.PeerSubmit, 7, 0, () => 1, _ => throw original);
        }
        catch (TimeoutException error) when (ReferenceEquals(error, original)) { }
        var first = timing.Prefix.Single();
        Require(first.Failed && first.Started is null && first.Finished is null && first.Command == 7,
            "The prefix lost its first failed submit.");
        for (int index = 1; index < 32; ++index)
            timing.Run(CooperativeDispatchOperation.PeerRead, 7, 9, () => 0, action => action());
        var snapshot = timing.Prefix.ToArray();
        for (int index = 32; index < 200; ++index)
            timing.Run(CooperativeDispatchOperation.PeerRelease, 8, 10, () => { tick += 100; return 0; }, action => action());
        Require(timing.Prefix.SequenceEqual(snapshot) && timing.Prefix.Count == 32
            && timing.Prefix.Select(item => item.Sequence).SequenceEqual(Enumerable.Range(1, 32).Select(value => (long)value))
            && timing.Recent[0].Sequence == 73 && timing.Longest?.Sequence == 33,
            "Later calls or maxima displaced the bounded dispatch prefix.");
        var exposed = (CooperativeDispatchObservation[])timing.Prefix;
        exposed[0] = exposed[1];
        Require(timing.Prefix[0] == first, "A caller mutated the retained dispatch prefix.");
    }

    private static void WorkerFaultKeepsItsExistingExceptionShape()
    {
        var timing = CooperativeDispatchTiming.Parse("1")!;
        var worker = new CooperativeInteropChecks.Worker();
        var original = new InvalidDataException("worker failed");
        AggregateException? observed = null;
        try
        {
            try { timing.Run<int>(CooperativeDispatchOperation.Create, 0, 0, () => throw original, action => worker.Run(action)); }
            catch (AggregateException error) { observed = error; }
            Require(observed?.InnerExceptions.Count == 1 && ReferenceEquals(observed.InnerException, original)
                && timing.Recent.Single().Finished is not null && timing.Failed == 1,
                "Timing changed the worker fault shape or lost its action bracket.");
        }
        finally { Require(worker.Join(), "The dispatch check worker did not join."); }
    }

    private static void DistinctPhaseMaximaUseTheirOwnIntervals()
    {
        var ticks = new Queue<long>(new long[] { 0, 100, 101, 102, 200, 201, 301, 302, 400, 401, 402, 602 });
        long tail = 1000;
        var timing = new CooperativeDispatchTiming(() => ticks.Count > 0 ? ticks.Dequeue() : tail++);
        for (int index = 0; index < 3; ++index)
            timing.Run(CooperativeDispatchOperation.PeerHeader, 1, 1, () => 0, action => action());
        Require(timing.LongestQueue?.Sequence == 1 && timing.LongestAction?.Sequence == 2
            && timing.LongestReturn?.Sequence == 3 && timing.Longest?.Sequence == 3,
            "Dispatch maxima used the wrong timing phase.");
        for (int index = 0; index < 130; ++index)
            timing.Run(CooperativeDispatchOperation.PeerHeader, 2, 2, () => 0, action => action());
        Require(timing.LongestQueue?.Sequence == 1 && timing.LongestAction?.Sequence == 2
            && timing.LongestReturn?.Sequence == 3 && timing.Longest?.Sequence == 3,
            "Eviction lost an independent phase maximum.");
    }

    private static void ActiveTimeoutRetainsStartedWithoutLateCompletion()
    {
        var timing = CooperativeDispatchTiming.Parse("1")!;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task<int>? running = null;
        var original = new TimeoutException("active dispatch timed out");
        Exception? observed = null;
        try
        {
            try
            {
                timing.Run(CooperativeDispatchOperation.PeerRead, 4, 5,
                    () => { entered.Set(); Require(release.Wait(TimeSpan.FromSeconds(5)), "The held action did not resume."); return 1; },
                    action => { running = Task.Run(action); Require(entered.Wait(TimeSpan.FromSeconds(5)), "The held action did not start."); throw original; });
            }
            catch (Exception error) { observed = error; }
            var frozen = timing.Recent.Single();
            Require(ReferenceEquals(original, observed) && frozen.Started is not null && frozen.Finished is null
                && timing.Incomplete == 1 && timing.LongestAction is null && timing.LongestReturn is null,
                "An active timeout fabricated completion or changed its error.");
            release.Set();
            Require(running!.Wait(TimeSpan.FromSeconds(5)) && timing.Recent.Single() == frozen,
                "Late action completion changed its retained timeout snapshot.");
        }
        finally
        {
            release.Set();
            if (running is not null) Require(running.Wait(TimeSpan.FromSeconds(5)), "The held action did not finish.");
        }
    }

    private static void DisabledAndInvalidModesDoNotCreateCapture()
    {
        Require(CooperativeDispatchTiming.Parse(null) is null && CooperativeDispatchTiming.Parse("0") is null,
            "Disabled dispatch timing created a capture.");
        foreach (string mode in new[] { "", "true", "2", " 1" })
        {
            bool rejected = false;
            try { CooperativeDispatchTiming.Parse(mode); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected, "Invalid dispatch timing mode was accepted.");
        }
    }

    private static void ForeignThreadCannotDispatchOrChangeEvidence()
    {
        int clockReads = 0, actions = 0;
        var timing = new CooperativeDispatchTiming(() => ++clockReads);
        var task = Task.Run(() =>
        {
            try { timing.Run(CooperativeDispatchOperation.Create, 0, 0, () => ++actions, action => action()); }
            catch (InvalidOperationException) { return true; }
            return false;
        });
        Require(task.Wait(TimeSpan.FromSeconds(5)) && task.Result && clockReads == 0 && actions == 0
            && timing.Calls == 0 && timing.Recent.Count == 0, "A foreign caller changed dispatch or timing state.");
    }
}
