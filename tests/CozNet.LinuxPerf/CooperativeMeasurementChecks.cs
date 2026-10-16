using CozNet;
using Microsoft.FSharp.Core;
using static CooperativeManagedChecks;

static class CooperativeMeasurementChecks
{
    private sealed class Clock(params long[] stamps) : TimeProvider
    {
        public int Reads;
        public override long TimestampFrequency => 1000000000;
        public override long GetTimestamp() => stamps[Reads++];
    }

    private static (LedgerThread Selected, LedgerThread Peer, SignalAccounting State) Fresh()
    {
        var selected = Parsed(SignalAccountingModule.register(FSharpOption<int>.Some(50), SignalAccountingModule.create()));
        var peer = Parsed(SignalAccountingModule.register(FSharpOption<int>.None, selected.Item2));
        return (selected.Item1, peer.Item1, peer.Item2);
    }

    private static Exception? Error(Action action)
    {
        try { action(); return null; } catch (Exception error) { return error; }
    }

    public static void Run()
    {
        ActivationRequiresOwnerCadence();
        PreparationFailureRemainsACaptureFailure();
        FrozenEstimateUsesGlobalDelayAndSurvivesCleanup();
        AcceptedPrefixAndSettledOwnershipRemainDistinct();
        MismatchedOwnershipAndCapabilitiesReject();
        FailedFreezeCannotRecoverWithCleanupState();
        LaterFailureInvalidatesFrozenEstimate();
        MissingSourceAndNonpositiveDurationReject();
        PendingNativeSuffixRemainsUnqualifiedAfterCleanup();
        OwnershipIdentityRejectsMalformedAndRetainsUnsubmitted();
    }

    private static void PreparationFailureRemainsACaptureFailure()
    {
        foreach (int failRead in new[] { 1, 2 })
        {
            var clock = new Clock(10, 20, 30, 40);
            var measurement = new FiniteCooperativeMeasurement(clock);
            var expected = new IOException("Controlled finite preparation failure.");
            int reads = 0;
            MeasurementWindowCapture.Readings Read() => ++reads == failRead ? throw expected
                : new(0, 0, 0, 1, new(0, 0, 0, null), new(0, 0, 0, null));
            Require(ReferenceEquals(Error(() => measurement.Prepare(Read)), expected)
                && measurement.CaptureFailure?.Contains(expected.Message, StringComparison.Ordinal) == true
                && measurement.Window.Status == MeasurementCaptureStatus.NotStarted && measurement.Window.Start is null,
                "Failed finite preparation must retain the capture failure and leave the real window unopened.");
            string? first = measurement.CaptureFailure;
            int before = reads, beforeClock = clock.Reads;
            Require(Error(() => measurement.Begin(Read)) is InvalidOperationException
                && measurement.CaptureFailure == first && reads == before && clock.Reads == beforeClock,
                "A later start must not replace the preparation failure or invoke the reader.");
            measurement.Freeze(Read);
            Require(measurement.AcceptanceClosed && Error(() => measurement.Prepare(Read)) is InvalidOperationException
                && measurement.CaptureFailure == first && reads == before && clock.Reads == beforeClock
                && measurement.Assess(false, false, true, 0).Status == "invalid",
                "Closed admission must prevent preparation and retain an invalid measurement.");
        }
    }

    private static void ActivationRequiresOwnerCadence()
    {
        Require(FiniteCooperativeMeasurement.Parse(null, false, false, TimeProvider.System) is null
            && FiniteCooperativeMeasurement.Parse("0", false, false, TimeProvider.System) is null
            && FiniteCooperativeMeasurement.Parse("1", true, true, TimeProvider.System) is not null,
            "Finite measurement activation changed its default or admitted mode.");
        foreach (var args in new[] { ("true", true, true), ("1", false, true), ("1", true, false), (" 1", true, true) })
            Require(Error(() => FiniteCooperativeMeasurement.Parse(args.Item1, args.Item2, args.Item3, TimeProvider.System))
                is ArgumentException, "Malformed or unsupported finite measurement activation passed.");
    }

