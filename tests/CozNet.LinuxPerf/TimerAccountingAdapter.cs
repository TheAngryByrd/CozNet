using CozNet;
using Microsoft.FSharp.Core;

sealed class TimerAccountingBinding
{
    public SignalServiceLease Lease { get; }
    public ulong Generation { get; }
    public ulong Command { get; }
    private readonly ulong begin, size;

    private TimerAccountingBinding(SignalServiceLease lease, ulong generation, ulong command, ulong begin, ulong size)
        => (Lease, Generation, Command, this.begin, this.size) = (lease, generation, command, begin, size);

    public static TimerAccountingBinding Parse(SignalServiceLease lease, ulong generation, ulong command, ulong begin, ulong size)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (generation is 0 or > uint.MaxValue || command == 0 || begin == 0 || size == 0 || size > ulong.MaxValue - begin)
            throw new InvalidDataException("The timer accounting binding has an invalid generation, command, or instruction range.");
        return new(lease, generation, command, begin, size);
    }

    public bool Eligible(ulong ip) => ip >= begin && ip - begin < size;
    public ulong Sequence(ulong ordinal)
    {
        if (ordinal is 0 or > 100000) throw new InvalidDataException("The timer child ordinal exceeds the journal bound.");
        return (Generation << 32) | ordinal;
    }
}

sealed class TimerAccountingAdapter
{
    private readonly TimerAccountingBinding binding;
    private readonly TargetLeaseReceivedChild[] accepted;
    private readonly TimerNativeSnapshot? previous;
    private readonly ulong previousReceipt;
    private readonly bool cancellationRequested;
    public long ConfirmedNanoseconds { get; }
    public int AcceptedCount => accepted.Length;
    public bool IsTerminal { get; }

    public TimerAccountingAdapter(TimerAccountingBinding binding)
        : this(binding, [], null, 0, false, 0, false) => ArgumentNullException.ThrowIfNull(binding);

    private TimerAccountingAdapter(TimerAccountingBinding binding, TargetLeaseReceivedChild[] accepted,
        TimerNativeSnapshot? previous, ulong previousReceipt, bool cancellationRequested, long confirmed, bool terminal)
    {
        this.binding = binding;
        this.accepted = accepted;
        this.previous = previous;
        this.previousReceipt = previousReceipt;
        this.cancellationRequested = cancellationRequested;
        ConfirmedNanoseconds = confirmed;
        IsTerminal = terminal;
    }

    public (TimerAccountingAdapter Adapter, SignalAccounting State) RequestCancellation(SignalAccounting state)
    {
        Require(!IsTerminal, "A settled timer journal cannot request cancellation.");
        RequireProgress(state);
        var next = Parsed(SignalAccountingModule.requestServiceCancellation(binding.Lease, state));
        return (new(binding, accepted, previous, previousReceipt, true, ConfirmedNanoseconds, false), next);
    }

    private void RequireProgress(SignalAccounting state)
    {
        var progress = Parsed(SignalAccountingModule.serviceProgress(binding.Lease, state));
        ulong sequence = accepted.Length == 0 ? 0 : binding.Sequence(accepted[^1].Raw.Ordinal);
        Require(progress.Count == accepted.LongLength && progress.LastSequence == sequence
            && progress.MeasuredNanoseconds == ConfirmedNanoseconds, "The accounting state does not match the accepted timer prefix.");
    }

