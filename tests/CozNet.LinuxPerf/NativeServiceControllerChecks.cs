using CozNet;
using Microsoft.FSharp.Core;

static class NativeServiceControllerChecks
{
    private sealed class Backend : IServiceInterventionBackend, IServiceSubmissionDiagnostics
    {
        public string Name => "fake-service";
        public int Submissions, Releases;
        public bool FailSubmit, FailRelease;
        public bool CaptureSubmission, InvalidTiming;
        public ServiceSubmissionObservation? LastSubmission { get; private set; }
        public Guid Identity;
        public ulong Command, Budget;
        public ServiceNativeObservation Observation = null!;
        public object Describe() => new { };
        public bool IsEligibleAddress(ulong address) => address == 100;
        public bool IsValidOutcome(ulong outcome, long result) => outcome is 1 or 2 && result == 0;
        public void RegisterCurrentThread(int slot) { }
        public void RetireCurrentThread(int slot) { }
        public void RestoreAfterThreadJoin() { }
        public void Submit(int slot, Guid identity, ulong command, ulong budget)
        {
            Submissions++;
            (Identity, Command, Budget) = (identity, command, budget);
            LastSubmission = CaptureSubmission ? new(slot, identity, command, budget, 10, 20,
                FailSubmit ? null : 0, new() { Entered = 1, ApiLocked = 2, OwnerEntered = 3,
                    OwnerLocked = 4, OwnerFinished = InvalidTiming ? 0UL : 5UL, Returned = 6 }) : null;
            if (FailSubmit) throw new IOException("Ambiguous submission.");
        }
        public ServiceNativeObservation Read(int slot, Guid identity) => Observation;
        public void Release(int slot, Guid identity)
        {
            if (FailRelease) throw new IOException("Release failed.");
            Releases++;
        }
    }

    private static T Parsed<T>(FSharpResult<T, string> result) =>
        result.IsOk ? result.ResultValue : throw new InvalidOperationException(result.ErrorValue);
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
    private static (SignalServiceLease Lease, SignalAccounting State) Reserve()
    {
        var selected = Parsed(SignalAccountingModule.register(FSharpOption<int>.Some(50), SignalAccountingModule.create()));
        var peer = Parsed(SignalAccountingModule.register(FSharpOption<int>.None, selected.Item2));
        var identity = Parsed(SignalAccountingModule.sampleIdentity(selected.Item1, peer.Item2));
        var sample = Parsed(CpuSampleObservationModule.tryCreate(identity, 1, 220000, 0,
            SampleThreadState.Runnable, SampleLocation.Selected));
        var state = Parsed(SignalAccountingModule.observe(selected.Item1, sample, peer.Item2)).Item2;
        state = Parsed(SignalAccountingModule.prepare(selected.Item1, state)).Item2;
        var reserved = Parsed(SignalAccountingModule.prepareService(peer.Item1, state));
        return (reserved.Item1.Value, reserved.Item2);
    }
    private static ReceivedServiceChild Child(ulong ordinal, ulong requested, ulong wait, ulong outcome = 2)
    {
        ulong started = ordinal * 1000000, handler = Math.Max(wait, 1);
        var ack = new ServiceNativeAcknowledgment { Sequence = ordinal, Outcome = outcome, Ip = 100,
            Handler = handler, Wait = wait, Started = started, Queued = started + 1,
            HandlerStarted = started + 2, HandlerFinished = started + 2 + handler, Observed = started + 3 + handler };
        return new(new ServiceNativeChild { Ordinal = ordinal, Requested = requested, Acknowledgment = ack },
            ack.Observed + 1, ack.Observed + 2, 0);
    }
    private static ServiceNativeObservation Observe(Backend backend, ulong phase, params ReceivedServiceChild[] children)
    {
        var identity = NativeServiceClient.Parts(backend.Identity);
        return new(new ServiceNativeSnapshot { IdentityFirst = identity.First, IdentitySecond = identity.Second,
            Command = backend.Command, Budget = backend.Budget, Phase = phase, Outstanding = phase == 1 ? 1UL : 0,
            Children = (ulong)children.Length, Measured = (ulong)children.Sum(child => (long)child.Native.Acknowledgment.Wait),
            Captured = children.Max(child => child.FirstSnapshotCaptured) + 1 }, children, []);
    }
    public static void Run()
    {
        SubmissionTimingRetainsOwnership();
        ServiceSubmissionTimingChecks.Run();
        PeerPrefixesSettleOnce();
        InvalidReceiptRetainsRawJournal();
        InvalidStateCannotSubmit();
        AmbiguousSubmissionRetainsOwnership();
        FailedReleaseRetainsOwnership();
    }

