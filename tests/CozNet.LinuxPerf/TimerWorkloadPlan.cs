sealed class TimerWorkloadPlan
{
    public SignalScenario? Scenario { get; }
    public bool ForceGc { get; }
    public string Name => Scenario?.ToString() ?? "Arithmetic";
    public int CreditSteps => Scenario.HasValue ? 128 : 16;
    public ulong CpuCreditPerStep => Scenario.HasValue ? 10000UL : 220000UL;
    public bool IsBlockedWait => Scenario is SignalScenario.NativeWait or SignalScenario.ManagedWait;
    public bool RequiresOnlyDeferred => Scenario is SignalScenario.Jit or SignalScenario.NativeWait or SignalScenario.ManagedWait;

    private TimerWorkloadPlan(SignalScenario? scenario, bool forceGc) => (Scenario, ForceGc) = (scenario, forceGc);
    public static TimerWorkloadPlan Arithmetic(bool forceGc) => new(null, forceGc);
    public static TimerWorkloadPlan ParseTransition(string scenario, string gcMode)
        => new(SignalTransitionWorkload.Parse(scenario), TimerGcReach.ParseMode(gcMode));

    public bool ReachedWork(long completed, int records, int chainedLeases, int paidChildren, int jitChildren,
        int nativeChildren, bool stableWaitReached) => records > 1 && (Scenario switch
    {
        null => completed > 0 && chainedLeases > 0 && paidChildren > 1,
        SignalScenario.Jit => completed >= 10 && jitChildren > 0,
        SignalScenario.NativeWait or SignalScenario.ManagedWait => stableWaitReached,
        SignalScenario.Transitions => completed > 0 && paidChildren > 0 && nativeChildren > 0,
        _ => false
    });
}
