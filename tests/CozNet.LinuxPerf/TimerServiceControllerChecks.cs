using CozNet;
using Microsoft.FSharp.Core;

static class TimerServiceControllerChecks
{
    private sealed class Clock : IThreadCpuClock
    {
        private long value;
        public int Reads, FailOnRead = int.MaxValue;
        public readonly Exception Failure = new IOException("diagnostic clock failure");
        public long ReadNanoseconds()
        {
            if (++Reads >= FailOnRead) throw Failure;
            return ++value;
        }
    }
    private sealed class Transport : ITimerTransport
    {
        public (int Status, ulong Generation)? LastSubmission { get; private set; }
        public TimerNativeCapture? LastRaw { get; private set; }
        public TimerObservation Observation = null!;
        public bool FailSubmit, FailRead, FailCancel, FailRelease;
        public int Releases, Cancellations;
        public ulong Generation = 1;
        public ulong Submit(uint slot, ulong command, ulong budget)
        {
            LastSubmission = (FailSubmit ? 22 : 0, Generation);
            if (FailSubmit) throw new IOException("submit failure");
            return Generation;
        }
        public TimerNativeSnapshot Probe(uint slot) => Observation.Raw.Header;
        public TimerObservation Read(uint slot)
        {
            if (FailRead) throw new IOException("read failure");
            LastRaw = Observation.Raw;
            return Observation;
        }
        public void Cancel(uint slot)
        {
            Cancellations++;
            if (FailCancel) throw new IOException("cancel failure");
        }
        public void ReleaseAfterRetiredCopy(uint slot)
        {
            Releases++;
            if (FailRelease) throw new IOException("release failure");
        }
    }

    private sealed class Fixture
    {
        public readonly Transport Transport = new();
        public readonly TimerServiceController Controller;
        public readonly SignalServiceLease Lease;
        public readonly LedgerThread Peer;
        public readonly LedgerThread Selected;
        public SignalAccounting State;
        public Fixture(ControllerPhaseCapture? phases = null)
        {
            var selected = Parsed(SignalAccountingModule.register(FSharpOption<int>.Some(50), SignalAccountingModule.create()));
            Selected = selected.Item1;
            var peer = Parsed(SignalAccountingModule.register(FSharpOption<int>.None, selected.Item2));
            Peer = peer.Item1;
            var identity = Parsed(SignalAccountingModule.sampleIdentity(selected.Item1, peer.Item2));
            var sample = Parsed(CpuSampleObservationModule.tryCreate(identity, 1, 220000, 0,
                SampleThreadState.Runnable, SampleLocation.Selected));
            State = Parsed(SignalAccountingModule.observe(selected.Item1, sample, peer.Item2)).Item2;
            State = Parsed(SignalAccountingModule.prepare(selected.Item1, State)).Item2;
            var reserved = Parsed(SignalAccountingModule.prepareService(Peer, State));
            Lease = reserved.Item1.Value;
            State = reserved.Item2;
            Controller = new(Transport, selected.Item1, Peer, 100, 20, TimeProvider.System, phases);
        }
        public void Submit() => Controller.Submit(1, Lease, ref State, "check");
        public TimerServiceController.PollResult Poll() => Controller.Poll(1, ref State, "check");
        public long Debt() => SignalServiceLeaseModule.nanoseconds(Parsed(SignalAccountingModule.prepareService(Peer, State)).Item1.Value);
    }

    private static TimerObservation Observation(ulong visibility, ulong stop, ulong wait = 0, ulong ip = 120,
        ulong budget = 110000, ulong command = 1, ulong started = 2000, ulong generation = 1)
    {
        ulong requested = Math.Min(50000UL, budget);
        var child = new TargetLeaseRawChild(1, requested, 999, started, started + Math.Max(wait, 1), wait, ip, 0, true);
        var received = new TargetLeaseReceivedChild(child, child.Finished + 1, child.Finished + 2,
            ServiceReceiptTiming.Parse(child.Armed, child.Finished, child.Finished + 1, child.Finished + 2, 0));
        var native = new TimerNativeChild { Ordinal = 1, Requested = requested, Armed = child.Armed, Started = child.Started,
            Finished = child.Finished, Waited = wait, Ip = ip, Valid = 1 };
        var header = new TimerNativeSnapshot { Generation = generation, Command = command, Budget = budget, Children = 1,
            Measured = wait, Visibility = visibility, Stop = stop, MayHaveDelivery = visibility == 0 ? 1UL : 0,
            Captured = child.Finished + 2 };
        return new(new(header, [native], new(0, header.Captured + 1)), [received]);
    }

