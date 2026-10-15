using CozNet;
using Microsoft.FSharp.Core;
using static CooperativeManagedChecks;

static class CooperativeDebtChecks
{
    private static SignalAccounting Accept(SignalServiceLease lease, long requested, long measured, SignalAccounting state)
    {
        var child = Parsed(SignalServiceChildModule.tryCreate(SignalServiceLeaseModule.identity(lease), 1, 1,
            requested, measured, measured, measured, 2, true));
        return Parsed(SignalAccountingModule.acceptServiceChild(lease, child, state));
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Foreign or duplicate debt ownership was accepted.");
    }

    public static void Run()
    {
        CooperativeAccountingCacheChecks.Run();
        FreshThreadsHaveNoDebt();
        UnsettledWaitDoesNotReduceLedgerDebt();
        SelectedWaitAndCatchUpPreserveOvershoot();
        SelectedPaymentPreservesEpochRemainder();
        ForeignAndDuplicateThreadsAreRejected();
    }

    private static void FreshThreadsHaveNoDebt()
    {
        var selected = Parsed(SignalAccountingModule.register(FSharpOption<int>.Some(50), SignalAccountingModule.create()));
        var peer = Parsed(SignalAccountingModule.register(FSharpOption<int>.None, selected.Item2));
        var observed = CooperativeDebtSnapshot.Capture(selected.Item1, peer.Item1, peer.Item2);
        Require(observed.Global == 0 && observed.SelectedLocal == 0 && observed.PeerLocal == 0
            && observed.Selected == 0 && observed.Peer == 0 && observed.SelectedState.Ledger.Phase.IsRunnable
            && observed.PeerState.Ledger.Phase.IsRunnable, "Fresh ledger threads acquired debt or a non-runnable phase.");
    }

    private static void UnsettledWaitDoesNotReduceLedgerDebt()
    {
        var fixture = Reserve(100000);
        var before = CooperativeDebtSnapshot.Capture(fixture.Selected, fixture.Peer, fixture.State);
        var state = Accept(fixture.Lease, 50000, 60000, fixture.State);
        var observed = CooperativeDebtSnapshot.Capture(fixture.Selected, fixture.Peer, state);
        Require(Parsed(SignalAccountingModule.confirmedServiceNanoseconds(fixture.Lease, state)) == 60000
            && observed.Global == 50000 && observed.SelectedLocal == 50000 && observed.PeerLocal == 0
            && observed.Selected == 0 && observed.Peer == 50000 && observed.PeerState.Ledger.Phase.IsDelaying,
            "An accepted but unsettled native wait reduced ledger debt or published overshoot.");
        var settled = Parsed(SignalAccountingModule.completeService(fixture.Lease, state)).Item2;
        var settledObservation = CooperativeDebtSnapshot.Capture(fixture.Selected, fixture.Peer, settled);
        Require(settledObservation.Global == 50000 && settledObservation.PeerLocal == 60000 && settledObservation.Peer == 0,
            "Peer settlement did not update local wait or published global overshoot before catch-up.");
        var continued = Parsed(SignalAccountingModule.prepareService(fixture.Peer, settled));
        Require(continued.Item1 is null, "A peer ahead of global delay reserved another wait.");
        settled = continued.Item2;
        var after = CooperativeDebtSnapshot.Capture(fixture.Selected, fixture.Peer, settled);
        Require(after.Global == 60000 && after.Selected == 10000 && after.Peer == 0
            && before.Global == 50000 && before.Peer == 50000 && observed.Peer == 50000,
            "Peer catch-up failed to publish overshoot or changed an earlier immutable debt observation.");
    }

    private static void SelectedWaitAndCatchUpPreserveOvershoot()
    {
        var fixture = Reserve(100000);
        var peerAccepted = Accept(fixture.Lease, 50000, 60000, fixture.State);
        var state = Parsed(SignalAccountingModule.completeService(fixture.Lease, peerAccepted)).Item2;
        state = Parsed(SignalAccountingModule.prepareService(fixture.Peer, state)).Item2;
        var reserved = Parsed(SignalAccountingModule.prepareSelectedService(fixture.Selected, state));
        Require(reserved.Item1 is not null && SignalServiceLeaseModule.nanoseconds(reserved.Item1.Value) == 10000,
            "The selected owner did not reserve debt caused by peer overshoot.");
        var lease = reserved.Item1!.Value;
        state = Parsed(SignalAccountingModule.sentService(lease, reserved.Item2));
        state = Accept(lease, 10000, 12000, state);
        var pending = CooperativeDebtSnapshot.Capture(fixture.Selected, fixture.Peer, state);
        Require(pending.Selected == 10000 && pending.SelectedState.Ledger.Phase.IsDelaying,
            "Selected wait was credited before its lease settled.");
        state = Parsed(SignalAccountingModule.completeService(lease, state)).Item2;
        var settled = CooperativeDebtSnapshot.Capture(fixture.Selected, fixture.Peer, state);
        Require(settled.Global == 60000 && settled.SelectedLocal == 62000 && settled.Selected == 0 && settled.Peer == 0,
            "Selected settlement omitted its measured wait or published global overshoot before catch-up.");
        var continued = Parsed(SignalAccountingModule.prepareSelectedService(fixture.Selected, state));
        Require(continued.Item1 is null, "A selected owner ahead of global delay reserved another wait.");
        state = continued.Item2;
        var observed = CooperativeDebtSnapshot.Capture(fixture.Selected, fixture.Peer, state);
        Require(observed.Global == 62000 && observed.SelectedLocal == 62000 && observed.PeerLocal == 60000
            && observed.Selected == 0 && observed.Peer == 2000 && observed.SelectedState.Ledger.Phase.IsRunnable
            && SignalAccountingModule.pendingCount(state) == 0 && pending.Selected == 10000,
            "Selected settlement omitted its measured wait, lost overshoot, or mutated the pending observation.");
    }

