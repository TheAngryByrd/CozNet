using CozNet;
using Microsoft.FSharp.Core;

sealed class ServiceAccountingAdapter
{
    private readonly SignalServiceLease lease;
    private readonly ulong command;
    private readonly Func<ulong, bool> eligible;
    private readonly Func<ulong, long, bool> validOutcome;
    private readonly ReceivedServiceChild[] accepted;
    private readonly bool cancellationRequested;
    public long ConfirmedNanoseconds { get; }
    public int AcceptedCount => accepted.Length;
    public bool IsTerminal { get; }

    public ServiceAccountingAdapter(SignalServiceLease lease, ulong command, Func<ulong, bool> eligible,
        Func<ulong, long, bool> validOutcome)
        : this(lease, command, eligible, validOutcome, [], 0, false, false)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(eligible);
        ArgumentNullException.ThrowIfNull(validOutcome);
        if (command == 0) throw new ArgumentOutOfRangeException(nameof(command));
    }

    private ServiceAccountingAdapter(SignalServiceLease lease, ulong command, Func<ulong, bool> eligible,
        Func<ulong, long, bool> validOutcome, ReceivedServiceChild[] accepted, long confirmed,
        bool cancellationRequested, bool terminal)
    {
        this.lease = lease;
        this.command = command;
        this.eligible = eligible;
        this.validOutcome = validOutcome;
        this.accepted = accepted;
        ConfirmedNanoseconds = confirmed;
        this.cancellationRequested = cancellationRequested;
        IsTerminal = terminal;
    }

    public (ServiceAccountingAdapter Adapter, SignalAccounting State) RequestCancellation(SignalAccounting state)
    {
        Require(!IsTerminal, "A completed service journal cannot request cancellation.");
        RequireProgress(state);
        var next = Parsed(SignalAccountingModule.requestServiceCancellation(lease, state));
        return (new(lease, command, eligible, validOutcome, accepted, ConfirmedNanoseconds, true, false), next);
    }

    private void RequireProgress(SignalAccounting state)
    {
        var progress = Parsed(SignalAccountingModule.serviceProgress(lease, state));
        ulong lastSequence = accepted.Length == 0 ? 0 : accepted[^1].Native.Acknowledgment.Sequence;
        Require(progress.Count == accepted.LongLength && progress.LastSequence == lastSequence &&
            progress.MeasuredNanoseconds == ConfirmedNanoseconds,
            "The accounting state does not match the accepted service prefix.");
    }

    public (ServiceAccountingAdapter Adapter, SignalAccounting State) Accept(ServiceNativeObservation observation, SignalAccounting state)
    {
        Require(!IsTerminal, "The service journal is already settled.");
        RequireProgress(state);
        var header = observation.Header;
        var identity = SignalServiceLeaseModule.identity(lease);
        var parts = NativeServiceClient.Parts(identity);
        long budget = SignalServiceLeaseModule.nanoseconds(lease);
        Require(header.IdentityFirst == parts.First && header.IdentitySecond == parts.Second && header.Command == command
            && header.Budget == (ulong)budget, "The native journal does not match its lease.");
        Require(header.Error == 0 && header.TransportError == 0 && header.Phase is >= 1 and <= 3,
            "The native service journal contains a failure.");
        Require(header.Children == (ulong)observation.Children.Length && header.Children <= 100000
            && header.Rejected == 0 && observation.Rejected.Length == 0 && header.Children >= (ulong)accepted.Length,
            "The service journal count or rejected records are invalid.");
        Require(header.Captured > 0 && header.Outstanding <= 1 && header.Cancellation <= 1
            && (header.Phase == 1 || header.Outstanding == 0), "The service snapshot has invalid lifetime state.");
        Require(header.Cancellation == 0 || cancellationRequested, "The native service cancellation has no managed intent.");
        Require(header.Phase != 3 || (cancellationRequested && header.Cancellation == 1),
            "The cancelled service has no matched cancellation intent.");
        for (int index = 0; index < accepted.Length; index++)
            Require(accepted[index] == observation.Children[index] && accepted[index].FirstSnapshotCaptured <= header.Captured,
                "A previously accepted service child changed or its snapshot clock regressed.");

        long confirmed = ConfirmedNanoseconds;
        var next = state;
        for (int index = accepted.Length; index < observation.Children.Length; index++)
        {
            var received = observation.Children[index];
            var child = received.Native;
            var ack = child.Acknowledgment;
            Require(child.Ordinal == (ulong)index + 1 && received.FirstSnapshotCaptured <= header.Captured,
                "The service child ordinal or first snapshot clock is invalid.");
            Require(validOutcome(ack.Outcome, ack.WaitResult), "The native service outcome and result disagree.");
            SignalPhaseTiming.Parse(ack.Started, ack.Queued, ack.HandlerStarted, ack.HandlerFinished, ack.Observed, ack.Handler);
            var receipt = ServiceReceiptTiming.Parse(ack.Started, ack.Observed, received.FirstSnapshotCaptured,
                received.FirstManagedReceiptUpper, received.ClockStatus);
            var parsed = Parsed(SignalServiceChildModule.tryCreate(identity, checked((long)child.Ordinal), ack.Sequence,
                checked((long)child.Requested), checked((long)ack.Wait), checked((long)ack.Handler),
                checked((long)receipt.UpperNanoseconds), checked((int)ack.Outcome), eligible(ack.Ip)));
            next = Parsed(SignalAccountingModule.acceptServiceChild(lease, parsed, next));
            confirmed = checked(confirmed + (long)ack.Wait);
        }
        Require(header.Measured == (ulong)confirmed, "The native aggregate differs from the accepted child waits.");
        bool journalStopped = observation.Children.Length > 0
            && (confirmed >= budget || observation.Children[^1].Native.Acknowledgment.Outcome != 2);
        Require(header.Phase != 1 || !journalStopped, "An active service contains a terminal child.");
        if (header.Phase == 2)
        {
            Require(journalStopped, "The completed service has no terminal child.");
            next = Parsed(SignalAccountingModule.completeService(lease, next)).Item2;
        }
        else if (header.Phase == 3)
        {
            ulong sequence = observation.Children.Length == 0 ? 0 : observation.Children[^1].Native.Acknowledgment.Sequence;
            var receipt = Parsed(SignalServiceStopReceiptModule.tryCreate(identity, observation.Children.LongLength,
                sequence, confirmed, 0));
            next = Parsed(SignalAccountingModule.confirmServiceStopped(lease, receipt, next)).Item2;
        }
        return (new(lease, command, eligible, validOutcome, observation.Children.ToArray(), confirmed,
            cancellationRequested, header.Phase != 1), next);
    }

    private static T Parsed<T>(FSharpResult<T, string> value) =>
        value.IsOk ? value.ResultValue : throw new InvalidDataException(value.ErrorValue);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