    public static void Run()
    {
        PartialPrefixDoesNotPayTwice();
        SelectedPrefixPreservesUnpaidDebt();
        EquivalentRetiredReceiptReleases();
        ChangedHeaderCannotHideFailure();
        ReadFailureDoesNotReuseRawCapture();
        CancellationFailureRemainsVisible();
        ReleaseFailureRetainsOwnership();
        SubmissionFailureRetainsSentLease();
        WrongSlotDoesNotSubmit();
        PhaseCaptureKeepsServiceAfterIdlePolls();
        UnavailableTargetRetainsGrowingDebt();
        DelayedDeliveryCreditsOnlyConfirmedWait();
        CombinedOperationAndPhaseFailuresRemainVisible();
        ClosingClockFailurePreservesReleasedWait();
        CancellationCanObserveCompletedOrDeferredJournal();
        ReleasedWaitAccumulatesAcrossGenerations();
    }

    private static Exception Failure(Action action)
    {
        try { action(); }
        catch (Exception error) { return error; }
        throw new InvalidOperationException("Expected an operation failure.");
    }

    private static void ReleasedWaitAccumulatesAcrossGenerations()
    {
        var f = new Fixture();
        f.Submit();
        f.Controller.Cancel(1, ref f.State);
        f.Transport.Observation = Observation(3, 3, 40000, 100);
        Require(f.Poll().Released && f.Controller.ReleasedWait(1) == 40000 && f.Debt() == 70000,
            "The first generation must release its payment and preserve the remaining debt.");
        var reserved = Parsed(SignalAccountingModule.prepareService(f.Peer, f.State));
        f.State = reserved.Item2;
        f.Transport.Generation = 2;
        f.Controller.Submit(1, reserved.Item1.Value, ref f.State, "check");
        f.Transport.Observation = Observation(3, 1, 70000, 100, budget: 70000, command: 2, generation: 2);
        Require(f.Poll().Released && f.Controller.ReleasedWait(1) == 110000 && f.Controller.ReleasedWait(0) == 0
            && f.Transport.Releases == 2 && !f.Controller.HasPending(1) && SignalAccountingModule.pendingCount(f.State) == 0
            && Parsed(SignalAccountingModule.prepareService(f.Peer, f.State)).Item1 is null
            && f.Controller.Journals.Select(journal => journal.Generation).SequenceEqual(new ulong[] { 1, 2 }),
            "Released waits must accumulate across generations without changing another slot or leaving debt or ownership.");
        Require(!f.Poll().Released && f.Controller.ReleasedWait(1) == 110000 && f.Transport.Releases == 2,
            "An idle poll must not repeat either generation's payment or native release.");
    }

    private static void CancellationCanObserveCompletedOrDeferredJournal()
    {
        foreach (bool completed in new[] { true, false })
        {
            var f = new Fixture();
            f.Submit();
            f.Controller.Cancel(1, ref f.State);
            f.Transport.Observation = completed ? Observation(3, 1, 110000, 100) : Observation(3, 2);
            var result = f.Poll();
            var next = Parsed(SignalAccountingModule.prepareService(f.Peer, f.State));
            Require(result.Released && f.Transport.Cancellations == 1 && f.Transport.Releases == 1
                && !f.Controller.HasPending(1) && SignalAccountingModule.pendingCount(f.State) == 0
                && f.Controller.ReleasedWait(1) == (completed ? 110000 : 0)
                && (completed ? next.Item1 is null : next.Item1 is not null && SignalServiceLeaseModule.nanoseconds(next.Item1.Value) == 110000),
                "Cancellation intent must accept a raced completed or deferred terminal journal without inventing wait or retaining a settled lease.");
        }
    }

