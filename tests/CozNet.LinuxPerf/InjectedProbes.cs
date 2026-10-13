using System.Runtime.InteropServices;

enum ProbeMode { Manual, Injected }

static class InjectedProbes
{
    [ThreadStatic] private static Action? callback;
    [ThreadStatic] private static long calls;
    public static long Calls => calls;

    public static ProbeMode ParseMode(string? value) => value switch
    {
        null or "manual" => ProbeMode.Manual,
        "injected" => ProbeMode.Injected,
        _ => throw new ArgumentException("Select manual or injected probes.")
    };

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

    public static void VerifyRewrite(long warmupCalls)
    {
        if (CozGetStatus() != 2 || CozGetInstrumentedMethods() != 1 || warmupCalls != 10002)
            throw new InvalidOperationException($"The native profiler did not inject the expected loop probes: status={CozGetStatus()}, methods={CozGetInstrumentedMethods()}, calls={warmupCalls}.");
    }
    [DllImport("CozNet.Profiler")]
    private static extern int CozGetStatus();
    [DllImport("CozNet.Profiler")]
    private static extern int CozGetInstrumentedMethods();
}