    private static void FrozenEstimateUsesGlobalDelayAndSurvivesCleanup()
    {
        var fixture = Fresh();
        var state = fixture.State;
        MeasurementWindowCapture.Readings Read(long progress, ulong samples) => CooperativeMeasurementSnapshot.Capture(
            CooperativeDebtSnapshot.Capture(fixture.Selected, fixture.Peer, state), progress, progress + 2,
            0, 0, 0, null, null, progress == 0 ? 0 : 50000, samples, 1);
        var clock = new Clock(100, 110, 1000100, 1000110);
        var measurement = new FiniteCooperativeMeasurement(clock);
        measurement.Begin(() => Read(0, 0));
        state = Credit(fixture.Selected, 1, 100000, state);
        measurement.Freeze(() => Read(10, 2));
        var frozen = measurement.Window.End;
        state = Credit(fixture.Selected, 2, 200000, state);
        measurement.Freeze(() => throw new InvalidOperationException("Cleanup cannot replace the endpoint."));
        var result = measurement.Assess(true, true, true, 3);
        Require(measurement.AcceptanceClosed && ReferenceEquals(frozen, measurement.Window.End) && clock.Reads == 4
            && result.Status == "unqualified" && result.DescriptiveEstimate == new FiniteCooperativeMeasurement.Estimate(
                999990, 1000010, 50000, 949990, 950010, 10, 10, 2)
            && result.Reasons.Contains("unqualified-ledger-residual") && result.Reasons.Contains("source-tail-outside-frozen-prefix")
            && frozen!.Accounting.Peer.ReleasedWait == 0,
            "The frozen estimate used worker waits, lost residuals, or changed after cleanup.");
    }

    private static void AcceptedPrefixAndSettledOwnershipRemainDistinct()
    {
        var fixture = Reserve(100000);
        var child = Parsed(SignalServiceChildModule.tryCreate(SignalServiceLeaseModule.identity(fixture.Lease),
            1, 7, 50000, 60000, 60000, 60000, 2, true));
        var accepted = Parsed(SignalAccountingModule.acceptServiceChild(fixture.Lease, child, fixture.State));
        var owned = CooperativeMeasurementSnapshot.Ownership.Parse(fixture.Lease, 3, 2, 1, 60000, false);
        MeasurementWindowCapture.Readings Read(SignalAccounting state, CooperativeMeasurementSnapshot.Ownership? ownership)
            => CooperativeMeasurementSnapshot.Capture(CooperativeDebtSnapshot.Capture(fixture.Selected, fixture.Peer, state),
                1, 2, 0, ownership is null ? 60000 : 0, ownership is null ? 1 : 0, null, ownership, 50000, 2, 1);
        var before = Read(accepted, owned);
        var settled = Parsed(SignalAccountingModule.completeService(fixture.Lease, accepted)).Item2;
        var after = Read(settled, CooperativeMeasurementSnapshot.Ownership.Parse(fixture.Lease, 3, 2, 1, 60000, true));
        var released = Read(settled, null);
        Require(before.Peer.Ledger!.UnsettledWait == 60000 && before.Peer.Ledger.Local == 0
            && before.Peer.Ledger.LastSequence == 7 && before.Global == 50000
            && after.Peer.Ledger!.UnsettledWait == 0 && after.Peer.Ledger.Local == 60000
            && after.Cooperative!.Peer is { LedgerSettled: true, ConfirmedWait: 60000 }
            && after.Peer.ActiveWait == 60000 && released.Cooperative!.Peer is null && released.Peer.ActiveWait == 0,
            "Accepted wait, settled local credit, and unreleased native ownership were conflated.");
        Require(Error(() => Read(accepted, null)) is InvalidDataException
            && Error(() => Read(accepted, CooperativeMeasurementSnapshot.Ownership.Parse(fixture.Lease, 3, 2, 0, 0, false)))
                is InvalidDataException && Error(() => Read(settled, owned)) is InvalidDataException,
            "A missing or mismatched cooperative accepted prefix passed capture.");
    }