    private static void ClosingClockFailurePreservesReleasedWait()
    {
        var clock = new Clock();
        var f = new Fixture(new ControllerPhaseCapture(clock));
        f.Submit();
        clock.FailOnRead = clock.Reads + 2;
        f.Transport.Observation = Observation(3, 1, 110000, 100);
        var failure = Failure(() => f.Poll());
        Require(ReferenceEquals(failure, clock.Failure) && f.Transport.Releases == 1
            && !f.Controller.HasPending(1) && f.Controller.ConfirmedWait(1) == 0
            && f.Controller.ReleasedWait(1) == 110000
            && SignalAccountingModule.globalNanoseconds(f.State) - f.Controller.ReleasedWait(1) == 0
            && f.Controller.Journals.Single().Failure!.Contains(clock.Failure.Message),
            "A diagnostic failure after release must retain paid wait and failure evidence without restoring native ownership.");
        Require(!f.Poll().Released && f.Transport.Releases == 1 && f.Controller.ReleasedWait(1) == 110000,
            "Cleanup after a diagnostic failure must neither release again nor pay the wait twice.");
    }

    private static void CombinedOperationAndPhaseFailuresRemainVisible()
    {
        foreach (string mode in new[] { "submit", "read", "accounting" })
        {
            bool submission = mode == "submit";
            var clock = new Clock();
            var phases = new ControllerPhaseCapture(clock);
            var f = new Fixture(phases);
            if (!submission) f.Submit();
            clock.FailOnRead = clock.Reads + 2;
            f.Transport.FailSubmit = submission;
            f.Transport.FailRead = mode == "read";
            f.Transport.Observation = Observation(0, 0, 40000, 100);
            if (mode == "accounting")
            {
                var header = f.Transport.Observation.Raw.Header;
                header.Measured++;
                f.Transport.Observation = f.Transport.Observation with { Raw = f.Transport.Observation.Raw with { Header = header } };
            }
            var failure = Failure(() => { if (submission) f.Submit(); else f.Poll(); });
            string primary = mode switch
            {
                "submit" => "submit failure",
                "read" => "read failure",
                _ => "The timer aggregate differs from accepted child waits."
            };
            Require(failure is AggregateException aggregate && aggregate.InnerExceptions.Count == 2
                && aggregate.InnerExceptions[0].Message == primary && ReferenceEquals(aggregate.InnerExceptions[1], clock.Failure)
                && f.Controller.PendingJournals.Single().Failure is { } recorded
                && recorded.Contains(primary) && recorded.Contains(clock.Failure.Message)
                && f.Controller.HasPending(1) && f.Controller.ConfirmedWait(1) == 0 && f.Transport.Releases == 0,
                "Transport and closing-clock failures must both survive without releasing ownership.");
            int reads = clock.Reads;
            phases.End(false);
            Require(clock.Reads == reads, "A failed closing clock must not leave an active phase for cleanup to retry.");
            clock.FailOnRead = int.MaxValue;
            phases.Begin("recovery", "read-peer", 1);
            phases.End();
            Require(phases.Entries[^1].Completed && phases.Entries[^1].Stage == "recovery",
                "A failed closing clock must not poison a later diagnostic phase.");
        }
    }

    private static TimerObservation EmptyObservation(ulong captured, bool cancelled = false)
    {
        var header = new TimerNativeSnapshot { Generation = 1, Command = 1, Budget = 110000,
            Visibility = cancelled ? 3UL : 0UL, Stop = cancelled ? 3UL : 0UL,
            MayHaveDelivery = cancelled ? 0UL : 1UL, Captured = captured };
        return new(new(header, [], new(0, captured + 1)), []);
    }