    private static void SubmissionTimingRetainsOwnership()
    {
        foreach (bool failSubmit in new[] { false, true })
        {
            var fixture = Reserve();
            var backend = new Backend { CaptureSubmission = true, InvalidTiming = true, FailSubmit = failSubmit };
            var controller = new NativeServiceController(backend);
            if (failSubmit) Reject<IOException>(() => controller.SubmitPeer(fixture.Lease, fixture.State, "measurement"));
            else Reject<InvalidDataException>(() => controller.SubmitPeer(fixture.Lease, fixture.State, "measurement"));
            Require(controller.HasPending(1) && backend.Submissions == 1 && backend.Releases == 0
                && ReferenceEquals(controller.PendingJournals.Single().Submission, backend.LastSubmission)
                && controller.PendingJournals.Single().SubmissionTiming is null,
                "Submission failure or invalid timing must retain raw evidence and ownership without release.");
            Reject<InvalidOperationException>(() => controller.SubmitPeer(fixture.Lease, fixture.State, "measurement"));
            Require(backend.Submissions == 1, "Timing rejection must prevent reuse of the owned slot.");
        }
        var success = Reserve();
        var validBackend = new Backend { CaptureSubmission = true };
        var validController = new NativeServiceController(validBackend);
        validController.SubmitPeer(success.Lease, success.State, "measurement");
        Require(validController.PendingJournals.Single().SubmissionTiming?.NativeNanoseconds == 5,
            "Accepted timing must retain parsed intervals beside the raw record.");
    }
    private static void PeerPrefixesSettleOnce()
    {
        var fixture = Reserve();
        var backend = new Backend();
        var controller = new NativeServiceController(backend);
        var state = controller.SubmitPeer(fixture.Lease, fixture.State, "measurement");
        var first = Child(1, 50000, 40000);
        backend.Observation = Observe(backend, 1, first);
        var partial = controller.Poll(1, state, "measurement");
        var repeated = controller.Poll(1, partial.State, "measurement");
        Require(!partial.Completed && partial.SettledWait == 0 && controller.ConfirmedPeerWait == 40000
            && ReferenceEquals(partial.State, repeated.State) && backend.Releases == 0,
            "An active prefix must contribute confirmed wait without settlement or duplicate credit.");
        backend.Observation = Observe(backend, 2, first, Child(2, 50000, 45000), Child(3, 25000, 27000));
        var final = controller.Poll(1, repeated.State, "settlement");
        Require(final.Completed && final.SettledWait == 112000 && final.LastSequence == 3
            && SignalAccountingModule.pendingCount(final.State) == 0 && backend.Releases == 1
            && !controller.HasPending(1) && controller.ConfirmedPeerWait == 0 && controller.Journals.Count == 1,
            "Terminal service must settle measured overshoot once and release native ownership.");
        var idle = controller.Poll(1, final.State, "settlement");
        Require(!idle.Completed && idle.SettledWait == 0 && backend.Releases == 1,
            "Polling a released slot must not repeat settlement or release.");
    }
    private static void InvalidReceiptRetainsRawJournal()
    {
        var fixture = Reserve();
        var backend = new Backend();
        var controller = new NativeServiceController(backend);
        var state = controller.SubmitPeer(fixture.Lease, fixture.State, "measurement");
        var child = Child(1, 50000, 0, 1);
        child = child with { FirstManagedReceiptUpper = child.Native.Acknowledgment.Started + 100000001 };
        backend.Observation = Observe(backend, 2, child);
        Reject<InvalidDataException>(() => controller.Poll(1, state, "measurement"));
        Require(controller.HasPending(1) && backend.Releases == 0 && controller.Journals.Count == 0
            && ReferenceEquals(controller.PendingJournals.Single().Observation, backend.Observation)
            && SignalAccountingModule.pendingCount(state) == 1 && controller.ConfirmedPeerWait == 0,
            "A rejected receipt must retain raw evidence and accounting ownership without release.");
    }
    private static void InvalidStateCannotSubmit()
    {
        var fixture = Reserve();
        var backend = new Backend();
        var controller = new NativeServiceController(backend);
        Reject<InvalidDataException>(() => controller.SubmitPeer(fixture.Lease, SignalAccountingModule.create(), "measurement"));
        Require(backend.Submissions == 0 && !controller.HasPending(1),
            "A stale lease must fail before any native submission or slot reservation.");
    }
    private static void AmbiguousSubmissionRetainsOwnership()
    {
        var fixture = Reserve();
        var backend = new Backend { FailSubmit = true };
        var controller = new NativeServiceController(backend);
        Reject<IOException>(() => controller.SubmitPeer(fixture.Lease, fixture.State, "measurement"));
        Require(controller.HasPending(1) && backend.Submissions == 1 && backend.Releases == 0,
            "An ambiguous submission must keep the native slot owned.");
        Reject<InvalidOperationException>(() => controller.SubmitPeer(fixture.Lease, fixture.State, "measurement"));
        Require(backend.Submissions == 1, "An ambiguous slot cannot accept another command.");
    }
    private static void FailedReleaseRetainsOwnership()
    {
        var fixture = Reserve();
        var backend = new Backend { FailRelease = true };
        var controller = new NativeServiceController(backend);
        var state = controller.SubmitPeer(fixture.Lease, fixture.State, "measurement");
        backend.Observation = Observe(backend, 2, Child(1, 50000, 0, 1));
        Reject<IOException>(() => controller.Poll(1, state, "measurement"));
        Require(controller.HasPending(1) && controller.Journals.Count == 0
            && controller.PendingJournals.Single().Observation == backend.Observation
            && SignalAccountingModule.pendingCount(state) == 1,
            "A failed release must retain ownership and evidence without publishing settlement.");
    }
}
