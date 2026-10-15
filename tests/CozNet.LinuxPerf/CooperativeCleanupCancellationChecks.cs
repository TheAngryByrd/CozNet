using static CooperativeManagedChecks;

static class CooperativeCleanupCancellationChecks
{
    public static void Run()
    {
        EveryFailureStagePreservesOtherSlot();
        BothFailuresAreRetainedInSlotOrder();
        AbsentAndRetiredSlotsDoNotAcquirePolling();
        PollingRequiresCompletedCancellation();
        RetryCanRecoverOnlyTheFailedSlot();
        SuccessfulCancellationSurvivesLaterProbeFailure();
    }

    private static void RejectPolling(CooperativeCleanupCancellation cleanup, uint slot)
    {
        try { cleanup.RequireCancelled(slot); }
        catch (InvalidOperationException) { return; }
        throw new InvalidDataException("Cleanup polling was admitted without successful cancellation.");
    }

    private static AggregateException Failure(Action action)
    {
        try { action(); }
        catch (AggregateException error) { return error; }
        throw new InvalidDataException("Cleanup did not report its failed slot.");
    }

    private static void EveryFailureStagePreservesOtherSlot()
    {
        for (int stage = 0; stage < 3; ++stage)
        {
            var failure = new IOException("Controlled slot failure.");
            var cancelled = new List<uint>();
            var cleanup = new CooperativeCleanupCancellation(2,
                slot => slot == 0 && stage == 0 ? throw failure : true,
                slot => slot == 0 && stage == 1 ? throw failure : 0UL,
                slot => { if (slot == 0 && stage == 2) throw failure; cancelled.Add(slot); });
            RejectPolling(cleanup, 0);
            RejectPolling(cleanup, 1);
            var result = Failure(cleanup.Begin);
            Require(cleanup.HasStarted && result.InnerExceptions.Count == 1
                && ReferenceEquals(result.InnerExceptions[0], failure) && cancelled.SequenceEqual(new uint[] { 1 }),
                $"Failure stage {stage} lost the first exception, skipped the second slot, or changed cancellation order.");
            RejectPolling(cleanup, 0);
            cleanup.RequireCancelled(1);
        }
    }

    private static void BothFailuresAreRetainedInSlotOrder()
    {
        var errors = new[] { new IOException("Peer cancellation failed."), new IOException("Selected cancellation failed.") };
        var calls = new List<uint>();
        var cleanup = new CooperativeCleanupCancellation(2, _ => true, _ => 0,
            slot => { calls.Add(slot); throw errors[slot]; });
        var result = Failure(cleanup.Begin);
        Require(calls.SequenceEqual(new uint[] { 0, 1 }) && result.InnerExceptions.Count == 2
            && ReferenceEquals(result.InnerExceptions[0], errors[0]) && ReferenceEquals(result.InnerExceptions[1], errors[1]),
            "Cancellation failures lost their slot order or original exception identity.");
        RejectPolling(cleanup, 0);
        RejectPolling(cleanup, 1);
    }

    private static void AbsentAndRetiredSlotsDoNotAcquirePolling()
    {
        var inspected = new List<uint>();
        var cleanup = new CooperativeCleanupCancellation(2, slot => slot == 1,
            slot => { inspected.Add(slot); return 3; },
            _ => throw new InvalidDataException("An absent or retired slot was cancelled."));
        cleanup.Begin();
        Require(cleanup.HasStarted && inspected.SequenceEqual(new uint[] { 1 }),
            "An absent slot was probed or cleanup did not start.");
        RejectPolling(cleanup, 0);
        RejectPolling(cleanup, 1);
    }

    private static void PollingRequiresCompletedCancellation()
    {
        CooperativeCleanupCancellation? cleanup = null;
        int calls = 0;
        cleanup = new(1, _ => true, _ => 1, slot =>
        {
            Require(cleanup!.HasStarted, "Cancellation started before cleanup admission closed.");
            RejectPolling(cleanup, slot);
            ++calls;
        });
        cleanup.Begin();
        cleanup.RequireCancelled(0);
        Require(calls == 1, "The pending slot was not cancelled exactly once.");
    }

    private static void RetryCanRecoverOnlyTheFailedSlot()
    {
        bool fail = true;
        var cancelled = new List<uint>();
        var cleanup = new CooperativeCleanupCancellation(2, _ => true,
            slot => cancelled.Contains(slot) ? 3U : 0U,
            slot => { if (slot == 0 && fail) throw new IOException("Controlled cancellation failure."); cancelled.Add(slot); });
        _ = Failure(cleanup.Begin);
        RejectPolling(cleanup, 0);
        cleanup.RequireCancelled(1);
        fail = false;
        cleanup.Begin();
        cleanup.RequireCancelled(0);
        cleanup.RequireCancelled(1);
        Require(cancelled.SequenceEqual(new uint[] { 1, 0 }),
            "Cleanup retry repeated retired cancellation or failed to recover the rejected slot.");
    }

    private static void SuccessfulCancellationSurvivesLaterProbeFailure()
    {
        bool fail = false;
        int calls = 0;
        var cleanup = new CooperativeCleanupCancellation(1, _ => true,
            _ => fail ? throw new IOException("Later snapshot failure.") : 2UL,
            _ => ++calls);
        cleanup.Begin();
        fail = true;
        _ = Failure(cleanup.Begin);
        cleanup.RequireCancelled(0);
        Require(calls == 1, "A later probe failure erased successful cancellation or repeated native cancellation.");
    }
}
