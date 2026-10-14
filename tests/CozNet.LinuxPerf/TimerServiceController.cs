using CozNet;
using Microsoft.FSharp.Core;

sealed class TimerServiceController
{
    internal sealed record Journal(uint Slot, Guid Lease, ulong Command, ulong Generation, long Budget,
        long StartedTimestamp, string StartedStage, long ObservedTimestamp, string ObservedStage,
        int? SubmissionStatus, TimerObservation? Accepted, TimerObservation? Attempted, TimerNativeCapture? RawAttempt,
        bool LedgerSettled, bool CancellationRequested, bool ReleaseAttempted, string? Failure);
    internal readonly record struct PollResult(bool Released, long SettledWait);

    private sealed class Pending(SignalServiceLease lease, Journal journal)
    {
        public SignalServiceLease Lease = lease;
        public Journal Journal = journal;
        public TimerAccountingAdapter? Adapter;
        public TimerNativeSnapshot? CopiedHeader;
        public bool CancellationAcknowledged;
        public int JournalIndex = -1;
    }

    private readonly ITimerTransport transport;
    private readonly TimeProvider time;
    private readonly ControllerPhaseCapture? phases;
    private readonly LedgerThread[] threads;
    private readonly ulong begin, size;
    private readonly Pending?[] pending = new Pending?[2];
    private readonly List<Journal> journals = [];
    private readonly long[] releasedWaits = new long[2];
    private ulong command;
    public IReadOnlyList<Journal> Journals => journals;
    public Journal[] PendingJournals => pending.Where(item => item is not null).Select(item => item!.Journal).ToArray();

    public TimerServiceController(ITimerTransport transport, LedgerThread selected, LedgerThread peer,
        ulong begin, ulong size, TimeProvider time, ControllerPhaseCapture? phases = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(peer);
        ArgumentNullException.ThrowIfNull(time);
        Require(!selected.Equals(peer) && begin != 0 && size != 0 && size <= ulong.MaxValue - begin,
            "The timer controller needs distinct workers and a valid instruction range.");
        (this.transport, threads, this.begin, this.size, this.time) = (transport, [selected, peer], begin, size, time);
        this.phases = phases;
    }

