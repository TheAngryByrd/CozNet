static class TimerWorkloadPlanChecks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    public static void Run()
    {
        var arithmetic = TimerWorkloadPlan.Arithmetic(false);
        Require(arithmetic.CreditSteps == 16 && arithmetic.CpuCreditPerStep == 220000 && !arithmetic.ForceGc
            && !arithmetic.RequiresOnlyDeferred && !arithmetic.IsBlockedWait, "The arithmetic schedule changed.");
        Require(arithmetic.ReachedWork(1, 2, 1, 2, 0, 0, false)
            && !arithmetic.ReachedWork(1, 2, 0, 2, 0, 0, false), "Arithmetic reach must require a completed multi-child paid lease.");
        foreach (var name in new[] { "jit", "native-wait", "managed-wait", "transitions" })
        {
            var plan = TimerWorkloadPlan.ParseTransition(name, "gc");
            Require(plan.CreditSteps == 128 && plan.CpuCreditPerStep == 10000 && plan.ForceGc
                && (ulong)plan.CreditSteps * plan.CpuCreditPerStep / 2 == 640000,
                "An all-deferred transition schedule must remain within the lease budget bound.");
            Require(!plan.ReachedWork(10, 1, 1, 2, 1, 1, true), "Transition reach must require timer reuse.");
        }
        var jit = TimerWorkloadPlan.ParseTransition("jit", "no-gc");
        Require(jit.RequiresOnlyDeferred && !jit.IsBlockedWait && jit.ReachedWork(10, 2, 0, 0, 1, 0, false)
            && !jit.ReachedWork(9, 2, 0, 0, 1, 0, false) && !jit.ReachedWork(10, 2, 0, 0, 0, 0, false),
            "JIT reach needs method progress and an interrupted compiler address.");
        foreach (var name in new[] { "native-wait", "managed-wait" })
        {
            var wait = TimerWorkloadPlan.ParseTransition(name, "no-gc");
            Require(wait.IsBlockedWait && wait.RequiresOnlyDeferred && wait.ReachedWork(0, 2, 0, 0, 0, 0, true)
                && !wait.ReachedWork(0, 2, 0, 0, 0, 0, false), "Wait reach requires an explicit stable wait.");
        }
        var transition = TimerWorkloadPlan.ParseTransition("transitions", "no-gc");
        Require(!transition.IsBlockedWait && !transition.RequiresOnlyDeferred && transition.ReachedWork(1, 2, 0, 1, 0, 1, false)
            && !transition.ReachedWork(1, 2, 0, 0, 0, 1, false) && !transition.ReachedWork(1, 2, 0, 1, 0, 0, false),
            "Transition reach needs paid selected code and an interrupted native fixture address.");
        foreach (var invalid in new[] { ("JIT", "gc"), ("jit", "GC") })
        {
            bool rejected = false;
            try { TimerWorkloadPlan.ParseTransition(invalid.Item1, invalid.Item2); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected, "Unknown transition modes must fail before side effects.");
        }
    }
}
