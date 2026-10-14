using System.Diagnostics;
using CozNet;
using Microsoft.FSharp.Core;

sealed class NativeServiceController(IServiceInterventionBackend backend)
{
    internal sealed record Journal(int Slot, Guid Identity, ulong Command, long BudgetNanoseconds,
        long StartedTimestamp, string StartedStage, long ObservedTimestamp, string ObservedStage,
        ServiceNativeObservation? Observation)
    {
        public ServiceSubmissionObservation? Submission { get; init; }
        public ServiceSubmissionTiming? SubmissionTiming { get; init; }
    }
    internal readonly record struct PollResult(SignalAccounting State, bool Completed, long SettledWait, ulong LastSequence);
    private sealed class Pending(Journal journal, ServiceSelectedAdapter? selected, ServiceAccountingAdapter? peer)
    {
        internal Journal Journal = journal;
        internal ServiceSelectedAdapter? Selected = selected;
        internal ServiceAccountingAdapter? Peer = peer;
        internal int AcceptedCount;
    }
    private readonly Pending?[] pending = new Pending?[2];
    private readonly List<Journal> journals = [];
    private ulong command;
    private long acceptedChildren;
    public IReadOnlyList<Journal> Journals => journals;
    public Journal[] PendingJournals => pending.Where(item => item is not null).Select(item => item!.Journal).ToArray();
    public bool HasPending(int slot) => pending[slot] is not null;
    public long ConfirmedPeerWait => pending[1]?.Peer?.ConfirmedNanoseconds ?? 0;

    public SignalAccounting SubmitSelected(SignalDelayRequest request, SignalAccounting state, string stage)
    {
        var submitted = Parsed(SignalAccountingModule.submitted(request, state));
        var operation = NewOperation(0, SignalDelayRequestModule.identity(request), SignalDelayRequestModule.nanoseconds(request), stage);
        pending[0] = new(operation, new(request, operation.Command, backend.IsEligibleAddress, backend.IsValidOutcome), null);
        Submit(operation);
        return submitted;
    }

    public SignalAccounting SubmitPeer(SignalServiceLease lease, SignalAccounting state, string stage)
    {
        var submitted = Parsed(SignalAccountingModule.sentService(lease, state));
        var operation = NewOperation(1, SignalServiceLeaseModule.identity(lease), SignalServiceLeaseModule.nanoseconds(lease), stage);
        pending[1] = new(operation, null, new(lease, operation.Command, backend.IsEligibleAddress, backend.IsValidOutcome));
        Submit(operation);
        return submitted;
    }

    private void Submit(Journal operation)
    {
        var pendingOperation = pending[operation.Slot]!;
        try
        {
            backend.Submit(operation.Slot, operation.Identity, operation.Command, (ulong)operation.BudgetNanoseconds);
        }
        finally
        {
            if (backend is IServiceSubmissionDiagnostics diagnostics)
                pendingOperation.Journal = pendingOperation.Journal with { Submission = diagnostics.LastSubmission };
        }
        if (pendingOperation.Journal.Submission is { } observation)
        {
            var timing = ServiceSubmissionTiming.Parse(observation, operation.Slot, operation.Identity,
                operation.Command, (ulong)operation.BudgetNanoseconds);
            pendingOperation.Journal = pendingOperation.Journal with { SubmissionTiming = timing };
        }
    }

    private Journal NewOperation(int slot, Guid identity, long budget, string stage)
    {
        if (pending[slot] is not null) throw new InvalidOperationException("The native service slot is already owned.");
        if (journals.Count >= 100000) throw new InvalidOperationException("The native service command capture limit was exceeded.");
        command = checked(command + 1);
        return new(slot, identity, command, budget, Stopwatch.GetTimestamp(), stage, 0, stage, null);
    }

    public PollResult Poll(int slot, SignalAccounting state, string stage)
    {
        var operation = pending[slot];
        if (operation is null) return new(state, false, 0, 0);
        if (slot == 0 && Stopwatch.GetElapsedTime(operation.Journal.StartedTimestamp).TotalMilliseconds > 100)
            throw new TimeoutException("A selected native service command exceeded 100 milliseconds.");
        var observation = backend.Read(slot, operation.Journal.Identity);
        operation.Journal = operation.Journal with { Observation = observation, ObservedTimestamp = Stopwatch.GetTimestamp(), ObservedStage = stage };
        long newCount = observation.Children.LongLength - operation.AcceptedCount;
        if (newCount < 0 || acceptedChildren + newCount > 100000)
            throw new InvalidDataException("The native service child capture limit or prefix count is invalid.");
        SignalAccounting next;
        ServiceSelectedAdapter? selected = null;
        ServiceAccountingAdapter? peer = null;
        bool completed;
        long wait;
        if (slot == 0)
        {
            var applied = operation.Selected!.Accept(observation, state);
            next = applied.State; selected = applied.Adapter; completed = selected.IsTerminal;
            wait = applied.Acknowledgment is null ? 0 : checked((long)applied.Acknowledgment.WaitNanoseconds);
        }
        else
        {
            var applied = operation.Peer!.Accept(observation, state);
            next = applied.State; peer = applied.Adapter; completed = peer.IsTerminal;
            wait = completed ? peer.ConfirmedNanoseconds : 0;
        }
        if (completed) backend.Release(slot, operation.Journal.Identity);
        acceptedChildren += newCount;
        ulong sequence = observation.Children.Length == 0 ? 0 : observation.Children[^1].Native.Acknowledgment.Sequence;
        if (completed)
        {
            journals.Add(operation.Journal);
            pending[slot] = null;
        }
        else
        {
            operation.Selected = selected;
            operation.Peer = peer;
            operation.AcceptedCount = observation.Children.Length;
        }
        return new(next, completed, wait, sequence);
    }

    private static T Parsed<T>(FSharpResult<T, string> result) => result.IsOk
        ? result.ResultValue : throw new InvalidDataException(result.ErrorValue);
}