    private static void MismatchedOwnershipAndCapabilitiesReject()
    {
        var fixture = Fresh();
        var reading = CooperativeMeasurementSnapshot.Capture(CooperativeDebtSnapshot.Capture(fixture.Selected, fixture.Peer, fixture.State),
            0, 0, 0, 0, 0, null, null, 0, 0, 1);
        foreach (var invalid in new[] { reading with { Global = 1 }, reading with { Peer = reading.Selected },
            reading with { Selected = reading.Selected with { Progress = 1 } }, reading with { Cooperative = null } })
        {
            var capture = new MeasurementWindowCapture(new Clock(100, 110));
            Require(Error(() => capture.Begin(() => invalid, () => { })) is InvalidDataException && capture.Start is null,
                "A frozen cooperative worker was detached from its ownership evidence.");
        }
        var window = new MeasurementWindowCapture(new Clock(100, 110, 200, 210));
        window.Begin(() => reading, () => { });
        Require(Error(() => window.Finish(() => new(0, 0, 0, 1, new(0, 0, 0, null), new(0, 0, 0, null))))
            is InvalidDataException && window.End is null, "Cooperative capability disappeared at the end boundary.");
    }

    private static void FailedFreezeCannotRecoverWithCleanupState()
    {
        var fixture = Fresh();
        var reading = CooperativeMeasurementSnapshot.Capture(CooperativeDebtSnapshot.Capture(fixture.Selected, fixture.Peer, fixture.State),
            0, 0, 0, 0, 0, null, null, 0, 0, 1);
        var measurement = new FiniteCooperativeMeasurement(new Clock(100, 110, 200));
        measurement.Begin(() => reading);
        var failure = new IOException("Injected endpoint failure.");
        Require(ReferenceEquals(Error(() => measurement.Freeze(() => throw failure)), failure), "Endpoint failure identity changed.");
        measurement.Freeze(() => reading);
        Require(measurement.AcceptanceClosed && measurement.Window.Status == MeasurementCaptureStatus.FinishFailed
            && measurement.Window.End is null && measurement.CaptureFailure!.Contains(failure.Message)
            && measurement.Assess(true, true, true, 0).Status == "invalid",
            "Cleanup recovered a failed endpoint or qualified its missing estimate.");
    }

    private static void LaterFailureInvalidatesFrozenEstimate()
    {
        var measurement = CompleteFresh();
        var frozen = measurement.Window.End;
        measurement.Fail(new IOException("Original source failure."));
        measurement.Fail(new IOException("Later cleanup failure."));
        var result = measurement.Assess(true, true, true, 2);
        Require(result.Status == "invalid" && result.DescriptiveEstimate is not null
            && ReferenceEquals(frozen, measurement.Window.End) && measurement.Failure!.Contains("Original source failure.")
            && !measurement.Failure.Contains("Later cleanup failure."), "A later failure replaced the first failure or erased the frozen record.");
    }

    private static FiniteCooperativeMeasurement CompleteFresh(long end = 1000)
    {
        var fixture = Fresh();
        var ledger = CooperativeDebtSnapshot.Capture(fixture.Selected, fixture.Peer, fixture.State);
        MeasurementWindowCapture.Readings Read(long progress, ulong samples) => CooperativeMeasurementSnapshot.Capture(
            ledger, progress, progress, 0, 0, 0, null, null, 0, samples, 1);
        var measurement = new FiniteCooperativeMeasurement(new Clock(100, 110, end, end + 10));
        measurement.Begin(() => Read(0, 0));
        measurement.Freeze(() => Read(1, 2));
        return measurement;
    }

