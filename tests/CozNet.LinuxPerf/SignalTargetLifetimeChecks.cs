static class SignalTargetLifetimeChecks
{
    private sealed class Backend : IInterventionBackend
    {
        public string Name => "lifetime-check";
        public int RegistrationThread, RetirementThread, Retirements;
        public object Describe() => new { };
        public bool IsEligibleAddress(ulong address) => false;
        public void RegisterCurrentThread(int slot) => RegistrationThread = Environment.CurrentManagedThreadId;
        public void RetireCurrentThread(int slot)
        {
            RetirementThread = Environment.CurrentManagedThreadId;
            Interlocked.Increment(ref Retirements);
        }
        public void RestoreAfterThreadJoin() { }
    }

    public static void UnresolvedDeliveryKeepsTargetAlive(bool failBody = false)
    {
        var backend = new Backend();
        using var stopped = new ManualResetEventSlim(true);
        using var rechecked = new ManualResetEventSlim();
        using var permitQuiescence = new ManualResetEventSlim();
        Exception? failure = null;
        var expected = new InvalidOperationException("Injected target body failure.");
        int checks = 0;
        var worker = new Thread(() => SignalTargetLifetime.Run(backend, 1, stopped, () =>
        {
            if (Interlocked.Increment(ref checks) == 1) return false;
            rechecked.Set();
            permitQuiescence.Wait();
            return true;
        }, () => { if (failBody) throw expected; }, error => failure = error)) { IsBackground = true };
        worker.Start();
        bool observed = false, stayedAlive = false, joined = false;
        try
        {
            observed = rechecked.Wait(TimeSpan.FromSeconds(3));
            stayedAlive = worker.IsAlive && Volatile.Read(ref backend.Retirements) == 0;
        }
        finally
        {
            permitQuiescence.Set();
            joined = worker.Join(TimeSpan.FromSeconds(3));
        }
        if (!observed || !stayedAlive || !joined || (failBody ? !ReferenceEquals(failure, expected) : failure is not null) || backend.Retirements != 1
            || backend.RegistrationThread != backend.RetirementThread)
            throw new InvalidOperationException($"Target lifetime failed: rechecked={observed}, alive={stayedAlive}, joined={joined}, retirements={backend.Retirements}.", failure);
    }
}