    private Pending? At(uint slot)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(slot, 1U);
        return pending[slot];
    }

    public long ReleasedWait(uint slot)
    {
        At(slot);
        return releasedWaits[slot];
    }

    public bool HasPending(uint slot) => At(slot) is not null;
    public long ConfirmedWait(uint slot) => At(slot)?.Adapter?.ConfirmedNanoseconds ?? 0;

    public void Submit(uint slot, SignalServiceLease lease, ref SignalAccounting state, string stage, int iteration = 0)
    {
        Require(At(slot) is null, "The timer controller slot is already owned.");
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        Require(SignalServiceLeaseModule.thread(lease).Equals(threads[slot]), "The timer lease belongs to another slot.");
        Require(journals.Count < 100000, "The timer controller journal bound was exceeded.");
        var submitted = Parsed(SignalAccountingModule.sentService(lease, state));
        ulong nextCommand = checked(command + 1);
        var operation = new Pending(lease, new(slot, SignalServiceLeaseModule.identity(lease), nextCommand, 0,
            SignalServiceLeaseModule.nanoseconds(lease), time.GetTimestamp(), stage, 0, stage,
            null, null, null, null, false, false, false, null));
        command = nextCommand;
        pending[slot] = operation;
        state = submitted;
        try
        {
            phases?.Begin(stage, slot == 0 ? "submit-selected" : "submit-peer", iteration);
            ulong generation;
            try { generation = transport.Submit(slot, command, checked((ulong)operation.Journal.Budget)); }
            finally
            {
                var result = transport.LastSubmission;
                operation.Journal = operation.Journal with { Generation = result?.Generation ?? 0, SubmissionStatus = result?.Status };
            }
            Require(transport.LastSubmission is { Status: 0 } receipt && receipt.Generation == generation,
                "The timer submission result does not match its generation.");
            operation.Adapter = new(TimerAccountingBinding.Parse(lease, generation, command, begin, size));
            phases?.End();
        }
        catch (Exception error)
        {
            var failure = RecordFailure(operation, error);
            if (ReferenceEquals(failure, error)) throw;
            throw failure;
        }
    }

    public void Cancel(uint slot, ref SignalAccounting state)
    {
        var operation = At(slot) ?? throw new InvalidOperationException("The timer slot has no pending lease.");
        Require(!operation.Journal.ReleaseAttempted, "A timer release remains unresolved.");
        if (operation.Journal.CancellationRequested)
        {
            Require(operation.CancellationAcknowledged, "A timer cancellation remains unresolved.");
            return;
        }
        if (operation.Adapter is { } adapter)
        {
            Require(!adapter.IsTerminal, "A settled timer lease cannot request cancellation.");
            var cancelled = adapter.RequestCancellation(state);
            operation.Adapter = cancelled.Adapter;
            state = cancelled.State;
        }
        else state = Parsed(SignalAccountingModule.requestServiceCancellation(operation.Lease, state));
        operation.Journal = operation.Journal with { CancellationRequested = true };
        try
        {
            transport.Cancel(slot);
            operation.CancellationAcknowledged = true;
        }
        catch (Exception error)
        {
            operation.Journal = operation.Journal with { Failure = error.ToString() };
            throw;
        }
    }

    public PollResult Poll(uint slot, ref SignalAccounting state, string stage, int iteration = 0)
    {
        var operation = At(slot);
        if (operation is null) return new(false, 0);
        Require(!operation.Journal.ReleaseAttempted, "A timer release remains unresolved.");
        try
        {
            Require(operation.Adapter is not null, "The timer submission has no accepted generation binding.");
            var header = transport.Probe(slot);
            if (operation.CopiedHeader is { } copied && header.Captured >= copied.Captured)
            {
                var comparable = header;
                comparable.Captured = copied.Captured;
                if (comparable.Equals(copied)) return new(false, 0);
            }
            phases?.Begin(stage, slot == 0 ? "read-selected" : "read-peer", iteration);
            TimerObservation observation;
            var earlierRaw = transport.LastRaw;
            operation.Journal = operation.Journal with { Attempted = null, RawAttempt = null };
            try { observation = transport.Read(slot); }
            finally
            {
                operation.Journal = operation.Journal with { RawAttempt = ReferenceEquals(earlierRaw, transport.LastRaw) ? null : transport.LastRaw,
                    ObservedTimestamp = time.GetTimestamp(), ObservedStage = stage };
            }
            operation.Journal = operation.Journal with { Attempted = observation, RawAttempt = observation.Raw };
            var adapter = operation.Adapter!;
            if (!adapter.IsTerminal)
            {
                var applied = adapter.Accept(observation, state);
                operation.Adapter = adapter = applied.Adapter;
                state = applied.State;
            }
            else CheckRetiredTransition(operation.Journal.Accepted!, observation);
            operation.Journal = operation.Journal with { Accepted = observation, LedgerSettled = adapter.IsTerminal };
            operation.CopiedHeader = observation.Raw.Header;
            if (observation.Raw.Header.Visibility != 3)
            {
                phases?.End();
                return new(false, 0);
            }
            Require(adapter.IsTerminal, "Retired timer ownership has no ledger settlement.");
            long releasedTotal = checked(releasedWaits[slot] + adapter.ConfirmedNanoseconds);
            operation.Journal = operation.Journal with { ReleaseAttempted = true };
            transport.ReleaseAfterRetiredCopy(slot);
            releasedWaits[slot] = releasedTotal;
            operation.JournalIndex = journals.Count;
            journals.Add(operation.Journal);
            pending[slot] = null;
            phases?.End();
            return new(true, adapter.ConfirmedNanoseconds);
        }
        catch (Exception error)
        {
            var failure = RecordFailure(operation, error);
            if (ReferenceEquals(failure, error)) throw;
            throw failure;
        }
    }

    private Exception RecordFailure(Pending operation, Exception error)
    {
        Exception failure = error;
        try { phases?.End(false); }
        catch (Exception phaseError) { failure = new AggregateException(error, phaseError); }
        operation.Journal = operation.Journal with { Failure = failure.ToString() };
        if (operation.JournalIndex >= 0) journals[operation.JournalIndex] = operation.Journal;
        return failure;
    }

    private static void CheckRetiredTransition(TimerObservation previous, TimerObservation current)
    {
        var before = previous.Raw.Header;
        var after = current.Raw.Header;
        Require(after.Visibility >= before.Visibility && after.Captured >= before.Captured
            && current.Raw.Clock.Status == 0 && current.Raw.Clock.Nanoseconds >= after.Captured
            && current.Raw.Clock.Nanoseconds >= previous.Raw.Clock.Nanoseconds,
            "A settled timer journal regressed its visibility or clock.");
        after.Visibility = before.Visibility;
        after.Captured = before.Captured;
        Require(after.Equals(before) && current.Raw.Children.SequenceEqual(previous.Raw.Children)
            && current.Received.Count == previous.Received.Count
            && current.Received.Zip(previous.Received).All(pair => pair.First.Raw == pair.Second.Raw
                && pair.First.FirstCaptured == pair.Second.FirstCaptured
                && pair.First.FirstManagedReceiptUpper == pair.Second.FirstManagedReceiptUpper
                && pair.First.Timing.UpperNanoseconds == pair.Second.Timing.UpperNanoseconds),
            "A settled timer journal changed before release.");
    }

    private static T Parsed<T>(FSharpResult<T, string> value) => value.IsOk ? value.ResultValue : throw new InvalidDataException(value.ErrorValue);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
