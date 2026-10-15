using CozNet;
using Microsoft.FSharp.Core;
using static CooperativeManagedChecks;

static class CooperativeAccountingCacheChecks
{
    private static (LedgerThread Selected, LedgerThread Peer, SignalAccounting State) Fresh()
    {
        var selected = Parsed(SignalAccountingModule.register(FSharpOption<int>.Some(50), SignalAccountingModule.create()));
        var peer = Parsed(SignalAccountingModule.register(FSharpOption<int>.None, selected.Item2));
        return (selected.Item1, peer.Item1, peer.Item2);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid accounting state was accepted by the reuse path.");
    }

    public static void Run()
    {
        IdleResultAndSnapshotAreReused();
        CreditAndPendingServiceInvalidateReuse();
        EqualDebtInAnotherStateRequiresFreshObservation();
        RejectedStateDoesNotPoisonReuse();
        NullStateCannotMatchAnEmptyCache();
    }

    private static void RequireIdleAgreement(CooperativeAccountingCache cache, LedgerThread selected, LedgerThread peer,
        SignalAccounting state)
    {
        var full = Parsed(SignalAccountingModule.prepareService(peer, state));
        var reused = cache.PreparePeer(state);
        var fullDebt = CooperativeDebtSnapshot.Capture(selected, peer, state);
        var reusedDebt = cache.Observe(state);
        Require(full.Item1 is null && reused.Lease is null && full.Item2.Equals(reused.State)
            && fullDebt.SelectedState.Equals(reusedDebt.SelectedState) && fullDebt.PeerState.Equals(reusedDebt.PeerState),
            "Idle reuse must agree with full model preparation and capture, including all immutable state fields.");
    }

    private static void IdleResultAndSnapshotAreReused()
    {
        var fixture = Fresh();
        var cache = new CooperativeAccountingCache(fixture.Selected, fixture.Peer);
        var first = cache.PreparePeer(fixture.State);
        var observed = cache.Observe(first.State);
        var again = cache.PreparePeer(first.State);
        Require(first.Lease is null && again.Lease is null && ReferenceEquals(first.State, again.State)
            && ReferenceEquals(observed, cache.Observe(again.State)) && observed.Selected == 0 && observed.Peer == 0
            && SignalAccountingModule.pendingCount(again.State) == 0,
            "Repeated idle preparation and observation must reuse validated immutable results without creating a lease.");
        RequireIdleAgreement(cache, fixture.Selected, fixture.Peer, again.State);
    }