    private static void UnavailableTargetRetainsGrowingDebt()
    {
        var f = new Fixture();
        f.Submit();
        var identity = Parsed(SignalAccountingModule.sampleIdentity(f.Selected, f.State));
        ulong sequence = 1;
        foreach (long expectedDebt in new[] { TimerDebtLimit.Nanoseconds - 1, TimerDebtLimit.Nanoseconds, TimerDebtLimit.Nanoseconds + 1 })
        {
            var sample = Parsed(CpuSampleObservationModule.tryCreate(identity, ++sequence, checked((ulong)expectedDebt * 2), 0,
                SampleThreadState.Runnable, SampleLocation.Selected));
            f.State = Parsed(SignalAccountingModule.observe(f.Selected, sample, f.State)).Item2;
            f.State = Parsed(SignalAccountingModule.prepareSelectedService(f.Selected, f.State)).Item2;
            f.Transport.Observation = EmptyObservation(sequence * 3_000_000);
            var result = f.Poll();
            long debt = SignalAccountingModule.globalNanoseconds(f.State) - f.Controller.ConfirmedWait(1);
            Require(debt == expectedDebt && !result.Released && result.SettledWait == 0
                && f.Controller.ConfirmedWait(1) == 0 && f.Controller.HasPending(1)
                && f.Controller.PendingJournals.Single().Budget == 110000,
                $"A target without a child receipt must retain {expectedDebt} ns debt and its original lease budget.");
            Require(TimerDebtLimit.Allows(0, debt) == (expectedDebt <= TimerDebtLimit.Nanoseconds)
                && TimerDebtLimit.Allows(debt, 0) == (expectedDebt <= TimerDebtLimit.Nanoseconds),
                $"The live debt gate must apply its inclusive 2 ms boundary to both slots at {expectedDebt} ns.");
        }
        f.Controller.Cancel(1, ref f.State);
        f.Transport.Observation = EmptyObservation(20_000_000, cancelled: true);
        var cancelled = f.Poll();
        Require(cancelled.Released && cancelled.SettledWait == 0 && f.Transport.Releases == 1
            && SignalAccountingModule.pendingCount(f.State) == 0
            && SignalAccountingModule.globalNanoseconds(f.State) == TimerDebtLimit.Nanoseconds + 1
            && SignalAccountingModule.prepareService(f.Peer, f.State).IsError,
            "Retired cancellation must release ownership without forgiving debt or permitting an oversized replacement lease.");
    }

    private static void DelayedDeliveryCreditsOnlyConfirmedWait()
    {
        var f = new Fixture();
        f.Submit();
        f.Transport.Observation = EmptyObservation(8_000_000);
        f.Poll();
        Require(f.Controller.ConfirmedWait(1) == 0, "Elapsed delivery time without a child must not earn wait credit.");
        f.Transport.Observation = Observation(0, 0, 40000, 100, started: 9_000_000);
        f.Poll();
        Require(f.Controller.ConfirmedWait(1) == 40000
            && SignalAccountingModule.globalNanoseconds(f.State) - f.Controller.ConfirmedWait(1) == 70000,
            "A long arm-to-handler interval must credit only the child's 40000 ns measured wait.");
        f.Controller.Cancel(1, ref f.State);
        f.Transport.Observation = Observation(3, 3, 40000, 100, started: 9_000_000);
        var result = f.Poll();
        Require(result.Released && result.SettledWait == 40000 && f.Debt() == 70000 && f.Transport.Releases == 1,
            "Cancellation after delayed delivery must settle measured wait once and retain the unpaid remainder.");
    }

