using CozNet;
using Microsoft.FSharp.Core;

static class TimerAccountingAdapterChecks
{
    private sealed record Fixture(LedgerThread Selected, LedgerThread Peer, SignalServiceLease Lease, SignalAccounting State);
    private static T Parsed<T>(FSharpResult<T, string> value) => value.IsOk ? value.ResultValue : throw new InvalidOperationException(value.ErrorValue);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException(message);
    }
    private static SignalAccounting Credit(LedgerThread selected, ulong sequence, ulong cpu, SignalAccounting state)
    {
        var identity = Parsed(SignalAccountingModule.sampleIdentity(selected, state));
        var sample = Parsed(CpuSampleObservationModule.tryCreate(identity, sequence, cpu, 0, SampleThreadState.Runnable, SampleLocation.Selected));
        state = Parsed(SignalAccountingModule.observe(selected, sample, state)).Item2;
        var prepared = Parsed(SignalAccountingModule.prepare(selected, state));
        Require(FSharpOption<SignalDelayRequest>.get_IsNone(prepared.Item1), "Selected credit must not request a physical wait.");
        return prepared.Item2;
    }
    private static Fixture Start()
    {
        var selected = Parsed(SignalAccountingModule.register(FSharpOption<int>.Some(50), SignalAccountingModule.create()));
        var peer = Parsed(SignalAccountingModule.register(FSharpOption<int>.None, selected.Item2));
        var state = Credit(selected.Item1, 1, 220000, peer.Item2);
        var reserved = Parsed(SignalAccountingModule.prepareService(peer.Item1, state));
        var lease = reserved.Item1.Value;
        return new(selected.Item1, peer.Item1, lease, Parsed(SignalAccountingModule.sentService(lease, reserved.Item2)));
    }
    private static TimerAccountingAdapter Adapter(Fixture fixture, ulong generation = 1)
        => new(TimerAccountingBinding.Parse(fixture.Lease, generation, 7, 100, 20));
    private static long Remaining(Fixture fixture, SignalAccounting state)
        => SignalServiceLeaseModule.nanoseconds(Parsed(SignalAccountingModule.prepareService(fixture.Peer, state)).Item1.Value);

    private static TargetLeaseReceivedChild[] Children(params (ulong Wait, int Status, ulong Ip)[] values)
    {
        var children = new List<TargetLeaseReceivedChild>();
        ulong measured = 0, finished = 1000;
        foreach (var value in values)
        {
            ulong started = finished + 1000;
            var child = new TargetLeaseRawChild((ulong)children.Count + 1, Math.Min(50000UL, 110000 - measured),
                finished - 1, started, started + Math.Max(value.Wait, 1), value.Wait, value.Ip, value.Status, true);
            finished = child.Finished;
            children.Add(new(child, finished + 1, finished + 2, ServiceReceiptTiming.Parse(child.Armed, finished, finished + 1, finished + 2, 0)));
            measured += value.Wait;
        }
        return children.ToArray();
    }
    private static TimerObservation Observation(ulong visibility, ulong stop, params TargetLeaseReceivedChild[] children)
    {
        var raw = children.Select(child => new TimerNativeChild
        {
            Ordinal = child.Raw.Ordinal, Requested = child.Raw.Requested, Armed = child.Raw.Armed, Started = child.Raw.Started,
            Finished = child.Raw.Finished, Waited = child.Raw.Waited, Ip = child.Raw.InstructionPointer,
            WaitStatus = (ulong)child.Raw.WaitStatus, Valid = child.Raw.Valid ? 1UL : 0
        }).ToArray();
        var header = new TimerNativeSnapshot
        {
            Generation = 1, Command = 7, Budget = 110000, Children = (ulong)children.Length,
            Measured = (ulong)children.Sum(child => (long)child.Raw.Waited), Visibility = visibility, Stop = stop,
            MayHaveDelivery = visibility == 0 ? 1UL : 0, Captured = children.Length == 0 ? 1UL : children.Max(child => child.FirstCaptured) + 1
        };
        return new(new(header, raw, new(0, header.Captured + 1)), children);
    }
    private static TimerObservation Header(TimerObservation observation, Func<TimerNativeSnapshot, TimerNativeSnapshot> change)
        => observation with { Raw = observation.Raw with { Header = change(observation.Raw.Header) } };

    public static void Run()
    {
        BindingBounds();
        PrefixAndConcurrentCreditSettleOnce();
        ZeroWaitPrefixRejectsStaleState();
        CancellationAndGenerationReuse();
        DeferredAndInterruptedPreserveDebt();
        ReceiptAndHandlerBoundaries();
        InvalidEvidenceKeepsReservation();
    }

    private static void BindingBounds()
    {
        var fixture = Start();
        foreach (var value in new[] { (0UL, 7UL, 100UL, 20UL), ((ulong)uint.MaxValue + 1, 7UL, 100UL, 20UL),
            (1UL, 0UL, 100UL, 20UL), (1UL, 7UL, 0UL, 20UL), (1UL, 7UL, 100UL, 0UL), (1UL, 7UL, ulong.MaxValue, 1UL) })
            Reject(() => TimerAccountingBinding.Parse(fixture.Lease, value.Item1, value.Item2, value.Item3, value.Item4), "An invalid timer binding was accepted.");
        var binding = TimerAccountingBinding.Parse(fixture.Lease, uint.MaxValue, 7, 100, ulong.MaxValue - 100);
        Require(binding.Sequence(100000) == 0xffffffff000186a0UL && binding.Eligible(100) && binding.Eligible(ulong.MaxValue - 1)
            && !binding.Eligible(99) && !binding.Eligible(ulong.MaxValue), "The maximum generation or instruction-range boundary differs.");
        Reject(() => binding.Sequence(0), "Sequence zero must fail.");
        Reject(() => binding.Sequence(100001), "A sequence beyond journal capacity must fail.");
    }

    private static void PrefixAndConcurrentCreditSettleOnce()
    {
        var fixture = Start();
        var children = Children((40000, 0, 100), (50000, 0, 119), (25000, 0, 100));
        var partial = Adapter(fixture).Accept(Observation(0, 0, children[0]), fixture.State);
        var repeated = partial.Adapter.Accept(Observation(0, 0, children[0]), partial.State);
        Require(ReferenceEquals(repeated.State, partial.State) && repeated.Adapter.AcceptedCount == 1
            && repeated.Adapter.ConfirmedNanoseconds == 40000, "A repeated prefix applied its wait twice.");
        Reject(() => partial.Adapter.Accept(Observation(0, 0, children[0]), fixture.State), "A stale accounting state replaced a published prefix.");
        var state = Credit(fixture.Selected, 2, 300000, repeated.State);
        var terminalObservation = Observation(1, 1, children);
        var terminal = repeated.Adapter.Accept(terminalObservation, state);
        Require(!terminal.Adapter.IsTerminal && terminal.Adapter.ConfirmedNanoseconds == 115000
            && SignalAccountingModule.pendingCount(terminal.State) == 1, "Terminal visibility released ownership before quiescence.");
        var retired = Header(terminalObservation, h => { h.Visibility = 3; return h; });
        var settled = terminal.Adapter.Accept(retired, terminal.State);
        Require(settled.Adapter.IsTerminal && settled.Adapter.AcceptedCount == 3 && SignalAccountingModule.pendingCount(settled.State) == 0
            && Remaining(fixture, settled.State) == 35000, "Settlement lost overshoot or new selected credit.");
        Reject(() => settled.Adapter.Accept(retired, settled.State), "A settled timer journal accepted replay.");
        var changed = children[0] with
        {
            FirstManagedReceiptUpper = children[0].FirstManagedReceiptUpper + 1,
            Timing = ServiceReceiptTiming.Parse(children[0].Raw.Armed, children[0].Raw.Finished,
                children[0].FirstCaptured, children[0].FirstManagedReceiptUpper + 1, 0)
        };
        Reject(() => partial.Adapter.Accept(Observation(0, 0, changed), partial.State), "A changed first receipt was accepted.");
        var mutated = Header(retired, h => { h.Stop = 3; return h; });
        var cancelling = terminal.Adapter.RequestCancellation(terminal.State);
        Reject(() => cancelling.Adapter.Accept(mutated, cancelling.State),
            "A sealed terminal journal changed its stop reason.");
    }

    private static void ZeroWaitPrefixRejectsStaleState()
    {
        var fixture = Start();
        var observation = Observation(0, 0, Children((0, 0, 100)));
        var partial = Adapter(fixture).Accept(observation, fixture.State);
        Require(partial.Adapter.AcceptedCount == 1 && partial.Adapter.ConfirmedNanoseconds == 0, "A zero-wait child did not advance the cursor.");
        Reject(() => partial.Adapter.Accept(observation, fixture.State), "A zero-wait prefix accepted a stale ledger state.");
    }

    private static void CancellationAndGenerationReuse()
    {
        var fixture = Start();
        var empty = Observation(3, 3);
        Reject(() => Adapter(fixture).Accept(empty, fixture.State), "Unrequested cancellation released the lease.");
        var cancelling = Adapter(fixture).RequestCancellation(fixture.State);
        var stopped = cancelling.Adapter.Accept(empty, cancelling.State);
        Require(stopped.Adapter.IsTerminal && Remaining(fixture, stopped.State) == 110000, "Empty cancellation paid debt.");
        var child = Children((40000, 0, 100));
        cancelling = Adapter(fixture).RequestCancellation(fixture.State);
        stopped = cancelling.Adapter.Accept(Observation(2, 3, child), cancelling.State);
        Require(stopped.Adapter.IsTerminal && Remaining(fixture, stopped.State) == 70000, "Partial cancellation lost measured wait.");

        var deferred = Adapter(fixture).Accept(Observation(3, 2, Children((0, 0, 120))), fixture.State);
        var reserved = Parsed(SignalAccountingModule.prepareService(fixture.Peer, deferred.State));
        var lease = reserved.Item1.Value;
        var nextFixture = fixture with { Lease = lease, State = Parsed(SignalAccountingModule.sentService(lease, reserved.Item2)) };
        var next = Header(Observation(3, 2, Children((0, 0, 99))), h => { h.Generation = 2; return h; });
        var second = Adapter(nextFixture, 2).Accept(next, nextFixture.State);
        Require(second.Adapter.IsTerminal && Remaining(nextFixture, second.State) == 110000, "A new generation could not settle ordinal one.");
        var old = Header(next, h => { h.Generation = 1; return h; });
        Reject(() => Adapter(nextFixture).Accept(old, nextFixture.State), "An old generation reused an accepted sequence.");
    }

    private static void DeferredAndInterruptedPreserveDebt()
    {
        var fixture = Start();
        var deferred = Adapter(fixture).Accept(Observation(3, 2, Children((0, 0, 120))), fixture.State);
        Require(deferred.Adapter.IsTerminal && Remaining(fixture, deferred.State) == 110000, "A deferred child paid debt.");
        var interrupted = Adapter(fixture).Accept(Observation(3, 4, Children((12345, 1, 100))), fixture.State);
        Require(interrupted.Adapter.IsTerminal && Remaining(fixture, interrupted.State) == 97655, "An interrupted child lost partial wait.");
    }

    private static void InvalidEvidenceKeepsReservation()
    {
        var fixture = Start();
        var adapter = Adapter(fixture);
        var children = Children((40000, 0, 100), (40000, 0, 100));
        var baseline = Observation(0, 0, children);
        var mutations = new (string Name, Func<TimerNativeSnapshot, TimerNativeSnapshot> Change)[]
        {
            ("generation", h => { h.Generation++; return h; }), ("command", h => { h.Command++; return h; }),
            ("budget", h => { h.Budget++; return h; }), ("count", h => { h.Children++; return h; }),
            ("aggregate", h => { h.Measured++; return h; }), ("native error", h => { h.Error = 1; return h; }),
            ("transport error", h => { h.TransportError = 22; return h; }), ("backend error", h => { h.BackendError = 22; return h; }),
            ("visibility", h => { h.Visibility = 4; return h; }), ("failed stop", h => { h.Visibility = 3; h.Stop = 5; h.MayHaveDelivery = 0; return h; }),
            ("premature completion", h => { h.Visibility = 3; h.Stop = 1; h.MayHaveDelivery = 0; return h; }),
            ("false deferral", h => { h.Visibility = 3; h.Stop = 2; h.MayHaveDelivery = 0; return h; }),
            ("false interruption", h => { h.Visibility = 3; h.Stop = 4; h.MayHaveDelivery = 0; return h; }),
            ("active stop", h => { h.Stop = 1; return h; }), ("delivery", h => { h.MayHaveDelivery = 2; return h; }),
            ("zero clock", h => { h.Captured = 0; return h; })
        };
        foreach (var mutation in mutations)
            Reject(() => adapter.Accept(Header(baseline, mutation.Change), fixture.State), $"Invalid {mutation.Name} released timer accounting.");
        foreach (var change in new Func<TargetLeaseRawChild, TargetLeaseRawChild>[]
        {
            c => c with { Valid = false }, c => c with { WaitStatus = 2 }, c => c with { InstructionPointer = 99 },
            c => c with { Ordinal = 1 }, c => c with { Requested = 49999 }, c => c with { Waited = ulong.MaxValue },
            c => c with { Armed = children[0].Raw.Finished + 1 }, c => c with { Started = c.Armed - 1 },
            c => c with { Finished = c.Started - 1 }, c => c with { Finished = c.Started + 5000001 }
        })
        {
            var invalid = children[1] with { Raw = change(children[1].Raw) };
            Reject(() => adapter.Accept(Observation(0, 0, children[0], invalid), fixture.State), "An invalid suffix published a valid prefix.");
        }
        var late = children[1] with { FirstManagedReceiptUpper = children[1].Raw.Armed + 100000001 };
        var lateObservation = Observation(0, 0, children[0], late);
        lateObservation = lateObservation with { Raw = lateObservation.Raw with { Clock = new(0, late.FirstManagedReceiptUpper) } };
        Reject(() => adapter.Accept(lateObservation, fixture.State), "A late first receipt paid debt.");
        var failedClock = baseline with { Raw = baseline.Raw with { Clock = new(1, 0) } };
        Reject(() => adapter.Accept(failedClock, fixture.State), "A failed clock paid debt.");
        var mismatch = baseline with { Received = new[] { children[0], children[1] with { Raw = children[1].Raw with { Waited = 1 } } } };
        Reject(() => adapter.Accept(mismatch, fixture.State), "A receipt unrelated to the raw journal paid debt.");
        Require(Parsed(SignalAccountingModule.confirmedServiceNanoseconds(fixture.Lease, fixture.State)) == 0
            && SignalAccountingModule.pendingCount(fixture.State) == 1, "Rejected evidence changed the original reservation.");
        var valid = adapter.Accept(baseline, fixture.State);
        Require(valid.Adapter.AcceptedCount == 2 && valid.Adapter.ConfirmedNanoseconds == 80000,
            "The rejected batches mutated the original adapter.");
    }

    private static void ReceiptAndHandlerBoundaries()
    {
        var fixture = Start();
        var raw = Children((40000, 0, 100))[0].Raw;
        raw = raw with { Finished = raw.Started + 5000000 };
        var child = new TargetLeaseReceivedChild(raw, raw.Finished + 1, raw.Armed + 100000000,
            ServiceReceiptTiming.Parse(raw.Armed, raw.Finished, raw.Finished + 1, raw.Armed + 100000000, 0));
        var observation = Observation(0, 0, child);
        observation = observation with { Raw = observation.Raw with { Clock = new(0, child.FirstManagedReceiptUpper) } };
        var applied = Adapter(fixture).Accept(observation, fixture.State);
        Require(applied.Adapter.AcceptedCount == 1 && applied.Adapter.ConfirmedNanoseconds == 40000 && !applied.Adapter.IsTerminal,
            "The exact 5 ms handler and 100 ms first receipt bounds must be inclusive.");
        var later = Header(observation, h => { h.Captured = child.FirstManagedReceiptUpper + 100; return h; });
        later = later with { Raw = later.Raw with { Clock = new(0, later.Raw.Header.Captured + 1) } };
        var repeated = applied.Adapter.Accept(later, applied.State);
        Require(ReferenceEquals(repeated.State, applied.State) && repeated.Adapter.ConfirmedNanoseconds == 40000,
            "A later snapshot must retain the original receipt bound.");
        Reject(() => repeated.Adapter.Accept(observation, repeated.State), "A regressed snapshot clock was accepted.");
        var terminal = Adapter(fixture).Accept(Observation(1, 2, Children((0, 0, 99))), fixture.State);
        Reject(() => terminal.Adapter.Accept(Observation(0, 0, Children((0, 0, 99))), terminal.State),
            "Terminal visibility regressed to active.");
        var pending = Observation(3, 2, Children((0, 0, 99)));
        pending = Header(pending, h => { h.MayHaveDelivery = 1; return h; });
        Reject(() => Adapter(fixture).Accept(pending, fixture.State), "Possible delivery was accepted as quiescent.");
    }
}
