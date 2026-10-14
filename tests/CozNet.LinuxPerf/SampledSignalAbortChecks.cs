using System.Diagnostics.Tracing;
using System.Text.Json;

internal static class SampledSignalAbortChecks
{
    private const string Failure = "Injected controller CPU failure.";
    public static int OwnerSchedulerShutdown(string path, string mode)
    {
        if (mode is not ("normal" or "finish-failure")) throw new ArgumentException("Invalid owner scheduler fixture mode.");
        int finished = 0, disposed = 0;
        bool fail = mode == "finish-failure";
        ISchedulerEventSession Create(bool owner) => new SchedulerSession(owner && fail, false,
            () => Interlocked.Increment(ref finished), () => Interlocked.Increment(ref disposed));
        var backend = new Backend(() => Volatile.Read(ref finished) == 4 && Volatile.Read(ref disposed) == 4,
            () => Create(true));
        int result = SampledSignalWorkload.Run(path, SampledSignalPlan.Parse("0", "qualified-sample", "warm"),
            () => backend, _ => new Session(), new Clock(), schedulerFactory: () => Create(false), captureServiceOwnerScheduling: true);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (result != 1 || root.GetProperty("passed").GetBoolean()
            || !root.GetProperty("serviceOwnerSchedulerTiming").GetBoolean()
            || root.GetProperty("schedulerCaptures").GetArrayLength() != 4
            || root.GetProperty("schedulerStopped").GetBoolean() == fail
            || root.GetProperty("joined").GetBoolean() == fail || root.GetProperty("restored").GetBoolean() == fail
            || backend.Retired != (fail ? 0 : 2) || finished != (fail ? 3 : 4) || disposed != (fail ? 3 : 4))
            throw new InvalidOperationException("Owner capture must finish before target retirement and service restoration.");
        return 0;
    }