    private static void PhaseCaptureKeepsServiceAfterIdlePolls()
    {
        var phases = new ControllerPhaseCapture(new Clock());
        for (int index = 0; index < 256; index++)
        {
            phases.Begin("check", "empty-drain", index);
            phases.End(retain: false);
        }
        Require(phases.Entries.Count == 0, "Discarded drains must not consume phase capacity.");
        var f = new Fixture(phases);
        f.Submit();
        f.Transport.Observation = Observation(0, 0, 40000, 100);
        f.Poll();
        for (int index = 0; index < 256; index++) f.Poll();
        Require(phases.Entries.Select(entry => entry.Phase).SequenceEqual(new[] { "submit-peer", "read-peer" }),
            "Unchanged polls must not consume phase capacity.");
        f.Controller.Cancel(1, ref f.State);
        f.Transport.Observation = Observation(3, 3, 40000, 100);
        f.Poll();
        Require(phases.Entries.Count == 3 && phases.Entries.All(entry => entry.Completed)
            && phases.Entries[^1].Phase == "read-peer" && f.Transport.Releases == 1,
            "A retired journal must retain its phase after repeated idle polls.");
        var failed = new Fixture(phases);
        failed.Transport.FailSubmit = true;
        Reject<IOException>(() => failed.Submit());
        Require(phases.Entries.Count == 4 && !phases.Entries[^1].Completed
            && phases.Entries[^1].Phase == "submit-peer" && failed.Controller.HasPending(1),
            "Failed submission must retain an incomplete phase and slot ownership.");
    }

    private static void PartialPrefixDoesNotPayTwice()
    {
        var f = new Fixture();
        f.Submit();
        f.Transport.Observation = Observation(0, 0, 40000, 100);
        f.Poll();
        var state = f.State;
        f.Poll();
        Require(ReferenceEquals(state, f.State) && f.Controller.ConfirmedWait(1) == 40000,
            "A repeated prefix must not pay twice.");
        f.Controller.Cancel(1, ref f.State);
        f.Transport.Observation = Observation(3, 3, 40000, 100);
        Require(f.Poll().Released && f.Debt() == 70000, "Cancellation must preserve the unpaid prefix debt.");
    }

    private static void EquivalentRetiredReceiptReleases()
    {
        var f = new Fixture();
        f.Submit();
        f.Transport.Observation = Observation(2, 2);
        Require(!f.Poll().Released && f.Controller.HasPending(1) && SignalAccountingModule.pendingCount(f.State) == 0,
            "Quiesced settlement must retain transport ownership.");
        f.Transport.Observation = Observation(3, 2);
        Require(f.Poll().Released && f.Transport.Releases == 1 && f.Debt() == 110000,
            "Equivalent retired receipts must release without paying deferred debt.");
    }

    private static void SelectedPrefixPreservesUnpaidDebt()
    {
        var f = new Fixture();
        f.Submit();
        f.Transport.Observation = Observation(3, 1, 115000, 100);
        Require(f.Poll().Released, "Peer overshoot must settle before selected recovery.");
        var published = Parsed(SignalAccountingModule.prepareService(f.Peer, f.State));
        Require(published.Item1 is null, "Peer overshoot must publish without another peer lease.");
        f.State = published.Item2;
        var selected = Parsed(SignalAccountingModule.prepareSelectedService(f.Selected, f.State));
        Require(selected.Item1 is not null && SignalServiceLeaseModule.nanoseconds(selected.Item1.Value) == 5000,
            "Peer overshoot must create 5000 ns of selected debt.");
        f.State = selected.Item2;
        f.Controller.Submit(0, selected.Item1!.Value, ref f.State, "selected");
        f.Transport.Observation = Observation(0, 0, 2000, 100, 5000, 2);
        f.Controller.Poll(0, ref f.State, "selected");
        var state = f.State;
        f.Controller.Poll(0, ref f.State, "selected");
        Require(ReferenceEquals(state, f.State) && f.Controller.ConfirmedWait(0) == 2000,
            "Selected prefix payment must remain visible and must not repeat.");
        f.Controller.Cancel(0, ref f.State);
        f.Transport.Observation = Observation(3, 3, 2000, 100, 5000, 2);
        Require(f.Controller.Poll(0, ref f.State, "selected").Released, "Selected cancellation must release the retired lease.");
        var remaining = Parsed(SignalAccountingModule.prepareSelectedService(f.Selected, f.State));
        Require(remaining.Item1 is not null && SignalServiceLeaseModule.nanoseconds(remaining.Item1.Value) == 3000,
            "Selected cancellation must preserve 3000 ns of unpaid debt.");
    }

