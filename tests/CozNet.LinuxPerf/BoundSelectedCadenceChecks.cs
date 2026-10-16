static class BoundSelectedCadenceChecks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Run()
    {
        BaselineAndOneUsePermission();
        NominalEventPermission();
        ResumeDiscardsFinalDrainPermission();
        ForeignEpochDoesNotChangePermission();
        ContinuousControlAndPlanBoundaries();
        WorkerStartPreservesOwnerAndFault();
    }

    private static void NominalEventPermission()
    {
        var policy = BoundSampleCreditPlan.Parse("nominal-event", true);
        Require(policy.NominalEvents && policy.Name == "nominal-event-v1", "Nominal settings lost their estimator identity.");
        Require(!BoundSampleCreditPlan.Parse(null, false).NominalEvents && !BoundSampleCreditPlan.Parse("interval", true).NominalEvents,
            "Legacy settings changed estimator.");
        foreach (var value in new[] { "", "1", "nominal", " nominal-event" })
        {
            bool rejected = false;
            try { BoundSampleCreditPlan.Parse(value, true); } catch (ArgumentException) { rejected = true; }
            Require(rejected, "An invalid sample credit policy was accepted.");
        }
        bool unsupported = false;
        try { BoundSampleCreditPlan.Parse("nominal-event", false); } catch (ArgumentException) { unsupported = true; }
        Require(unsupported, "Nominal credit accepted a source without selected-payment ownership.");
        var cadence = new BoundSelectedCheckCadence(BoundSelectedCadencePlan.Parse("0", "qualified-sample", "5"), policy.NominalEvents);
        Require(!cadence.TryCheck(out _), "An empty nominal stream authorized a check.");
        cadence.Observe(1);
        Require(cadence.TryCheck(out var first) && first == new BoundSelectedAdmission(1, 1, true), "The initial zero-speedup event did not authorize a check.");
        Require(!cadence.TryCheck(out _), "One event authorized repeated checks.");
        cadence.Resume();
        Require(!cadence.TryCheck(out _), "Resume retained old permission.");
        cadence.Observe(2);
        Require(cadence.TryCheck(out var resumed) && resumed == new BoundSelectedAdmission(2, 2, true), "The first resumed nominal event did not authorize a check.");
    }

    private static void BaselineAndOneUsePermission()
    {
        var cadence = new BoundSelectedCheckCadence(BoundSelectedCadencePlan.Parse("0", "qualified-sample", "5"));
        Require(!cadence.TryCheck(out _), "An empty source cannot authorize a qualified check.");
        cadence.Observe(1);
        Require(!cadence.TryCheck(out _), "An epoch baseline cannot authorize a check.");
        cadence.Observe(1);
        Require(cadence.TryCheck(out var admission) && admission == new BoundSelectedAdmission(2, 1, true),
            "A zero-speedup interval must authorize one check with its original sample prefix.");
        Require(!cadence.TryCheck(out _) && cadence.Checks == 1, "A consumed interval cannot authorize another check.");
        cadence.Observe(1);
        Require(cadence.TryCheck(out admission) && admission.SampleCount == 3 && cadence.Checks == 2,
            "A later interval must authorize the next check.");
    }

    private static void ResumeDiscardsFinalDrainPermission()
    {
        var cadence = new BoundSelectedCheckCadence(BoundSelectedCadencePlan.Parse("50", "qualified-sample", "5"));
        cadence.Observe(1);
        cadence.Observe(1);
        Require(cadence.TryCheck(out _), "The initial interval must authorize a check.");
        cadence.Observe(1);
        cadence.Resume();
        Require(!cadence.TryCheck(out _), "Final-drain readiness cannot survive source resume.");
        cadence.Observe(2);
        Require(!cadence.TryCheck(out _), "The resumed baseline cannot authorize a check.");
        cadence.Observe(2);
        Require(cadence.TryCheck(out var admission) && admission == new BoundSelectedAdmission(5, 2, true),
            "The resumed complete interval must retain its new epoch and cumulative prefix.");
    }

    private static void ForeignEpochDoesNotChangePermission()
    {
        var cadence = new BoundSelectedCheckCadence(BoundSelectedCadencePlan.Parse("50", "qualified-sample", "5"));
        cadence.Observe(1);
        cadence.Observe(1);
        foreach (ulong epoch in new ulong[] { 0, 2 })
        {
            bool rejected = false;
            try { cadence.Observe(epoch); }
            catch (InvalidDataException) { rejected = true; }
            Require(rejected, "A foreign epoch must reject before changing cadence state.");
        }
        Require(cadence.TryCheck(out var admission) && admission == new BoundSelectedAdmission(2, 1, true),
            "Rejected samples cannot change the admitted sample prefix.");
        cadence.Resume();
        bool staleRejected = false;
        try { cadence.Observe(1); }
        catch (InvalidDataException) { staleRejected = true; }
        Require(staleRejected && !cadence.TryCheck(out _), "An old sample cannot authorize payment after resume.");
    }

    private static void ContinuousControlAndPlanBoundaries()
    {
        var plan = BoundSelectedCadencePlan.Parse("50", "continuous", "5");
        var cadence = new BoundSelectedCheckCadence(plan);
        Require(plan.Batches == 5 && plan.Sampling.Percent == 50, "The parsed plan must preserve fixed work and speedup.");
        Require(BoundSelectedCadencePlan.Parse("50", "qualified-sample", "2147483647").Batches == int.MaxValue,
            "The fixed-work parser must accept its maximum declared batch count.");
        Require(cadence.TryCheck(out var first) && !first.Qualified && cadence.TryCheck(out _) && cadence.Checks == 2,
            "The explicit continuous control must permit checks without samples.");
        foreach (string batches in new[] { "0", "-1", " 5", "+5", "2147483648", "1.5" })
        {
            bool rejected = false;
            try { BoundSelectedCadencePlan.Parse("50", "continuous", batches); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected, $"The fixed-work parser accepted invalid count '{batches}'.");
        }
    }

    private static void WorkerStartPreservesOwnerAndFault()
    {
        var worker = new CooperativeInteropChecks.Worker();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        try
        {
            var first = worker.Start(() =>
            {
                entered.Set();
                Require(release.Wait(TimeSpan.FromSeconds(5)), "The worker check did not release its held operation.");
                return Environment.CurrentManagedThreadId;
            });
            Require(entered.Wait(TimeSpan.FromSeconds(5)) && !first.IsCompleted,
                "Starting a held operation must return its incomplete task.");
            var second = worker.Start(() => Environment.CurrentManagedThreadId);
            Require(!second.IsCompleted, "A queued operation cannot bypass the held owner operation.");
            release.Set();
            Require(first.Wait(TimeSpan.FromSeconds(5)) && second.Wait(TimeSpan.FromSeconds(5))
                && first.Result == second.Result && first.Result != Environment.CurrentManagedThreadId,
                "Queued operations must complete on the same dedicated owner thread.");
            var expected = new InvalidDataException("worker fault identity");
            var fault = worker.Start<int>(() => throw expected);
            Require(SpinWait.SpinUntil(() => fault.IsCompleted, TimeSpan.FromSeconds(5)), "A failed worker operation did not complete.");
            Exception? observed = null;
            try { fault.GetAwaiter().GetResult(); }
            catch (Exception error) { observed = error; }
            Require(fault.IsFaulted && ReferenceEquals(observed, expected), "The worker task must retain the original fault.");
        }
        finally
        {
            release.Set();
            Require(worker.Join(), "The worker did not join after completing its queued operations.");
        }
    }
}
