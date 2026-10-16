using CozNet;

sealed class CooperativeServiceCadence
{
    private SignalAccounting? idleState;
    public long Checks { get; private set; }
    public long Executions { get; private set; }
    public long Skipped => Checks - Executions;

    public bool ShouldRun(SignalAccounting state, bool pending)
    {
        ArgumentNullException.ThrowIfNull(state);
        Checks = checked(Checks + 1);
        if (!pending && ReferenceEquals(state, idleState)) return false;
        idleState = null;
        Executions = checked(Executions + 1);
        return true;
    }

    public void Completed(SignalAccounting state, bool pending)
    {
        ArgumentNullException.ThrowIfNull(state);
        idleState = pending ? null : state;
    }
}
