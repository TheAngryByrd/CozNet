using CozNet;
using Microsoft.FSharp.Core;

static class ServiceSelectedAdapterChecks
{
    private sealed record Fixture(LedgerThread Selected, SignalDelayRequest Request,
        SignalAccounting Reserved, SignalAccounting Submitted);
    private static T Parsed<T>(FSharpResult<T, string> value) => value.IsOk ? value.ResultValue : throw new InvalidOperationException(value.ErrorValue);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (InvalidDataException) { rejected = true; }
        Require(rejected, message);
    }
    private static Fixture Start()
    {
        var selected = Parsed(SignalAccountingModule.register(FSharpOption<int>.Some(50), SignalAccountingModule.create()));
        var peer = Parsed(SignalAccountingModule.register(FSharpOption<int>.None, selected.Item2));
        var identity = Parsed(SignalAccountingModule.sampleIdentity(selected.Item1, peer.Item2));
        var sample = Parsed(CpuSampleObservationModule.tryCreate(identity, 1, 80000, 0,
            SampleThreadState.Runnable, SampleLocation.Selected));
        var state = Parsed(SignalAccountingModule.observe(selected.Item1, sample, peer.Item2)).Item2;
        state = Parsed(SignalAccountingModule.prepare(selected.Item1, state)).Item2;
        var prepared = Parsed(SignalAccountingModule.prepare(peer.Item1, state));
        var request = prepared.Item1.Value;
        state = Parsed(SignalAccountingModule.sent(request, 1, prepared.Item2));
        state = Parsed(SignalAccountingModule.complete(request, 1,
            SignalSettlement.NewMeasured(Parsed(DelayTicksModule.tryCreate(70000))), state));
        state = Parsed(SignalAccountingModule.prepare(peer.Item1, state)).Item2;
        prepared = Parsed(SignalAccountingModule.prepare(selected.Item1, state));
        request = prepared.Item1.Value;
        Require(SignalDelayRequestModule.nanoseconds(request) == 30000, "Peer overshoot must establish 30,000 ns of selected debt.");
        return new(selected.Item1, request, prepared.Item2, Parsed(SignalAccountingModule.submitted(request, prepared.Item2)));
    }
    private static ServiceSelectedAdapter Adapter(Fixture fixture) => new(fixture.Request, 1, ip => ip == 100,
        (outcome, result) => outcome is 1 or 2 ? result == 0 : outcome == 3 && result == -99);

    private static ServiceNativeObservation Observation(Fixture fixture, ulong measured = 12000, ulong outcome = 2, long result = 0)
    {
        var parts = NativeServiceClient.Parts(SignalDelayRequestModule.identity(fixture.Request));
        ulong handler = Math.Max(measured, 1);
        var ack = new ServiceNativeAcknowledgment { Sequence = 1, Outcome = outcome, Ip = 100, Handler = handler,
            Wait = measured, WaitResult = result, Started = 1000000, Queued = 1000001,
            HandlerStarted = 1000002, HandlerFinished = 1000002 + handler, Observed = 1000003 + handler };
        var received = new ReceivedServiceChild(new ServiceNativeChild { Ordinal = 1, Requested = 30000, Acknowledgment = ack },
            ack.Observed + 1, ack.Observed + 2, 0);
        return new(new ServiceNativeSnapshot { IdentityFirst = parts.First, IdentitySecond = parts.Second, Command = 1,
            Budget = 30000, Measured = measured, Phase = 2, Children = 1, Captured = received.FirstSnapshotCaptured + 1 }, [received], []);
    }
    private static ServiceNativeObservation Active(Fixture fixture)
    {
        var header = Observation(fixture).Header;
        header.Phase = 1; header.Children = 0; header.Measured = 0; header.Outstanding = 1;
        return new(header, [], []);
    }

    public static void Run()
    {
        ActiveSelectedJournalsRequireSubmittedState();
        SelectedPartialServiceStopsAfterOneChild();
        InvalidSelectedJournalsRetainSubmission();
        SelectedReceiptAndOutcomeBounds();
    }

    private static void ActiveSelectedJournalsRequireSubmittedState()
    {
        var fixture = Start();
        var adapter = Adapter(fixture);
        var active = Active(fixture);
        var accepted = adapter.Accept(active, fixture.Submitted);
        Require(ReferenceEquals(accepted.Adapter, adapter) && ReferenceEquals(accepted.State, fixture.Submitted)
            && accepted.Acknowledgment is null, "An active empty journal must preserve the pending request.");
        Reject(() => adapter.Accept(active, fixture.Reserved), "A reserved state cannot replace submitted ownership.");
        var bound = Parsed(SignalAccountingModule.sent(fixture.Request, 1, fixture.Submitted));
        Reject(() => adapter.Accept(active, bound), "An already bound state cannot replace awaiting-sequence ownership.");
    }

    private static void SelectedPartialServiceStopsAfterOneChild()
    {
        foreach (var item in new[] { (Wait: 12000UL, Outcome: 2UL, Result: 0L, Remaining: 18000L),
            (Wait: 0UL, Outcome: 1UL, Result: 0L, Remaining: 30000L),
            (Wait: 12345UL, Outcome: 3UL, Result: -99L, Remaining: 17655L) })
        {
            var fixture = Start();
            var observation = Observation(fixture, item.Wait, item.Outcome, item.Result);
            var accepted = Adapter(fixture).Accept(observation, fixture.Submitted);
            var remaining = Parsed(SignalAccountingModule.prepare(fixture.Selected, accepted.State)).Item1.Value;
            Require(accepted.Adapter.IsTerminal && accepted.Acknowledgment?.WaitNanoseconds == item.Wait
                && SignalAccountingModule.pendingCount(accepted.State) == 0
                && SignalDelayRequestModule.nanoseconds(remaining) == item.Remaining,
                "One selected child must settle its exact wait and leave the remaining debt for a later sampled check.");
            Reject(() => accepted.Adapter.Accept(observation, accepted.State), "A settled selected cursor must reject replay.");
        }
        var overshoot = Start();
        var settled = Adapter(overshoot).Accept(Observation(overshoot, 35000), overshoot.Submitted);
        var next = Parsed(SignalAccountingModule.prepare(overshoot.Selected, settled.State));
        Require(next.Item1 is null && SignalAccountingModule.globalNanoseconds(next.Item2) == 75000,
            "Selected overshoot must publish in full at the next ordinary check.");
    }

    private static void InvalidSelectedJournalsRetainSubmission()
    {
        var fixture = Start();
        var adapter = Adapter(fixture);
        var observation = Observation(fixture);
        var mutations = new (string Name, Func<ServiceNativeSnapshot, ServiceNativeSnapshot> Change)[] {
            ("identity", h => { h.IdentityFirst ^= 1; return h; }),
            ("command", h => { h.Command++; return h; }),
            ("budget", h => { h.Budget++; return h; }),
            ("native error", h => { h.Error = 1; return h; }),
            ("transport error", h => { h.TransportError = -1; return h; }),
            ("phase", h => { h.Phase = 3; return h; }),
            ("cancellation", h => { h.Cancellation = 1; return h; }),
            ("rejected count", h => { h.Rejected = 1; return h; }),
            ("zero capture", h => { h.Captured = 0; return h; }),
            ("child count", h => { h.Children = 0; return h; }),
            ("outstanding child", h => { h.Outstanding = 1; return h; }),
            ("aggregate", h => { h.Measured++; return h; }),
            ("active terminal child", h => { h.Phase = 1; return h; }) };
        foreach (var mutation in mutations)
            Reject(() => adapter.Accept(observation with { Header = mutation.Change(observation.Header) }, fixture.Submitted),
                $"The selected adapter must reject an invalid {mutation.Name}.");
        Reject(() => adapter.Accept(observation with { Rejected = [new()] }, fixture.Submitted), "Rejected records cannot settle a selected request.");
        var header = observation.Header;
        header.Children = 2;
        Reject(() => adapter.Accept(observation with { Header = header, Children = [observation.Children[0], observation.Children[0]] }, fixture.Submitted),
            "Selected service cannot return multiple children.");
        var active = Active(fixture);
        header = active.Header; header.Measured = 1;
        Reject(() => adapter.Accept(active with { Header = header }, fixture.Submitted), "An empty active journal cannot report a measured wait.");
        header = active.Header; header.Outstanding = 2;
        Reject(() => adapter.Accept(active with { Header = header }, fixture.Submitted), "Selected service cannot own two outstanding children.");
        header = observation.Header; header.Captured = observation.Children[0].FirstSnapshotCaptured - 1;
        Reject(() => adapter.Accept(observation with { Header = header }, fixture.Submitted), "The snapshot cannot precede the child's first capture.");
        Require(Parsed(SignalAccountingModule.isAwaitingSequence(fixture.Request, fixture.Submitted))
            && SignalAccountingModule.pendingCount(fixture.Submitted) == 1, "Rejected headers must retain submitted ownership.");
        Require(adapter.Accept(observation, fixture.Submitted).Adapter.IsTerminal, "Rejected headers must leave valid settlement available.");
    }

    private static void SelectedReceiptAndOutcomeBounds()
    {
        var fixture = Start();
        var adapter = Adapter(fixture);
        var observation = Observation(fixture);
        var received = observation.Children[0];
        void RejectChild(ReceivedServiceChild child, string message) =>
            Reject(() => adapter.Accept(observation with { Children = [child] }, fixture.Submitted), message);
        RejectChild(received with { FirstManagedReceiptUpper = 101000001 }, "A late managed receipt cannot settle selected service.");
        RejectChild(received with { ClockStatus = 1 }, "A failed receipt clock cannot settle selected service.");
        var native = received.Native;
        native.Ordinal = 2;
        RejectChild(received with { Native = native }, "The selected child ordinal must be one.");
        native = received.Native; native.Requested = 29999;
        RejectChild(received with { Native = native }, "The selected request budget must match exactly.");
        native = received.Native; native.Acknowledgment.Sequence = 0;
        RejectChild(received with { Native = native }, "A zero native sequence cannot bind the selected request.");
        native = received.Native; native.Acknowledgment.Ip = 101;
        RejectChild(received with { Native = native }, "A measured selected wait requires an eligible address.");
        native = received.Native; native.Acknowledgment.WaitResult = -99;
        RejectChild(received with { Native = native }, "A measured outcome cannot carry an interruption result.");
        native = received.Native; native.Acknowledgment.HandlerStarted = native.Acknowledgment.Started - 1;
        RejectChild(received with { Native = native }, "Native phase timestamps must be ordered.");
        Require(Parsed(SignalAccountingModule.isAwaitingSequence(fixture.Request, fixture.Submitted)),
            "Rejected selected children must leave the native sequence unbound.");
        Require(adapter.Accept(observation, fixture.Submitted).Adapter.IsTerminal, "The original submission must still accept its valid child.");
    }
}
