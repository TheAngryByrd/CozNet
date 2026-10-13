using System.Runtime.InteropServices;

enum ProbeMode { Manual, Injected }

sealed class ProbePlan
{
    public ProbeMode Mode { get; }
    public int BackedgeInterval { get; }
    public long CallsPerBatch => Mode == ProbeMode.Manual ? 0 : 1 + (CpuWorkload.Iterations + 1) / BackedgeInterval;

    private ProbePlan(ProbeMode mode, int interval) => (Mode, BackedgeInterval) = (mode, interval);

    public static ProbePlan Parse(string? mode, string? interval)
    {
        ProbeMode parsedMode = mode switch
        {
            null or "manual" => ProbeMode.Manual,
            "injected" => ProbeMode.Injected,
            _ => throw new ArgumentException("Select manual or injected probes.")
        };
        int parsedInterval = interval switch
        {
            null or "1" => 1,
            "64" => 64,
            "256" => 256,
            "1024" => 1024,
            _ => throw new ArgumentException("Select probe interval 1, 64, 256, or 1024.")
        };
        if (parsedMode == ProbeMode.Manual && parsedInterval != 1)
            throw new ArgumentException("Manual probes require interval 1.");
        return new ProbePlan(parsedMode, parsedInterval);
    }
}

static class InjectedProbes
{
    [ThreadStatic] private static Action? callback;
    [ThreadStatic] private static long calls;
    public static long Calls => calls;

    public static void Hit()
    {
        calls++;
        callback?.Invoke();
    }

    public static IDisposable Bind(Action action) => new Binding(action);

    private sealed class Binding : IDisposable
    {
        private readonly int owner = Environment.CurrentManagedThreadId;
        private bool disposed;
        public Binding(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            if (callback is not null) throw new InvalidOperationException("This thread already has a probe callback.");
            callback = action;
        }
        public void Dispose()
        {
            if (Environment.CurrentManagedThreadId != owner) throw new InvalidOperationException("The probe callback belongs to another thread.");
            if (disposed) return;
            callback = null;
            disposed = true;
        }
    }

    public static void VerifyRewrite(ProbePlan plan, long warmupCalls)
    {
        if (CozGetStatus() != 2 || CozGetInstrumentedMethods() != 1 || warmupCalls != plan.CallsPerBatch)
            throw new InvalidOperationException($"The native profiler did not inject the expected loop probes: status={CozGetStatus()}, methods={CozGetInstrumentedMethods()}, calls={warmupCalls}.");
    }
    [DllImport("CozNet.Profiler")]
    private static extern int CozGetStatus();
    [DllImport("CozNet.Profiler")]
    private static extern int CozGetInstrumentedMethods();
}
