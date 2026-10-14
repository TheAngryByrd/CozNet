static class SignalTargetChecks
{
    public static void Run()
    {
        FailedTargetWaitsForSenderStop();
        CompletedTargetWaitsForSenderStop();
        PendingRequestPreventsRetirement();
    }
    private static void FailedTargetWaitsForSenderStop() => CheckLifetime(true, true);
    private static void CompletedTargetWaitsForSenderStop() => CheckLifetime(false, true);
    private static void PendingRequestPreventsRetirement() => CheckLifetime(true, false);
    private static void CheckLifetime(bool fail, bool drained)
    {
        using var stopped = new ManualResetEventSlim();
        using var completed = new ManualResetEventSlim();
        var backend = new RecordingBackend();
        Exception? observed = null;
        var expected = new InvalidOperationException("Injected target failure.");
        var target = new Thread(() => SignalTargetLifetime.Run(backend, 0, stopped, () => drained,
            () =>
            {
                if (fail) throw expected;
                completed.Set();
            },
            error => { observed = error; completed.Set(); })) { IsBackground = true };
        target.Start();
        bool joinedBeforeStop = false, reached = false, retiredBeforeStop = false, joinedAfterStop;
        try
        {
            reached = completed.Wait(TimeSpan.FromSeconds(1));
            joinedBeforeStop = target.Join(TimeSpan.FromMilliseconds(100));
            retiredBeforeStop = Volatile.Read(ref backend.RetiredOn) != 0;
        }
        finally
        {
            stopped.Set();
            joinedAfterStop = target.Join(TimeSpan.FromSeconds(1));
        }
        if (!reached || joinedBeforeStop || retiredBeforeStop || !joinedAfterStop)
            throw new InvalidOperationException($"Target lifetime failed: reached={reached}, joinedBeforeStop={joinedBeforeStop}, retiredBeforeStop={retiredBeforeStop}, joinedAfterStop={joinedAfterStop}.");
        if (fail ? !ReferenceEquals(observed, expected) : observed is not null)
            throw new InvalidOperationException("Target lifetime did not preserve the body failure.");
        if (backend.RegisteredOn == 0 || (drained ? backend.RetiredOn != backend.RegisteredOn : backend.RetiredOn != 0))
            throw new InvalidOperationException("Target retirement did not preserve request ownership or thread identity.");
    }
    private sealed class RecordingBackend : IBoundedInterventionBackend
    {
        public int RegisteredOn, RetiredOn;
        public string Name => "target-lifetime-check";
        public object Describe() => new { };
        public bool IsEligibleAddress(ulong address) => false;
        public void RegisterCurrentThread(int slot) => Volatile.Write(ref RegisteredOn, Environment.CurrentManagedThreadId);
        public void RetireCurrentThread(int slot) => Volatile.Write(ref RetiredOn, Environment.CurrentManagedThreadId);
        public ulong Request(int slot, SignalPlan plan) => throw new NotSupportedException();
        public SignalAcknowledgment? Read(int slot, ulong sequence) => throw new NotSupportedException();
        public void RestoreAfterThreadJoin() => throw new NotSupportedException();
    }
}