    public (TimerAccountingAdapter Adapter, SignalAccounting State) Accept(TimerObservation observation, SignalAccounting state)
    {
        Require(!IsTerminal, "The timer journal is already settled.");
        RequireProgress(state);
        var header = observation.Raw.Header;
        var children = observation.Received.ToArray();
        long budget = SignalServiceLeaseModule.nanoseconds(binding.Lease);
        Require(header.Generation == binding.Generation && header.Command == binding.Command && header.Budget == (ulong)budget,
            "The timer journal does not match its accounting binding.");
        Require(header.Error == 0 && header.TransportError == 0 && header.BackendError == 0
            && header.Visibility <= 3 && header.Stop <= 4 && header.MayHaveDelivery <= 1,
            "The timer journal contains a failure or invalid state.");
        Require(header.Children == (ulong)children.Length && children.Length == observation.Raw.Children.Length
            && children.Length >= accepted.Length && children.Length <= 100000, "The timer journal count is invalid.");
        Require(header.Captured > 0 && observation.Raw.Clock.Status == 0 && observation.Raw.Clock.Nanoseconds >= header.Captured
            && observation.Raw.Clock.Nanoseconds >= previousReceipt, "The timer snapshot clock is invalid.");
        Require(header.Visibility == 0 ? header.Stop == 0 && header.MayHaveDelivery == 1 : header.Stop != 0,
            "The timer visibility contradicts its stop state.");
        Require(header.Visibility < 2 || header.MayHaveDelivery == 0, "A quiescent timer still reports possible delivery.");
        Require(header.Stop != 3 || cancellationRequested, "Timer cancellation has no managed intent.");
        if (previous is { } earlier)
        {
            Require(header.Captured >= earlier.Captured && header.Visibility >= earlier.Visibility,
                "Timer capture time or visibility moved backwards.");
            Require(earlier.Visibility == 0 || (header.Children == earlier.Children && header.Measured == earlier.Measured
                && header.Stop == earlier.Stop && (header.Visibility != 1 || header.MayHaveDelivery == earlier.MayHaveDelivery)),
                "A terminal timer journal changed.");
        }

        long confirmed = ConfirmedNanoseconds;
        var next = state;
        int lastOutcome = 0;
        for (int index = 0; index < children.Length; index++)
        {
            var received = children[index];
            var child = received.Raw;
            var raw = observation.Raw.Children[index];
            Require(raw.Ordinal == child.Ordinal && raw.Requested == child.Requested && raw.Armed == child.Armed
                && raw.Started == child.Started && raw.Finished == child.Finished && raw.Waited == child.Waited
                && raw.Ip == child.InstructionPointer && raw.WaitStatus == (ulong)child.WaitStatus && raw.Valid == 1 && child.Valid,
                "The accepted timer child differs from its raw capture or is invalid.");
            Require(child.Ordinal == (ulong)index + 1 && child.Started >= child.Armed && child.Finished >= child.Started
                && child.WaitStatus is 0 or 1, "The timer child has an invalid ordinal, clock, or wait status.");
            Require(child.Requested is > 0 and <= 50000 && child.Finished - child.Started <= 5000000
                && child.Waited <= child.Finished - child.Started, "The timer child wait or handler interval exceeds its bound.");
            if (index > 0)
            {
                var prior = children[index - 1].Raw;
                Require(child.Armed >= prior.Started && child.Armed <= prior.Finished && child.Started >= prior.Finished,
                    "The timer continuation does not follow its preceding handler.");
            }
            var timing = ServiceReceiptTiming.Parse(child.Armed, child.Finished, received.FirstCaptured,
                received.FirstManagedReceiptUpper, 0);
            Require(received.FirstCaptured <= header.Captured && received.FirstManagedReceiptUpper <= observation.Raw.Clock.Nanoseconds
                && received.Timing.UpperNanoseconds == timing.UpperNanoseconds, "The timer child first receipt is inconsistent.");
            bool eligible = binding.Eligible(child.InstructionPointer);
            Require(eligible || (child.Waited == 0 && child.WaitStatus == 0), "An ineligible timer child contains wait.");
            lastOutcome = !eligible ? 1 : child.WaitStatus == 1 ? 3 : 2;
            if (index < accepted.Length)
            {
                var prior = accepted[index];
                Require(prior.Raw == child && prior.FirstCaptured == received.FirstCaptured
                    && prior.FirstManagedReceiptUpper == received.FirstManagedReceiptUpper
                    && prior.Timing.UpperNanoseconds == timing.UpperNanoseconds, "A previously accepted timer receipt changed.");
                continue;
            }
            var parsed = Parsed(SignalServiceChildModule.tryCreate(SignalServiceLeaseModule.identity(binding.Lease),
                checked((long)child.Ordinal), binding.Sequence(child.Ordinal), checked((long)child.Requested),
                checked((long)child.Waited), checked((long)(child.Finished - child.Started)), checked((long)timing.UpperNanoseconds),
                lastOutcome, eligible));
            next = Parsed(SignalAccountingModule.acceptServiceChild(binding.Lease, parsed, next));
            confirmed = checked(confirmed + (long)child.Waited);
        }
        Require(header.Measured == (ulong)confirmed, "The timer aggregate differs from accepted child waits.");
        bool stopped = children.Length > 0 && (confirmed >= budget || lastOutcome != 2);
        Require(header.Visibility != 0 || !stopped, "An active timer journal contains a terminal child.");
        Require(header.Stop != 1 || (children.Length > 0 && lastOutcome == 2 && confirmed >= budget),
            "The completed timer has not serviced its budget.");
        Require(header.Stop != 2 || (children.Length > 0 && lastOutcome == 1), "The deferred timer has no deferred child.");
        Require(header.Stop != 4 || (children.Length > 0 && lastOutcome == 3), "The interrupted timer has no interrupted child.");
        bool settled = header.Visibility >= 2;
        if (settled)
        {
            if (header.Stop == 3)
            {
                ulong sequence = children.Length == 0 ? 0 : binding.Sequence(children[^1].Raw.Ordinal);
                var receipt = Parsed(SignalServiceStopReceiptModule.tryCreate(SignalServiceLeaseModule.identity(binding.Lease),
                    children.LongLength, sequence, confirmed, 0));
                next = Parsed(SignalAccountingModule.confirmServiceStopped(binding.Lease, receipt, next)).Item2;
            }
            else next = Parsed(SignalAccountingModule.completeService(binding.Lease, next)).Item2;
        }
        return (new(binding, children, header, observation.Raw.Clock.Nanoseconds, cancellationRequested, confirmed, settled), next);
    }

    private static T Parsed<T>(FSharpResult<T, string> value) => value.IsOk ? value.ResultValue : throw new InvalidDataException(value.ErrorValue);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
