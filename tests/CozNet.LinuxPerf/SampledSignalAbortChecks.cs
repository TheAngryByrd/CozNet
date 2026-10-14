using System.Diagnostics.Tracing;
using System.Text.Json;

internal static class SampledSignalAbortChecks
{
    private const string Failure = "Injected controller CPU failure.";

    public static int Run(string path, string startup)
    {
        var plan = SampledSignalPlan.Parse("0", "qualified-sample", startup, "no-gc");
        var trace = new SampledSignalTrace();
        var backend = new Backend();
        using var listener = new Listener();
        int result = SampledSignalWorkload.Run(path, plan, () => backend, _ => new Session(), new Clock(), trace);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        string stage = plan.WarmupMilliseconds > 0 ? "warmup" : "measurement";
        if (result != 1 || root.GetProperty("passed").GetBoolean() || root.GetProperty("protocolPassed").GetBoolean()
            || !root.GetProperty("joined").GetBoolean() || !root.GetProperty("restored").GetBoolean()
            || root.GetProperty("failureBoundary").GetProperty("Stage").GetString() != stage
            || trace.AbortBoundary?.Stage != stage || trace.AbortBoundary.AnchorId != 2
            || trace.AbortBoundary.FailureTimestamp != root.GetProperty("failureBoundary").GetProperty("Timestamp").GetInt64()
            || !root.GetProperty("failures").EnumerateArray().Any(value => value.GetString()!.Contains(Failure, StringComparison.Ordinal))
            || !trace.Anchors.Select(anchor => anchor.Phase).SequenceEqual(["run-start", stage + "-start", "run-abort", "run-end"]))
            throw new InvalidOperationException("The injected failure must preserve its abort marker, failed result, and joined targets.");
        return 0;
    }

    private sealed class Listener : EventListener
    {
        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "CozNet-SampledSignals") EnableEvents(source, EventLevel.Informational);
        }
    }

    private sealed class Clock : IThreadCpuClock
    {
        public long ReadNanoseconds() => throw new InvalidOperationException(Failure);
    }

    private sealed class Session : ICpuEventSession
    {
        public void Enable() { }
        public ulong Disable() => throw new InvalidOperationException("Unexpected sampling disable before the injected failure.");
        public SampleBatch Drain() => throw new InvalidOperationException("Unexpected sample drain before the injected failure.");
        public void Dispose() { }
    }

    private sealed class Backend : IBoundedInterventionBackend
    {
        private readonly int[] owners = new int[2];
        private int retired;
        public string Name => "trace-abort-check";
        public object Describe() => new { injectedControllerFailure = true };
        public bool IsEligibleAddress(ulong address) => false;
        public void RegisterCurrentThread(int slot) => owners[slot] = Environment.CurrentManagedThreadId;
        public ulong Request(int slot, SignalPlan plan) => throw new InvalidOperationException("Unexpected native request.");
        public SignalAcknowledgment? Read(int slot, ulong sequence) => throw new InvalidOperationException("Unexpected native read.");
        public void RetireCurrentThread(int slot)
        {
            if (owners[slot] != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("The abort fixture retired a target from another thread.");
            Interlocked.Increment(ref retired);
        }
        public void RestoreAfterThreadJoin()
        {
            if (retired != 2) throw new InvalidOperationException("Both aborted targets must retire before restoration.");
        }
    }
}