    private static void SelectedPaymentPreservesEpochRemainder()
    {
        var selected = Parsed(SignalAccountingModule.register(FSharpOption<int>.Some(50), SignalAccountingModule.create()));
        var peer = Parsed(SignalAccountingModule.register(FSharpOption<int>.None, selected.Item2));
        var state = peer.Item2;
        var stream = Parsed(SignalAccountingModule.sampleIdentity(selected.Item1, state));
        long Observe(ulong sequence, ulong cpu, ulong epoch)
        {
            var sample = Parsed(CpuSampleObservationModule.tryCreate(stream, sequence, cpu, epoch,
                SampleThreadState.Runnable, SampleLocation.Selected));
            var observed = Parsed(SignalAccountingModule.observe(selected.Item1, sample, state));
            state = observed.Item2;
            return DelayTicksModule.value(observed.Item1);
        }
        Require(Observe(1, 1, 1) == 0 && Observe(2, 4, 1) == 1,
            "The initial selected epoch did not retain a fractional credit remainder.");
        state = Parsed(SignalAccountingModule.prepare(selected.Item1, state)).Item2;
        var peerReservation = Parsed(SignalAccountingModule.prepareService(peer.Item1, state));
        var peerLease = peerReservation.Item1!.Value;
        state = Parsed(SignalAccountingModule.sentService(peerLease, peerReservation.Item2));
        state = Parsed(SignalAccountingModule.completeService(peerLease, Accept(peerLease, 1, 4, state))).Item2;
        state = Parsed(SignalAccountingModule.prepareService(peer.Item1, state)).Item2;
        var reservation = Parsed(SignalAccountingModule.prepareSelectedService(selected.Item1, state));
        var lease = reservation.Item1!.Value;
        Require(SignalServiceLeaseModule.nanoseconds(lease) == 3,
            "Selected epoch service did not reserve the peer overshoot.");
        state = Parsed(SignalAccountingModule.sentService(lease, reservation.Item2));
        state = Parsed(SignalAccountingModule.completeService(lease, Accept(lease, 3, 3, state))).Item2;
        var continued = Parsed(SignalAccountingModule.prepare(selected.Item1, state));
        Require(continued.Item1 is null, "Selected catch-up reserved a second payment in the same pause.");
        state = continued.Item2;
        Require(Parsed(SignalAccountingModule.requiredEpoch(selected.Item1, state)) == 2,
            "One selected lease did not advance exactly one required eligibility epoch.");
        var stale = Parsed(CpuSampleObservationModule.tryCreate(stream, 3, 10, 1,
            SampleThreadState.Runnable, SampleLocation.Selected));
        Require(SignalAccountingModule.observe(selected.Item1, stale, state).IsError,
            "A post-payment sample retained the old eligibility epoch.");
        Require(Observe(3, 10, 2) == 0 && Observe(4, 11, 2) == 1
            && CooperativeDebtSnapshot.Capture(selected.Item1, peer.Item1, state).SelectedLocal == 5
            && SignalAccountingModule.pendingCount(state) == 0,
            "Resume credited its baseline, lost the fractional remainder, or retained a lease.");
    }

    private static void ForeignAndDuplicateThreadsAreRejected()
    {
        var fixture = Reserve(100000);
        var foreign = Reserve(100000);
        Reject(() => CooperativeDebtSnapshot.Capture(fixture.Selected, fixture.Selected, fixture.State));
        Reject(() => CooperativeDebtSnapshot.Capture(fixture.Selected, foreign.Peer, fixture.State));
        Reject(() => CooperativeDebtSnapshot.Capture(foreign.Selected, fixture.Peer, fixture.State));
    }
}
