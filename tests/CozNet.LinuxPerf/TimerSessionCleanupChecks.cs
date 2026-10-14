static class TimerSessionCleanupChecks
{
    private sealed class Samples(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    public static void Run()
    {
        SuccessfulCleanupPreservesOrder();
        SampleDisposalFailureDoesNotSkipNativeClose();
        MultipleCleanupFailuresRemainOrdered();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void SuccessfulCleanupPreservesOrder()
    {
        var calls = new List<string>();
        var result = TimerSessionCleanup.Run(new Samples(() => calls.Add("samples")),
            () => calls.Add("native"), () => calls.Add("gate"));
        Require(result.NativeClosed && result.Errors.Length == 0 && calls.SequenceEqual(new[] { "samples", "native", "gate" }),
            "Successful cleanup must dispose samples, close native state, and dispose the start gate in order.");
        var absent = TimerSessionCleanup.Run(null, () => calls.Add("native-only"), () => calls.Add("gate-only"));
        Require(absent.NativeClosed && absent.Errors.Length == 0 && calls.TakeLast(2).SequenceEqual(new[] { "native-only", "gate-only" }),
            "A missing sample session must not prevent cleanup after partial setup.");
    }

    private static void SampleDisposalFailureDoesNotSkipNativeClose()
    {
        var failure = new IOException("Injected sample disposal failure.");
        bool closed = false, disposedGate = false;
        var result = TimerSessionCleanup.Run(new Samples(() => throw failure), () => closed = true, () => disposedGate = true);
        Require(closed && disposedGate && result.NativeClosed && result.Errors.Length == 1 && ReferenceEquals(result.Errors[0], failure),
            "A sample disposal failure must survive successful native close without escaping before evidence can be written.");
    }

    private static void MultipleCleanupFailuresRemainOrdered()
    {
        var sample = new IOException("Injected sample disposal failure.");
        var native = new IOException("Injected native close failure.");
        var gate = new IOException("Injected gate disposal failure.");
        var result = TimerSessionCleanup.Run(new Samples(() => throw sample), () => throw native, () => throw gate);
        Require(!result.NativeClosed && result.Errors.SequenceEqual(new Exception[] { sample, native, gate }),
            "Every cleanup error must remain in operation order, and failed native close must not be reported as closed.");
    }
}
