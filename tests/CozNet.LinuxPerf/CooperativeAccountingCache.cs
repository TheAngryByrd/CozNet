using CozNet;
using static CooperativeManagedChecks;

sealed class CooperativeAccountingCache
{
    private readonly LedgerThread selected;
    private readonly LedgerThread peer;
    private SignalAccounting? observedState;
    private CooperativeDebtSnapshot? observed;
    private SignalAccounting? idleState;

    public CooperativeAccountingCache(LedgerThread selected, LedgerThread peer)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(peer);
        Require(!selected.Equals(peer), "Accounting reuse requires two distinct ledger threads.");
        (this.selected, this.peer) = (selected, peer);
    }

    public CooperativeDebtSnapshot Observe(SignalAccounting state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (ReferenceEquals(state, observedState)) return observed!;
        var next = CooperativeDebtSnapshot.Capture(selected, peer, state);
        observed = next;
        observedState = state;
        return next;
    }

    public (SignalServiceLease? Lease, SignalAccounting State) PreparePeer(SignalAccounting state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (ReferenceEquals(state, idleState)) return (null, state);
        var prepared = Parsed(SignalAccountingModule.prepareService(peer, state));
        var lease = prepared.Item1?.Value;
        idleState = lease is null ? prepared.Item2 : null;
        return (lease, prepared.Item2);
    }
}
