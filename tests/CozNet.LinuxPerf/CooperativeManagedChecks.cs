using CozNet;
using Microsoft.FSharp.Core;

static class CooperativeManagedChecks
{
    internal sealed record Fixture(LedgerThread Selected, LedgerThread Peer, SignalServiceLease Lease, SignalAccounting State);
    internal static readonly CooperativeSite Site = CooperativeSite.Parse(1, 2, 3);
    internal static T Parsed<T>(FSharpResult<T, string> value) => value.IsOk ? value.ResultValue : throw new InvalidDataException(value.ErrorValue);
    internal static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new InvalidOperationException(message);
    }
    internal static SignalAccounting Credit(LedgerThread selected, ulong sequence, ulong amount, SignalAccounting state)
    {
        var identity = Parsed(SignalAccountingModule.sampleIdentity(selected, state));
        var sample = Parsed(CpuSampleObservationModule.tryCreate(identity, sequence, amount, 0, SampleThreadState.Runnable, SampleLocation.Selected));
        var observed = Parsed(SignalAccountingModule.observe(selected, sample, state));
        return Parsed(SignalAccountingModule.prepare(selected, observed.Item2)).Item2;
    }
    internal static Fixture Reserve(ulong amount = 220000)
    {
        var selected = Parsed(SignalAccountingModule.register(FSharpOption<int>.Some(50), SignalAccountingModule.create()));
        var peer = Parsed(SignalAccountingModule.register(FSharpOption<int>.None, selected.Item2));
        var state = Credit(selected.Item1, 1, amount, peer.Item2);
        var reserved = Parsed(SignalAccountingModule.prepareService(peer.Item1, state));
        var lease = reserved.Item1.Value;
        return new(selected.Item1, peer.Item1, lease, Parsed(SignalAccountingModule.sentService(lease, reserved.Item2)));
    }
    internal static long Remaining(Fixture fixture, SignalAccounting state)
    {
        var next = Parsed(SignalAccountingModule.prepareService(fixture.Peer, state)).Item1;
        return FSharpOption<SignalServiceLease>.get_IsSome(next) ? SignalServiceLeaseModule.nanoseconds(next.Value) : 0;
    }
    internal static CooperativeAccountingAdapter Adapter(Fixture fixture, ulong generation = 1, ulong command = 7) =>
        new(CooperativeAccountingBinding.Parse(fixture.Lease, generation, command, Site));
    private static CooperativeNativeChild[] Children(params ulong[] waits)
    {
        ulong measured = 0, finished = 1000;
        return waits.Select((wait, index) =>
        {
            var child = new CooperativeNativeChild { Ordinal = (ulong)index + 1, Requested = Math.Min(50000UL, 110000 - measured),
                Armed = finished - 1, Started = finished + 1000, Finished = finished + 1000 + Math.Max(wait, 1), Waited = wait,
                Worker = 1, CodeVersion = 2, Site = 3, Valid = 1 };
            measured += wait;
            finished = child.Finished;
            return child;
        }).ToArray();
    }
    private static CooperativeNativeSnapshot Header(ulong visibility, ulong stop, params CooperativeNativeChild[] children) => new()
    {
        Generation = 1, Command = 7, Budget = 110000, Children = (ulong)children.Length,
        Measured = (ulong)children.Sum(child => (long)child.Waited), Visibility = visibility, Stop = stop,
        MayHaveDelivery = visibility == 0 ? 1UL : 0,
        Captured = children.Length == 0 ? 1 : children.Max(child => child.Finished) + 1
    };
    private static CooperativeReceipts Tracker() => new(CooperativeLeaseIdentity.Parse(1, 7, 110000));
    private static CooperativeObservation Observe(CooperativeReceipts tracker, CooperativeNativeSnapshot header, params CooperativeNativeChild[] children) =>
        tracker.Observe(header, children, new(0, header.Captured + 1));

    public static void Run()
    {
        CpuDiagnosticsPreserveFailedReceipts();
        SiteIdentityAndLayouts();
        FirstReceiptsAndImmutablePrefixes();
        FailedReceiptsRemainFailed();
        PrefixCreditAndSettlement();
        DeferralCancellationAndFailure();
        InvalidPaymentKeepsReservation();
    }
    private static void CpuDiagnosticsPreserveFailedReceipts()
    {
        var children = Children(50000);
        var header = Header(3, 1, children);
        var cpu = new CooperativeCpuCapture(0, 1, null, 0, Array.Empty<CooperativeNativeCpuChild>(), "controlled CPU capture failure");
        var tracker = Tracker();
        Reject(() => tracker.Observe(header, children, new(2, 0), cpu: cpu), "CPU evidence repaired a failed receipt.");
        Require(tracker.FirstFailure is { } failed && failed.Clock.Status == 2 && ReferenceEquals(failed.Cpu, cpu),
            "The original receipt failure or its CPU diagnostic was replaced.");
        var accepted = Tracker().Observe(header, children, new(0, header.Captured + 1), cpu: cpu);
        Require(ReferenceEquals(accepted.Raw.Cpu, cpu) && accepted.Received.Count == 1,
            "A diagnostic failure replaced successful receipt evidence.");
    }

    private static void SiteIdentityAndLayouts()
    {
        NativeCooperativeClient.VerifyLayout();
        foreach (var site in new[] { (0UL, 2UL, 3UL), (1UL, 0UL, 3UL), (1UL, 2UL, 0UL) })
            Reject(() => CooperativeSite.Parse(site.Item1, site.Item2, site.Item3), "A zero site identity was accepted.");
        foreach (var identity in new[] { (0UL, 7UL, 1UL), ((ulong)uint.MaxValue + 1, 7UL, 1UL), (1UL, 0UL, 1UL), (1UL, 7UL, 0UL), (1UL, 7UL, 2000001UL) })
            Reject(() => CooperativeLeaseIdentity.Parse(identity.Item1, identity.Item2, identity.Item3), "An invalid lease identity was accepted.");
        var fixture = Reserve();
        var binding = CooperativeAccountingBinding.Parse(fixture.Lease, uint.MaxValue, 7, Site);
        Require(binding.Sequence(100000) == 0xffffffff000186a0UL, "The cooperative sequence truncated the maximum generation.");
        Reject(() => binding.Sequence(0), "Ordinal zero was accepted.");
        Reject(() => binding.Sequence(100001), "An excessive ordinal was accepted.");
        var child = Children(0)[0];
        Require(binding.Eligible(child) && !binding.Eligible(child with { Worker = 2 }) && !binding.Eligible(child with { CodeVersion = 3 })
            && !binding.Eligible(child with { Site = 4 }), "Cooperative eligibility did not compare all site fields.");
    }
    private static void FirstReceiptsAndImmutablePrefixes()
    {
        var children = Children(50000, 50000);
        var tracker = Tracker();
        var first = Observe(tracker, Header(0, 0, children[0]), children[0]);
        ulong firstUpper = first.Received[0].FirstManagedReceiptUpper;
        var grown = Observe(tracker, Header(0, 0, children), children);
        children[0] = children[0] with { Worker = 9 };
        Require(first.Received.Count == 1 && grown.Received.Count == 2 && grown.Received[0].FirstManagedReceiptUpper == firstUpper
            && first.Raw.Children[0].Worker == 1 && grown.Raw.Children[0].Worker == 1, "Caller changes mutated retained cooperative evidence.");
        var laterHeader = grown.Raw.Header with { Captured = 300000000 };
        var later = tracker.Observe(laterHeader, grown.Raw.Children.ToArray(), new(0, 300000001));
        Require(later.Received[0] == first.Received[0] && later.Received[1] == grown.Received[1], "A later read replaced the first receipt bounds.");
        var exact = Tracker().Observe(Header(0, 0, Children(50000)), Children(50000), new(0, 100000999));
        Require(exact.Received[0].Timing.UpperNanoseconds == 100000000, "The exact 100 ms receipt bound was rejected.");
    }
    private static void FailedReceiptsRemainFailed()
    {
        var child = Children(50000)[0];
        var header = Header(0, 0, child);
        foreach (var malformed in new[] { header with { Generation = 2 }, header with { Command = 8 }, header with { Budget = 110001 },
            header with { Children = 0 }, header with { Captured = 0 }, header with { Visibility = 4 }, header with { Stop = 6 },
            header with { Error = 7 }, header with { MayHaveDelivery = 2 }, header with { Stop = 1 }, header with { Error = 1 },
            header with { TransportError = 1 }, header with { MayHaveDelivery = 0 }, header with { Visibility = 3 },
            header with { Visibility = 3, Stop = 3 } })
        {
            var tracker = Tracker();
            Reject(() => Observe(tracker, malformed, child), "An invalid cooperative header was accepted.");
            Require(tracker.FirstFailure?.Header == malformed, "The failed cooperative header was not retained.");
        }
        foreach (var mutation in new[] { child with { Worker = 0 }, child with { CodeVersion = 0 }, child with { Site = 0 },
            child with { Ordinal = 2 }, child with { Valid = 2 }, child with { WaitStatus = 3 }, child with { Finished = child.Armed - 1 } })
        {
            var tracker = Tracker();
            Reject(() => Observe(tracker, header, mutation), "Malformed cooperative evidence was accepted.");
            var failure = tracker.FirstFailure;
            Reject(() => Observe(tracker, header, child), "A valid read repaired rejected cooperative evidence.");
            Require(ReferenceEquals(failure, tracker.FirstFailure) && failure!.Children[0] == mutation, "The original failed raw child was lost.");
        }
        foreach (var test in new[] { (new CooperativeClockRead(0, 100001000), 0), (new CooperativeClockRead(2, 0), 0),
            (new CooperativeClockRead(0, header.Captured - 1), 0), (new CooperativeClockRead(0, header.Captured + 1), 4) })
        {
            var tracker = Tracker();
            Reject(() => tracker.Observe(header, new[] { child }, test.Item1, test.Item2), "A failed or late receipt was accepted.");
            var failure = tracker.FirstFailure;
            Reject(() => Observe(tracker, header, child), "A later clock repaired the failed receipt.");
            Require(ReferenceEquals(failure, tracker.FirstFailure) && failure!.Clock == test.Item1 && failure.Status == test.Item2,
                "Receipt failure evidence changed.");
        }
        var prefix = Tracker();
        Observe(prefix, header, child);
        Reject(() => Observe(prefix, header with { Captured = header.Captured + 1 }, child with { Site = 4 }), "An accepted site identity changed.");
        var terminal = Tracker();
        var end = header with { Visibility = 3, Stop = 3, MayHaveDelivery = 0 };
        Observe(terminal, end, child);
        Reject(() => Observe(terminal, end with { Measured = end.Measured + 1 }, child), "A terminal payment changed.");
        var backwards = Tracker();
        Observe(backwards, header, child);
        Reject(() => Observe(backwards, header with { Captured = header.Captured - 1 }, child), "Capture time moved backwards.");
        var upper = Tracker();
        upper.Observe(header, new[] { child }, new(0, header.Captured + 100));
        Reject(() => Observe(upper, header with { Captured = header.Captured + 1 }, child), "Receipt upper time moved backwards.");
        var shrink = Tracker();
        Observe(shrink, header, child);
        Reject(() => Observe(shrink, header with { Children = 0 }), "A cooperative prefix shrank.");
    }
    private static void PrefixCreditAndSettlement()
    {
        var fixture = Reserve();
        var adapter = Adapter(fixture);
        var tracker = Tracker();
        var children = Children(50000, 50000, 10000);
        var first = Observe(tracker, Header(0, 0, children[0]), children[0]);
        var accepted = adapter.Accept(first, fixture.State);
        Require(accepted.Adapter.ConfirmedNanoseconds == 50000 && !accepted.Adapter.IsTerminal
            && SignalAccountingModule.pendingCount(accepted.State) == 1, "An active prefix settled the cooperative lease.");
        var concurrent = Credit(fixture.Selected, 2, 260000, accepted.State);
        var repeated = accepted.Adapter.Accept(first, concurrent);
        Require(repeated.Adapter.ConfirmedNanoseconds == 50000, "A repeated prefix paid debt twice.");
        Reject(() => repeated.Adapter.Accept(first, fixture.State), "A stale ledger was accepted after payment.");
        var final = Observe(tracker, Header(3, 1, children), children);
        var settled = repeated.Adapter.Accept(final, repeated.State);
        Require(settled.Adapter.IsTerminal && settled.Adapter.ConfirmedNanoseconds == 110000
            && SignalAccountingModule.pendingCount(settled.State) == 0 && Remaining(fixture, settled.State) == 20000,
            "Cooperative settlement lost concurrent credit or paid it twice.");
        Reject(() => settled.Adapter.Accept(final, settled.State), "A terminal cooperative lease settled twice.");
    }
    private static void DeferralCancellationAndFailure()
    {
        foreach (var deferred in new[] { Children(0)[0] with { Worker = 9 }, Children(0)[0] with { CodeVersion = 9 }, Children(0)[0] with { Site = 9 } })
        {
            var fixture = Reserve();
            var settled = Adapter(fixture).Accept(Observe(Tracker(), Header(3, 2, deferred), deferred), fixture.State);
            Require(settled.Adapter.IsTerminal && settled.Adapter.ConfirmedNanoseconds == 0 && Remaining(fixture, settled.State) == 110000,
                "A deferred cooperative site paid debt.");
        }
        var cancelledFixture = Reserve();
        var adapter = Adapter(cancelledFixture);
        var child = Children(0)[0];
        var cancelled = Observe(Tracker(), Header(3, 3, child), child);
        Reject(() => adapter.Accept(cancelled, cancelledFixture.State), "Cancellation settled without managed intent.");
        var requested = adapter.RequestCancellation(cancelledFixture.State);
        var stopped = requested.Adapter.Accept(cancelled, requested.State);
        Require(stopped.Adapter.IsTerminal && Remaining(cancelledFixture, stopped.State) == 110000, "Cancellation paid unperformed wait.");
        var interruptedFixture = Reserve();
        var interrupted = Children(10000)[0] with { WaitStatus = 1 };
        var interruption = Adapter(interruptedFixture).Accept(Observe(Tracker(), Header(3, 4, interrupted), interrupted), interruptedFixture.State);
        Require(interruption.Adapter.IsTerminal && Remaining(interruptedFixture, interruption.State) == 100000, "Interrupted wait accounting differs.");
    }
    private static void InvalidPaymentKeepsReservation()
    {
        var fixture = Reserve();
        var good = Children(50000)[0];
        foreach (var child in new[] { good with { Worker = 9 }, good with { CodeVersion = 9 }, good with { Site = 9 },
            good with { Valid = 0 }, good with { WaitStatus = 2 }, good with { Requested = 0 }, good with { Requested = 50001 },
            good with { Started = good.Armed - 1 }, good with { Finished = good.Started + 5000001 }, good with { Waited = 50001 } })
        {
            var observation = Observe(Tracker(), Header(0, 0, child), child);
            Reject(() => Adapter(fixture).Accept(observation, fixture.State), "Invalid cooperative payment was accepted.");
            Require(SignalAccountingModule.pendingCount(fixture.State) == 1
                && Parsed(SignalAccountingModule.confirmedServiceNanoseconds(fixture.Lease, fixture.State)) == 0,
                "Rejected payment changed its ledger reservation.");
        }
        var cancelled = Adapter(fixture).RequestCancellation(fixture.State);
        Reject(() => cancelled.Adapter.Accept(Observe(Tracker(), Header(3, 3)), cancelled.State), "A zero-child cooperative terminal was accepted.");
        var failure = Observe(Tracker(), Header(3, 5, good) with { Error = 3 }, good);
        Reject(() => Adapter(fixture).Accept(failure, fixture.State), "A failed cooperative journal settled.");
    }
}