    private static void MissingSourceAndNonpositiveDurationReject()
    {
        var measurement = CompleteFresh();
        foreach (var flags in new[] { (false, true, true, 2L), (true, false, true, 2L), (true, true, false, 2L), (true, true, true, 1L) })
            Require(measurement.Assess(flags.Item1, flags.Item2, flags.Item3, flags.Item4).Status == "invalid",
                "Incomplete workload, source, cleanup, or source prefix qualified an experiment.");
        Require(CompleteFresh(110).Assess(true, true, true, 2).Status == "invalid",
            "A zero lower effective duration qualified an experiment.");
    }

    private static void PendingNativeSuffixRemainsUnqualifiedAfterCleanup()
    {
        var fixture = Fresh();
        var measurement = new FiniteCooperativeMeasurement(new Clock(100, 110, 1000000, 1000010));
        measurement.Begin(() => CooperativeMeasurementSnapshot.Capture(
            CooperativeDebtSnapshot.Capture(fixture.Selected, fixture.Peer, fixture.State), 0, 0, 0, 0, 0, null, null, 0, 0, 1));
        var credited = Credit(fixture.Selected, 1, 100000, fixture.State);
        var reserved = Parsed(SignalAccountingModule.prepareService(fixture.Peer, credited));
        var lease = reserved.Item1.Value;
        var sent = Parsed(SignalAccountingModule.sentService(lease, reserved.Item2));
        measurement.Freeze(() => CooperativeMeasurementSnapshot.Capture(
            CooperativeDebtSnapshot.Capture(fixture.Selected, fixture.Peer, sent), 3, 4, 0, 0, 0, null,
            CooperativeMeasurementSnapshot.Ownership.Parse(lease, 1, 1, 0, 0, false), 50000, 2, 1));
        var child = Parsed(SignalServiceChildModule.tryCreate(SignalServiceLeaseModule.identity(lease),
            1, 1, 50000, 50000, 50000, 50000, 2, true));
        var accepted = Parsed(SignalAccountingModule.acceptServiceChild(lease, child, sent));
        var cleanup = Parsed(SignalAccountingModule.completeService(lease, accepted)).Item2;
        Require(SignalAccountingModule.pendingCount(cleanup) == 0, "Controlled cleanup did not settle its lease.");
        var result = measurement.Assess(true, true, true, 2);
        Require(result.Status == "unqualified" && result.Reasons.Contains("native-ownership-at-boundary")
            && measurement.Window.End!.Accounting.Peer.Ledger!.Local == 0
            && measurement.Window.End.Accounting.Cooperative!.Peer is { LedgerSettled: false, ConfirmedWait: 0 },
            "Cleanup qualified an unknown native suffix or replaced its frozen local credit.");
    }

    private static void OwnershipIdentityRejectsMalformedAndRetainsUnsubmitted()
    {
        var fixture = Reserve();
        foreach (var input in new[] { (0UL, 1UL, 0L, 0L, false), (1UL, (ulong)uint.MaxValue + 1, 0L, 0L, false),
            (1UL, 1UL, -1L, 0L, false), (1UL, 1UL, 0L, -1L, false), (1UL, 0UL, 1L, 1L, false), (1UL, 0UL, 0L, 0L, true) })
            Require(Error(() => CooperativeMeasurementSnapshot.Ownership.Parse(fixture.Lease, input.Item1, input.Item2,
                input.Item3, input.Item4, input.Item5)) is InvalidDataException, "Invalid native ownership passed parsing.");
        var unsubmitted = CooperativeMeasurementSnapshot.Ownership.Parse(fixture.Lease, 1, 0, 0, 0, false);
        var capture = CooperativeMeasurementSnapshot.Capture(
            CooperativeDebtSnapshot.Capture(fixture.Selected, fixture.Peer, fixture.State), 1, 1, 0, 0, 0,
            null, unsubmitted, 110000, 2, 1);
        Require(capture.Cooperative!.Peer!.Generation == 0 && capture.Peer.Ledger!.Phase == "delaying",
            "Unresolved submission identity was fabricated or dropped.");
    }
}
