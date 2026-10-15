using CozNet;
using static CooperativeManagedChecks;

sealed class CooperativeDebtSnapshot
{
    public SignalThreadSnapshot SelectedState { get; }
    public SignalThreadSnapshot PeerState { get; }
    public long Global => DelayTicksModule.value(SelectedState.Ledger.Global);
    public long SelectedLocal => DelayTicksModule.value(SelectedState.Ledger.Local);
    public long PeerLocal => DelayTicksModule.value(PeerState.Ledger.Local);
    public long Selected => Math.Max(0, Global - SelectedLocal);
    public long Peer => Math.Max(0, Global - PeerLocal);

    private CooperativeDebtSnapshot(SignalThreadSnapshot selected, SignalThreadSnapshot peer)
        => (SelectedState, PeerState) = (selected, peer);

    public static CooperativeDebtSnapshot Capture(LedgerThread selected, LedgerThread peer, SignalAccounting state)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(peer);
        ArgumentNullException.ThrowIfNull(state);
        Require(!selected.Equals(peer), "Debt observation requires two distinct ledger threads.");
        return new(Parsed(SignalAccountingModule.captureThread(selected, state)),
            Parsed(SignalAccountingModule.captureThread(peer, state)));
    }
}
