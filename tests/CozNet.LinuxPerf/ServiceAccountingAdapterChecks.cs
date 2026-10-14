using CozNet;
using Microsoft.FSharp.Core;

static class ServiceAccountingAdapterChecks
{
    private sealed record Fixture(LedgerThread Selected, LedgerThread Peer, SignalServiceLease Lease, SignalAccounting State);
    private static T Parsed<T>(FSharpResult<T, string> value) => value.IsOk ? value.ResultValue : throw new InvalidOperationException(value.ErrorValue);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (InvalidDataException) { rejected = true; }
        Require(rejected, message);
    }

    private static SignalAccounting Credit(LedgerThread selected, ulong sequence, ulong cpu, SignalAccounting state)
    {
        var identity = Parsed(SignalAccountingModule.sampleIdentity(selected, state));
        var observation = Parsed(CpuSampleObservationModule.tryCreate(identity, sequence, cpu, 0,
            SampleThreadState.Runnable, SampleLocation.Selected));
        state = Parsed(SignalAccountingModule.observe(selected, observation, state)).Item2;
        var prepared = Parsed(SignalAccountingModule.prepare(selected, state));
        Require(FSharpOption<SignalDelayRequest>.get_IsNone(prepared.Item1), "Selected credit must publish without a wait.");
        return prepared.Item2;
    }

    private static Fixture Start()
    {
        var selected = Parsed(SignalAccountingModule.register(FSharpOption<int>.Some(50), SignalAccountingModule.create()));
        var peer = Parsed(SignalAccountingModule.register(FSharpOption<int>.None, selected.Item2));
        var state = Credit(selected.Item1, 1, 220000, peer.Item2);
        var service = Parsed(SignalAccountingModule.prepareService(peer.Item1, state));
        var lease = service.Item1.Value;
        state = Parsed(SignalAccountingModule.sentService(lease, service.Item2));
        return new(selected.Item1, peer.Item1, lease, state);
    }

    private static ServiceAccountingAdapter Adapter(Fixture fixture) => new(fixture.Lease, 1, ip => ip == 100,
        (outcome, result) => outcome is 1 or 2 ? result == 0 : outcome == 3 && result == -99);

    private static ReceivedServiceChild Child(ulong ordinal, ulong requested, ulong measured, ulong outcome = 2, long result = 0)
    {
        ulong started = ordinal * 1000000;
        ulong handler = Math.Max(measured, 1);
        var ack = new ServiceNativeAcknowledgment { Sequence = ordinal, Outcome = outcome, Ip = 100,
            Handler = handler, Wait = measured, WaitResult = result, Started = started, Queued = started + 1,
            HandlerStarted = started + 2, HandlerFinished = started + 2 + handler, Observed = started + 3 + handler };
        return new(new ServiceNativeChild { Ordinal = ordinal, Requested = requested, Acknowledgment = ack },
            ack.Observed + 1, ack.Observed + 2, 0);
    }

    private static ServiceNativeObservation Observation(Fixture fixture, ulong phase, params ReceivedServiceChild[] children)
    {
        var identity = NativeServiceClient.Parts(SignalServiceLeaseModule.identity(fixture.Lease));
        return new(new ServiceNativeSnapshot { IdentityFirst = identity.First, IdentitySecond = identity.Second,
            Command = 1, Budget = (ulong)SignalServiceLeaseModule.nanoseconds(fixture.Lease),
            Phase = phase, Outstanding = phase == 1 ? 1UL : 0, Cancellation = phase == 3 ? 1UL : 0,
            Children = (ulong)children.Length, Measured = (ulong)children.Sum(child => checked((long)child.Native.Acknowledgment.Wait)),
            Captured = children.Length == 0 ? 1 : children.Max(child => child.FirstSnapshotCaptured) + 1 }, children, []);
    }

    private static long Remaining(Fixture fixture, SignalAccounting state) => SignalServiceLeaseModule.nanoseconds(
        Parsed(SignalAccountingModule.prepareService(fixture.Peer, state)).Item1.Value);

    public static void Run()
    {
        RepeatedPrefixAndNewCreditSettleOnce();
        InvalidSuffixPublishesNoPartialState();
        CancellationRequiresIntentAndKeepsUnpaidDebt();
        RejectsForeignAndLateObservations();
        BackendOutcomeRulesRemainExplicit();
        InvalidHeadersKeepDebtReserved();
        RegressedSnapshotKeepsAcceptedPrefix();
        ZeroWaitPrefixRejectsStaleState();
    }

    private static void RepeatedPrefixAndNewCreditSettleOnce()
    {
        var fixture = Start();
        var first = Child(1, 50000, 40000);
        var prefix = Observation(fixture, 1, first);
        var partial = Adapter(fixture).Accept(prefix, fixture.State);
        var repeated = partial.Adapter.Accept(prefix, partial.State);
        Require(ReferenceEquals(repeated.State, partial.State) && repeated.Adapter.AcceptedCount == 1
            && repeated.Adapter.ConfirmedNanoseconds == 40000, "Repeated snapshots must not pay the same child twice.");
        var state = Credit(fixture.Selected, 2, 300000, repeated.State);
        var final = repeated.Adapter.Accept(Observation(fixture, 2, first, Child(2, 50000, 50000), Child(3, 20000, 25000)), state);
        Require(final.Adapter.IsTerminal && final.Adapter.ConfirmedNanoseconds == 115000
            && SignalAccountingModule.pendingCount(final.State) == 0 && Remaining(fixture, final.State) == 35000,
            "Aggregate settlement must retain overshoot and leave new selected credit unpaid.");
        Reject(() => final.Adapter.Accept(prefix, final.State), "A settled adapter must reject replay.");
        Reject(() => partial.Adapter.Accept(prefix, fixture.State), "A stale accounting state must not replace the accepted prefix.");
        var changed = first with { FirstManagedReceiptUpper = first.FirstManagedReceiptUpper + 1 };
        Reject(() => partial.Adapter.Accept(Observation(fixture, 1, changed), partial.State), "Accepted receipt bounds must be immutable.");
    }

    private static void InvalidSuffixPublishesNoPartialState()
    {
        var fixture = Start();
        var adapter = Adapter(fixture);
        var first = Child(1, 50000, 40000);
        Reject(() => adapter.Accept(Observation(fixture, 1, first, Child(2, 50000, 40000, 2, -7)), fixture.State),
            "A native error in a later child must reject the batch.");
        Require(Parsed(SignalAccountingModule.confirmedServiceNanoseconds(fixture.Lease, fixture.State)) == 0,
            "Rejected batches must not publish their valid prefix.");
        var valid = adapter.Accept(Observation(fixture, 1, first, Child(2, 50000, 40000)), fixture.State);
        Require(valid.Adapter.AcceptedCount == 2 && valid.Adapter.ConfirmedNanoseconds == 80000,
            "The original state must still accept the corrected batch.");
    }

    private static void CancellationRequiresIntentAndKeepsUnpaidDebt()
    {
        var fixture = Start();
        var adapter = Adapter(fixture);
        Reject(() => adapter.Accept(Observation(fixture, 3), fixture.State), "Unsolicited cancellation cannot release debt.");
        var cancelling = adapter.RequestCancellation(fixture.State);
        var empty = cancelling.Adapter.Accept(Observation(fixture, 3), cancelling.State);
        Require(empty.Adapter.IsTerminal && Remaining(fixture, empty.State) == 110000, "An empty cancellation pays no debt.");
        var first = Child(1, 50000, 40000);
        var partial = adapter.Accept(Observation(fixture, 1, first), fixture.State);
        cancelling = partial.Adapter.RequestCancellation(partial.State);
        var stopped = cancelling.Adapter.Accept(Observation(fixture, 3, first), cancelling.State);
        Require(stopped.Adapter.IsTerminal && Remaining(fixture, stopped.State) == 70000,
            "Cancellation settles only the accepted measured prefix.");
    }

    private static void RejectsForeignAndLateObservations()
    {
        var fixture = Start();
        var adapter = Adapter(fixture);
        var child = Child(1, 50000, 40000);
        var observation = Observation(fixture, 1, child);
        var header = observation.Header;
        header.Command = 2;
        Reject(() => adapter.Accept(observation with { Header = header }, fixture.State), "A foreign command must fail.");
        header = observation.Header;
        header.Measured++;
        Reject(() => adapter.Accept(observation with { Header = header }, fixture.State), "An incorrect aggregate must fail.");
        var late = child with { FirstManagedReceiptUpper = child.Native.Acknowledgment.Started + 100000001 };
        Reject(() => adapter.Accept(Observation(fixture, 1, late), fixture.State), "Late managed receipt cannot settle native wait.");
        Reject(() => adapter.Accept(Observation(fixture, 2, child), fixture.State), "Premature completion cannot release a lease.");
    }

    private static void BackendOutcomeRulesRemainExplicit()
    {
        var fixture = Start();
        var interrupted = Child(1, 50000, 12345, 3, -99);
        var settled = Adapter(fixture).Accept(Observation(fixture, 2, interrupted), fixture.State);
        Require(settled.Adapter.IsTerminal && Remaining(fixture, settled.State) == 97655,
            "The backend-specific interrupted result must preserve its partial wait.");
    }

    private static void InvalidHeadersKeepDebtReserved()
    {
        var fixture = Start();
        var adapter = Adapter(fixture);
        var observation = Observation(fixture, 1, Child(1, 50000, 40000));
        var mutations = new (string Name, Func<ServiceNativeSnapshot, ServiceNativeSnapshot> Change)[]
        {
            ("native error", h => { h.Error = 1; return h; }),
            ("transport error", h => { h.TransportError = -1; return h; }),
            ("invalid phase", h => { h.Phase = 4; h.Outstanding = 0; return h; }),
            ("rejected count", h => { h.Rejected = 1; return h; }),
            ("unsolicited active cancellation", h => { h.Cancellation = 1; return h; })
        };
        void Check(ServiceNativeObservation invalid, string name)
        {
            Reject(() => adapter.Accept(invalid, fixture.State), $"The adapter must reject {name}.");
            Require(adapter.AcceptedCount == 0 && adapter.ConfirmedNanoseconds == 0 &&
                Parsed(SignalAccountingModule.confirmedServiceNanoseconds(fixture.Lease, fixture.State)) == 0 &&
                SignalAccountingModule.pendingCount(fixture.State) == 1,
                $"Rejecting {name} must retain the unpaid reservation and original cursor.");
        }
        foreach (var mutation in mutations)
            Check(observation with { Header = mutation.Change(observation.Header) }, mutation.Name);
        Check(observation with { Rejected = [new ServiceNativeAcknowledgment()] }, "rejected records");
        Check(Observation(fixture, 1, Child(1, 50000, 0, 1)), "an active terminal child");
        var complete = Observation(fixture, 2, Child(1, 50000, 0, 1));
        var header = complete.Header;
        header.Cancellation = 1;
        Check(complete with { Header = header }, "unsolicited completed cancellation");
    }

    private static void RegressedSnapshotKeepsAcceptedPrefix()
    {
        var fixture = Start();
        var first = Child(1, 50000, 40000);
        var observation = Observation(fixture, 1, first);
        var partial = Adapter(fixture).Accept(observation, fixture.State);
        var header = observation.Header;
        header.Captured = first.FirstSnapshotCaptured - 1;
        Reject(() => partial.Adapter.Accept(observation with { Header = header }, partial.State),
            "An unchanged child must not permit a regressed snapshot clock.");
        Require(partial.Adapter.AcceptedCount == 1 && partial.Adapter.ConfirmedNanoseconds == 40000 &&
            Parsed(SignalAccountingModule.confirmedServiceNanoseconds(fixture.Lease, partial.State)) == 40000 &&
            SignalAccountingModule.pendingCount(partial.State) == 1,
            "A regressed snapshot must retain the accepted prefix and unpaid reservation.");
        var cancelling = partial.Adapter.RequestCancellation(partial.State);
        var settled = cancelling.Adapter.Accept(Observation(fixture, 3, first), cancelling.State);
        Require(Remaining(fixture, settled.State) == 70000,
            "Rejection must leave only the original measured prefix available for settlement.");
    }

    private static void ZeroWaitPrefixRejectsStaleState()
    {
        var fixture = Start();
        var first = Child(1, 50000, 0);
        var observation = Observation(fixture, 1, first);
        var partial = Adapter(fixture).Accept(observation, fixture.State);
        Reject(() => partial.Adapter.Accept(observation, fixture.State),
            "A zero-wait child must still bind the cursor to its accounting journal position.");
        Reject(() => partial.Adapter.RequestCancellation(fixture.State),
            "Cancellation must reject a stale zero-wait journal position.");
        var otherNative = first.Native;
        var otherAck = otherNative.Acknowledgment;
        otherAck.Sequence = 2;
        otherNative.Acknowledgment = otherAck;
        var other = Adapter(fixture).Accept(Observation(fixture, 1, first with { Native = otherNative }), fixture.State);
        Reject(() => partial.Adapter.Accept(observation, other.State),
            "Equal counts and waits must not accept a different final native sequence.");
        Reject(() => partial.Adapter.RequestCancellation(other.State),
            "Cancellation must reject a different final native sequence.");
        var repeated = partial.Adapter.Accept(observation, partial.State);
        var next = repeated.Adapter.Accept(Observation(fixture, 1, first, Child(2, 50000, 40000)), repeated.State);
        Require(next.Adapter.AcceptedCount == 2 && next.Adapter.ConfirmedNanoseconds == 40000,
            "The correct zero-wait prefix must accept its next child after stale-state rejection.");
    }
}