    public static int SchedulerShutdown(string path, string mode)
    {
        if (mode is not ("normal" or "finish-failure" or "creation-failure" or "capture-failure"))
            throw new ArgumentException("Invalid scheduler shutdown fixture mode.");
        int created = 0, finished = 0, disposed = 0;
        int expected = mode == "creation-failure" ? 2 : 3;
        var backend = new Backend(() => Volatile.Read(ref finished) == expected && Volatile.Read(ref disposed) == expected);
        ISchedulerEventSession Create()
        {
            int index = Interlocked.Increment(ref created);
            if (mode == "creation-failure" && index == 2)
                throw new InvalidOperationException("Injected scheduler creation failure.");
            return new SchedulerSession(mode == "finish-failure" && index == 2,
                mode == "capture-failure" && index == 2,
                () => Interlocked.Increment(ref finished), () => Interlocked.Increment(ref disposed));
        }
        int result = SampledSignalWorkload.Run(path, SampledSignalPlan.Parse("0", "qualified-sample", "warm"),
            () => backend, _ => new Session(), new Clock(), schedulerFactory: Create);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        bool failedStop = mode == "finish-failure";
        if (result != 1 || root.GetProperty("passed").GetBoolean() || root.GetProperty("protocolPassed").GetBoolean()
            || root.GetProperty("schedulerStopped").GetBoolean() == failedStop
            || root.GetProperty("joined").GetBoolean() == failedStop
            || root.GetProperty("restored").GetBoolean() == failedStop
            || backend.Retired != (failedStop ? 0 : 2)
            || finished != (failedStop ? 2 : expected) || disposed != (failedStop ? 2 : expected))
            throw new InvalidOperationException($"Scheduler shutdown invariant failed for {mode}: retired={backend.Retired}, finished={finished}, disposed={disposed}.");
        string expectedFailure = mode switch
        {
            "finish-failure" => "Injected scheduler stop failure.",
            "creation-failure" => "workers failed to become ready",
            "capture-failure" => "Injected scheduler capture loss.",
            _ => Failure
        };
        if (!root.GetProperty("failures").EnumerateArray().Any(value => value.GetString()!.Contains(expectedFailure, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Scheduler fixture did not retain {expectedFailure}");
        if (mode == "creation-failure" && root.GetProperty("schedulerCaptures").EnumerateArray().Count(value => value.ValueKind == JsonValueKind.Null) != 1)
            throw new InvalidOperationException("Failed registration must preserve the missing scheduler capture.");
        return 0;
    }

    private sealed class SchedulerSession(bool failStop, bool failCapture, Action finish, Action dispose) : ISchedulerEventSession
    {
        private bool stopped;
        private bool released;
        public SchedulerCapture Finish()
        {
            if (failStop) throw new TimeoutException("Injected scheduler stop failure.");
            if (stopped) throw new InvalidOperationException("Scheduler fixture finished twice.");
            stopped = true;
            finish();
            return new(1, Environment.CurrentManagedThreadId, 1, 2, 3, 4, failCapture ? 1UL : 0UL, [],
                failCapture ? ["Injected scheduler capture loss."] : []);
        }
        public void Dispose()
        {
            if (!stopped || released) throw new InvalidOperationException("Scheduler fixture disposed outside its finished state.");
            released = true;
            dispose();
        }
    }

    public static int Run(string path, string startup, bool initialize = false, string percent = "0")
    {
        var plan = SampledSignalPlan.Parse(percent, "qualified-sample", startup, "no-gc");
        var trace = new SampledSignalTrace();
        var backend = new Backend();
        using var listener = new Listener();
        int result = SampledSignalWorkload.Run(path, plan, () => backend, _ => new Session(), new Clock(), trace,
            initializeManagedPaths: initialize);
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
        if (initialize) InitializationRetainsSeparateEvidenceAndResetsLiveState(root);
        return 0;
    }

    private static void InitializationRetainsSeparateEvidenceAndResetsLiveState(JsonElement root)
    {
        var preparation = root.GetProperty("initialization");
        var boundary = root.GetProperty("failureBoundary");
        var synthetic = preparation.GetProperty("Boundary");
        if (preparation.GetProperty("Source").GetString() != "synthetic"
            || preparation.GetProperty("Samples").GetArrayLength() != 1
            || preparation.GetProperty("Journals").GetArrayLength() != 2
            || synthetic.GetProperty("CreditNanoseconds").GetInt64() != 40000
            || !synthetic.GetProperty("WaitNanoseconds").EnumerateArray().Select(item => item.GetInt64()).SequenceEqual([30000L, 70000L])
            || synthetic.GetProperty("Pending").GetInt32() != 0
            || boundary.GetProperty("GlobalNanoseconds").GetInt64() != 0
            || boundary.GetProperty("CreditNanoseconds").GetInt64() != 0
            || boundary.GetProperty("Pending").GetInt32() != 0
            || boundary.GetProperty("WaitNanoseconds").EnumerateArray().Any(item => item.GetInt64() != 0)
            || boundary.GetProperty("DebtNanoseconds").EnumerateArray().Any(item => item.GetInt64() != 0)
            || root.GetProperty("samples").GetArrayLength() != 0
            || root.GetProperty("requests").GetArrayLength() != 0
            || root.GetProperty("serviceJournals").ValueKind != JsonValueKind.Null)
            throw new InvalidOperationException("Synthetic initialization must retain separate evidence and leave fresh live accounting.");
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

    private sealed class Backend(Func<bool>? canRetire = null, Func<ISchedulerEventSession>? ownerCapture = null) : IBoundedInterventionBackend, IServiceOwnerSchedulerBackend
    {
        private readonly int[] owners = new int[2];
        private int retired;
        public int Retired => Volatile.Read(ref retired);
        public string Name => "trace-abort-check";
        public ISchedulerEventSession CaptureOwnerScheduling() => ownerCapture is null
            ? throw new NotSupportedException("Owner capture is not configured.") : ownerCapture();
        public object Describe() => new { injectedControllerFailure = true };
        public bool IsEligibleAddress(ulong address) => false;
        public void RegisterCurrentThread(int slot) => owners[slot] = Environment.CurrentManagedThreadId;
        public ulong Request(int slot, SignalPlan plan) => throw new InvalidOperationException("Unexpected native request.");
        public SignalAcknowledgment? Read(int slot, ulong sequence) => throw new InvalidOperationException("Unexpected native read.");
        public void RetireCurrentThread(int slot)
        {
            if (canRetire is not null && !canRetire()) throw new InvalidOperationException("A target retired before scheduler capture stopped.");
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
