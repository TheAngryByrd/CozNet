using CozNet;
using Microsoft.FSharp.Core;

static class PeerSignalCadenceChecks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static T Parsed<T>(FSharpResult<T, string> result) =>
        result.IsOk ? result.ResultValue : throw new InvalidOperationException(result.ErrorValue);

    public static void Run()
    {
        InitialCheckAndUnchangedState();
        NewCreditRequiresCheck();
        MeasuredCompletionRequiresRemainingDebtCheck();
        DeferredCompletionRequiresRetry();
        CreditWhilePendingSurvivesSkippedPreparation();
        FreshAccountingWindowNeedsCheck();
        ZeroCreditObservationStillChangesState();
    }

    private static void InitialCheckAndUnchangedState()
    {
        var replay = new Replay();
        Require(replay.Cadence.NeedsCheck(replay.State), "The first state must permit a peer check.");
        replay.IdleCheck();
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool needed = false;
        for (int index = 0; index < 10000; index++) needed |= replay.Cadence.NeedsCheck(replay.State);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(!needed && allocated == 0, $"Unchanged peer state required a check or allocated bytes: needed={needed}, allocated={allocated}.");
    }

    private static void NewCreditRequiresCheck()
    {
        var replay = new Replay();
        replay.IdleCheck();
        replay.Credit(1, 1000000);
        Require(replay.Cadence.NeedsCheck(replay.State), "New selected credit must permit a peer check.");
        var request = replay.Prepare(1);
        Require(SignalDelayRequestModule.nanoseconds(request) == 50000 && SignalAccountingModule.globalNanoseconds(replay.State) == 500000,
            "A one-millisecond selected interval at 50 percent must publish 500000 ns and reserve a bounded peer wait.");
        Require(!replay.Cadence.NeedsCheck(replay.State), "The post-send state must suppress another unchanged peer check.");
    }

    private static void MeasuredCompletionRequiresRemainingDebtCheck()
    {
        var replay = new Replay();
        replay.Credit(1, 1000000);
        var request = replay.Prepare(1);
        replay.Complete(request, 1, SignalSettlement.NewMeasured(Parsed(DelayTicksModule.tryCreate(100000))));
        Require(replay.Cadence.NeedsCheck(replay.State), "A measured acknowledgment must permit another debt check.");
        var next = replay.Prepare(2);
        Require(SignalDelayRequestModule.nanoseconds(next) == 50000 && SignalAccountingModule.pendingCount(replay.State) == 1,
            "Partial settlement must leave the remaining peer debt serviceable.");
    }

    private static void DeferredCompletionRequiresRetry()
    {
        var replay = new Replay();
        replay.Credit(1, 1000000);
        var request = replay.Prepare(1);
        replay.Complete(request, 1, SignalSettlement.Deferred);
        Require(replay.Cadence.NeedsCheck(replay.State), "A deferred acknowledgment must permit a retry.");
        Require(SignalDelayRequestModule.nanoseconds(replay.Prepare(2)) == 50000,
            "A deferred request must not consume peer debt.");
    }

    private static void CreditWhilePendingSurvivesSkippedPreparation()
    {
        var replay = new Replay();
        replay.Credit(1, 1000000);
        var request = replay.Prepare(1);
        replay.Credit(2, 2000000);
        Require(replay.Cadence.NeedsCheck(replay.State) && SignalAccountingModule.pendingCount(replay.State) == 1,
            "Credit can change while the peer has a pending request.");
        replay.Cadence.RecordChecked(replay.State);
        replay.Complete(request, 1, SignalSettlement.NewMeasured(Parsed(DelayTicksModule.tryCreate(100000))));
        Require(replay.Cadence.NeedsCheck(replay.State), "An acknowledgment must re-enable service after a skipped pending preparation.");
        Require(SignalDelayRequestModule.nanoseconds(replay.Prepare(2)) == 50000
            && SignalAccountingModule.globalNanoseconds(replay.State) == 1000000,
            "Credit published during the pending interval must remain in the ledger.");
    }

    private static void FreshAccountingWindowNeedsCheck()
    {
        var previous = new Replay();
        previous.IdleCheck();
        var fresh = new Replay();
        Require(previous.Cadence.NeedsCheck(fresh.State), "A fresh accounting window cannot inherit an unchanged-state decision.");
    }

    private static void ZeroCreditObservationStillChangesState()
    {
        var replay = new Replay(0);
        replay.IdleCheck();
        replay.Credit(1, 1000000);
        Require(replay.Cadence.NeedsCheck(replay.State) && SignalAccountingModule.globalNanoseconds(replay.State) == 0,
            "A new zero-credit observation must be distinguished from an unchanged state.");
        replay.IdleCheck();
        Require(!replay.Cadence.NeedsCheck(replay.State), "An unchanged zero-credit state must suppress repeated peer checks.");
    }

    private sealed class Replay
    {
        private readonly LedgerThread selected, peer;
        private readonly Guid stream;
        public SignalAccounting State { get; private set; }
        public PeerSignalCadence Cadence { get; } = new();

        public Replay(int percent = 50)
        {
            var first = Parsed(SignalAccountingModule.register(FSharpOption<int>.Some(percent), SignalAccountingModule.create()));
            var second = Parsed(SignalAccountingModule.register(FSharpOption<int>.None, first.Item2));
            selected = first.Item1;
            peer = second.Item1;
            State = second.Item2;
            stream = Parsed(SignalAccountingModule.sampleIdentity(selected, State));
        }

        public void Credit(ulong sequence, ulong represented)
        {
            var observation = Parsed(CpuSampleObservationModule.tryCreate(stream, sequence, represented, 0,
                SampleThreadState.Runnable, SampleLocation.Selected));
            State = Parsed(SignalAccountingModule.observe(selected, observation, State)).Item2;
            var published = Parsed(SignalAccountingModule.prepare(selected, State));
            Require(published.Item1 is null, "The replay's selected credit must publish without requiring a selected wait.");
            State = published.Item2;
        }

        public void IdleCheck()
        {
            var prepared = Parsed(SignalAccountingModule.prepare(peer, State));
            Require(prepared.Item1 is null, "An idle peer must have no delay request.");
            State = prepared.Item2;
            Cadence.RecordChecked(State);
        }

        public SignalDelayRequest Prepare(ulong sequence)
        {
            var prepared = Parsed(SignalAccountingModule.prepare(peer, State));
            var request = prepared.Item1?.Value ?? throw new InvalidOperationException("The peer must have unpaid debt.");
            State = Parsed(SignalAccountingModule.sent(request, sequence, prepared.Item2));
            Cadence.RecordChecked(State);
            return request;
        }

        public void Complete(SignalDelayRequest request, ulong sequence, SignalSettlement settlement) =>
            State = Parsed(SignalAccountingModule.complete(request, sequence, settlement, State));
    }
}
