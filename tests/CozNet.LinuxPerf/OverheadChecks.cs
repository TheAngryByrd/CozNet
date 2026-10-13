static class OverheadChecks
{
    public static void Run()
    {
        ZeroSpeedupWithoutEventsCompletes();
        NonzeroSpeedupWithoutEventsFails();
        UnexpectedEventFails();
        OrdinaryCompletionStillRequiresEvents();
    }

    private static SampledIntervention Start(int speedup)
    {
        var intervention = new SampledIntervention("selected", speedup, new ManagedWaitBackend(), new FixedClock(), 0,
            ProbePlan.Parse("manual", "1"));
        intervention.WaitUntilReady();
        return intervention;
    }

    private static void ZeroSpeedupWithoutEventsCompletes()
    {
        using var intervention = Start(0);
        var result = intervention.CompleteWithoutSamples();
        if (result.SpeedupPercent != 0 || result.Live.AppliedSamples != 0 || result.SelectedPeriodNanoseconds != 0 ||
            result.CreditedTicks != 0 || result.GlobalDelayTicks != 0 || result.WorkerWaitTicks != 0 ||
            result.WorkerBatches == 0 || result.WorkerChecks == 0)
            throw new Exception("ZeroSpeedupWithoutEventsCompletes: completion lost work or created a sample or delay.");
    }

    private static void NonzeroSpeedupWithoutEventsFails()
    {
        using var intervention = Start(50);
        Reject(() => intervention.CompleteWithoutSamples(), "NonzeroSpeedupWithoutEventsFails: a treatment completed without samples.");
    }

    private static void UnexpectedEventFails()
    {
        using var intervention = Start(0);
        intervention.Publish(CpuSample.Create(1, 1, 1, 1000000, 1, "selected"));
        Reject(() => intervention.CompleteWithoutSamples(), "UnexpectedEventFails: no-sample completion accepted an event.");
    }

    private static void OrdinaryCompletionStillRequiresEvents()
    {
        using var intervention = Start(0);
        Reject(() => intervention.Complete(intervention.ObserveLive()), "OrdinaryCompletionStillRequiresEvents: collection completed without events.");
    }

    private sealed class FixedClock : IThreadCpuClock
    {
        public long ReadNanoseconds() => 0;
    }

    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception(message);
    }
}