    private static void ChangedHeaderCannotHideFailure()
    {
        foreach (int field in Enumerable.Range(0, 4))
        {
            var f = new Fixture();
            f.Submit();
            f.Transport.Observation = Observation(0, 0, 40000, 100);
            f.Poll();
            var state = f.State;
            var header = f.Transport.Observation.Raw.Header;
            if (field == 0) header.Error = 1;
            if (field == 1) header.TransportError = 22;
            if (field == 2) header.Generation++;
            if (field == 3) header.Measured++;
            f.Transport.Observation = f.Transport.Observation with { Raw = f.Transport.Observation.Raw with { Header = header } };
            Reject<InvalidDataException>(() => f.Poll());
            Require(ReferenceEquals(state, f.State) && f.Controller.HasPending(1) && f.Transport.Releases == 0,
                $"Changed header field {field} must fail without settlement or release.");
        }
    }

    private static void ReadFailureDoesNotReuseRawCapture()
    {
        var f = new Fixture();
        f.Submit();
        f.Transport.Observation = Observation(0, 0, 40000, 100);
        f.Poll();
        f.Transport.Observation = Observation(3, 3, 40000, 100);
        f.Transport.FailRead = true;
        Reject<IOException>(() => f.Poll());
        var journal = f.Controller.PendingJournals.Single();
        Require(journal.RawAttempt is null && journal.Attempted is null && journal.Accepted is not null,
            "A failed read must not label an earlier capture as the new attempt.");
    }

    private static void CancellationFailureRemainsVisible()
    {
        var f = new Fixture();
        f.Submit();
        f.Transport.FailCancel = true;
        Reject<IOException>(() => f.Controller.Cancel(1, ref f.State));
        Reject<InvalidDataException>(() => f.Controller.Cancel(1, ref f.State));
        Require(f.Transport.Cancellations == 1 && f.Controller.HasPending(1)
            && f.Controller.PendingJournals.Single().CancellationRequested,
            "Failed cancellation must retain intent and ownership without an implicit native retry.");
    }

    private static void ReleaseFailureRetainsOwnership()
    {
        var f = new Fixture();
        f.Submit();
        f.Transport.Observation = Observation(3, 2);
        f.Transport.FailRelease = true;
        Reject<IOException>(() => f.Poll());
        var state = f.State;
        Reject<InvalidDataException>(() => f.Poll());
        Reject<InvalidDataException>(() => f.Submit());
        Require(f.Controller.HasPending(1) && f.Transport.Releases == 1 && f.Controller.Journals.Count == 0
            && ReferenceEquals(state, f.State) && f.Controller.PendingJournals.Single().LedgerSettled,
            "Failed release must prevent reuse, repeat release, and repeat settlement.");
    }

    private static void SubmissionFailureRetainsSentLease()
    {
        var f = new Fixture();
        f.Transport.FailSubmit = true;
        Reject<IOException>(() => f.Submit());
        var journal = f.Controller.PendingJournals.Single();
        Require(f.Controller.HasPending(1) && journal.Generation == 1 && journal.SubmissionStatus == 22
            && SignalAccountingModule.pendingCount(f.State) == 1
            && SignalAccountingModule.sentService(f.Lease, f.State).IsError,
            "Submission failure must retain the sent lease and native submission evidence.");
    }

    private static void WrongSlotDoesNotSubmit()
    {
        var f = new Fixture();
        var state = f.State;
        Reject<InvalidDataException>(() => f.Controller.Submit(0, f.Lease, ref f.State, "check"));
        Require(ReferenceEquals(state, f.State) && f.Transport.LastSubmission is null && !f.Controller.HasPending(0),
            "A mismatched lease must fail before submission or accounting mutation.");
    }

    private static T Parsed<T>(FSharpResult<T, string> value) => value.IsOk ? value.ResultValue : throw new InvalidOperationException(value.ErrorValue);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
