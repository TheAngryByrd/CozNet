using CozNet;
using Microsoft.FSharp.Core;

sealed class ServiceSelectedAdapter
{
    private readonly SignalDelayRequest request;
    private readonly ulong command;
    private readonly Func<ulong, bool> eligible;
    private readonly Func<ulong, long, bool> validOutcome;
    public bool IsTerminal { get; }

    public ServiceSelectedAdapter(SignalDelayRequest request, ulong command, Func<ulong, bool> eligible,
        Func<ulong, long, bool> validOutcome) : this(request, command, eligible, validOutcome, false)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(eligible);
        ArgumentNullException.ThrowIfNull(validOutcome);
        if (command == 0) throw new ArgumentOutOfRangeException(nameof(command));
    }

    private ServiceSelectedAdapter(SignalDelayRequest request, ulong command, Func<ulong, bool> eligible,
        Func<ulong, long, bool> validOutcome, bool terminal) =>
        (this.request, this.command, this.eligible, this.validOutcome, IsTerminal) =
        (request, command, eligible, validOutcome, terminal);

    public (ServiceSelectedAdapter Adapter, SignalAccounting State, SignalAcknowledgment? Acknowledgment)
        Accept(ServiceNativeObservation observation, SignalAccounting state)
    {
        Require(!IsTerminal, "The selected service journal is already settled.");
        Require(Parsed(SignalAccountingModule.isAwaitingSequence(request, state)),
            "The selected request is not awaiting its native sequence.");
        var identity = SignalDelayRequestModule.identity(request);
        var parts = NativeServiceClient.Parts(identity);
        var header = observation.Header;
        Require(header.IdentityFirst == parts.First && header.IdentitySecond == parts.Second && header.Command == command
            && header.Budget == (ulong)SignalDelayRequestModule.nanoseconds(request),
            "The selected journal does not match its submitted request.");
        Require(header.Error == 0 && header.TransportError == 0 && header.Phase is 1 or 2 && header.Cancellation == 0,
            "The selected service journal contains a failure or cancellation.");
        Require(header.Rejected == 0 && observation.Rejected.Length == 0 && header.Captured > 0
            && header.Children == (ulong)observation.Children.Length && header.Outstanding <= 1,
            "The selected snapshot count or lifetime fields are invalid.");
        if (header.Phase == 1)
        {
            Require(header.Children == 0 && header.Measured == 0, "An active selected service contains a completed child.");
            return (this, state, null);
        }
        Require(header.Children == 1 && header.Outstanding == 0,
            "A completed selected service must contain exactly one quiescent child.");
        var received = observation.Children[0];
        var child = received.Native;
        var ack = child.Acknowledgment;
        Require(child.Ordinal == 1 && child.Requested == header.Budget && header.Measured == ack.Wait
            && received.FirstSnapshotCaptured <= header.Captured,
            "The selected child differs from its request or snapshot.");
        Require(validOutcome(ack.Outcome, ack.WaitResult), "The selected outcome and native result disagree.");
        var timing = SignalPhaseTiming.Parse(ack.Started, ack.Queued, ack.HandlerStarted, ack.HandlerFinished, ack.Observed, ack.Handler);
        var receipt = ServiceReceiptTiming.Parse(ack.Started, ack.Observed, received.FirstSnapshotCaptured,
            received.FirstManagedReceiptUpper, received.ClockStatus);
        Parsed(SignalServiceChildModule.tryCreate(identity, 1, ack.Sequence, checked((long)child.Requested),
            checked((long)ack.Wait), checked((long)ack.Handler), checked((long)receipt.UpperNanoseconds),
            checked((int)ack.Outcome), eligible(ack.Ip)));
        var settlement = ack.Outcome == 1 ? SignalSettlement.Deferred
            : SignalSettlement.NewMeasured(Parsed(DelayTicksModule.tryCreate(checked((long)ack.Wait))));
        var bound = Parsed(SignalAccountingModule.sent(request, ack.Sequence, state));
        var completed = Parsed(SignalAccountingModule.complete(request, ack.Sequence, settlement, bound));
        return (new(request, command, eligible, validOutcome, true), completed,
            new(ack.Sequence, ack.Outcome, ack.Ip, ack.Handler, ack.Wait, ack.WaitResult, timing));
    }

    private static T Parsed<T>(FSharpResult<T, string> value) =>
        value.IsOk ? value.ResultValue : throw new InvalidDataException(value.ErrorValue);
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidDataException(message);
    }
}