    private static void CreditAndPendingServiceInvalidateReuse()
    {
        var fixture = Fresh();
        var cache = new CooperativeAccountingCache(fixture.Selected, fixture.Peer);
        var idle = cache.PreparePeer(fixture.State);
        var original = cache.Observe(idle.State);
        var credited = Credit(fixture.Selected, 1, 100000, idle.State);
        var due = cache.Observe(credited);
        Require(due.Peer == 50000 && due.Selected == 0 && original.Peer == 0 && !ReferenceEquals(original, due),
            "New credit must invalidate the cached zero-debt observation without changing its earlier values.");
        var prepared = cache.PreparePeer(credited);
        Require(prepared.Lease is not null && SignalServiceLeaseModule.nanoseconds(prepared.Lease) == 50000,
            "New credit must reserve the full peer debt after an idle result was cached.");
        var lease = prepared.Lease!;
        var pending = cache.Observe(prepared.State);
        Require(pending.Peer == 50000 && pending.PeerState.Operation.IsService && pending.PeerState.Ledger.Phase.IsDelaying,
            "Reservation must replace the idle snapshot even while numeric debt remains unchanged.");
        Reject(() => cache.PreparePeer(prepared.State));
        var sent = Parsed(SignalAccountingModule.sentService(lease, prepared.State));
        var child = Parsed(SignalServiceChildModule.tryCreate(SignalServiceLeaseModule.identity(lease), 1, 1,
            50000, 60000, 60000, 60000, 2, true));
        var accepted = Parsed(SignalAccountingModule.acceptServiceChild(lease, child, sent));
        var unsettled = cache.Observe(accepted);
        Require(unsettled.Peer == 50000 && unsettled.PeerLocal == 0 && !ReferenceEquals(pending, unsettled)
            && !pending.PeerState.Operation.Equals(unsettled.PeerState.Operation)
            && unsettled.PeerState.Operation.Equals(CooperativeDebtSnapshot.Capture(fixture.Selected, fixture.Peer, accepted).PeerState.Operation),
            "Accepted native wait must refresh the operation snapshot without prematurely reducing ledger debt.");
        var settled = Parsed(SignalAccountingModule.completeService(lease, accepted)).Item2;
        var beforeCatchUp = cache.Observe(settled);
        Require(beforeCatchUp.Global == 50000 && beforeCatchUp.PeerLocal == 60000 && beforeCatchUp.Selected == 0,
            "Service settlement must refresh local wait without publishing overshoot early.");
        var continued = cache.PreparePeer(settled);
        var afterCatchUp = cache.Observe(continued.State);
        Require(continued.Lease is null && afterCatchUp.Global == 60000 && afterCatchUp.Selected == 10000
            && afterCatchUp.Peer == 0 && ReferenceEquals(continued.State, cache.PreparePeer(continued.State).State)
            && ReferenceEquals(afterCatchUp, cache.Observe(continued.State)) && original.Global == 0 && pending.Peer == 50000,
            "Idle preparation must publish peer overshoot before caching its new state and preserve earlier snapshots.");
        RequireIdleAgreement(cache, fixture.Selected, fixture.Peer, continued.State);
    }

    private static void EqualDebtInAnotherStateRequiresFreshObservation()
    {
        var fixture = Fresh();
        var cache = new CooperativeAccountingCache(fixture.Selected, fixture.Peer);
        var first = cache.PreparePeer(fixture.State);
        var before = cache.Observe(first.State);
        var equivalent = Parsed(SignalAccountingModule.prepareService(fixture.Peer, first.State)).Item2;
        Require(!ReferenceEquals(first.State, equivalent), "The equal-debt fixture must use another state instance.");
        var after = cache.Observe(equivalent);
        var prepared = cache.PreparePeer(equivalent);
        Require(!ReferenceEquals(before, after) && before.Global == after.Global && before.Peer == after.Peer
            && !ReferenceEquals(equivalent, prepared.State) && prepared.Lease is null,
            "Equal numeric debt in another immutable state must take the full observation and preparation paths.");
    }

    private static void RejectedStateDoesNotPoisonReuse()
    {
        var fixture = Fresh();
        var foreign = Fresh();
        var cache = new CooperativeAccountingCache(fixture.Selected, fixture.Peer);
        var idle = cache.PreparePeer(fixture.State);
        var observed = cache.Observe(idle.State);
        Reject(() => cache.Observe(foreign.State));
        Reject(() => cache.PreparePeer(foreign.State));
        Require(ReferenceEquals(idle.State, cache.PreparePeer(idle.State).State)
            && ReferenceEquals(observed, cache.Observe(idle.State)),
            "Rejected foreign state must not replace either validated cache entry.");
        Reject(() => new CooperativeAccountingCache(fixture.Selected, fixture.Selected));
    }

    private static void NullStateCannotMatchAnEmptyCache()
    {
        var fixture = Fresh();
        var cache = new CooperativeAccountingCache(fixture.Selected, fixture.Peer);
        foreach (Action operation in new Action[] { () => cache.Observe(null!), () => cache.PreparePeer(null!) })
        {
            Exception? failure = null;
            try { operation(); }
            catch (ArgumentNullException error) { failure = error; }
            Require(failure is not null, "Null state must fail before it can match an empty cache entry.");
        }
    }
}
