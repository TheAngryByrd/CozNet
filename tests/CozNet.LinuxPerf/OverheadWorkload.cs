using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

enum OverheadStage { Baseline, Loaded, Probes, Callbacks, Collection }

sealed class OverheadPlan
{
    public OverheadStage Stage { get; }
    public bool Loaded => Stage != OverheadStage.Baseline;
    public bool Injected => Stage is OverheadStage.Probes or OverheadStage.Callbacks or OverheadStage.Collection;
    public bool Callbacks => Stage is OverheadStage.Callbacks or OverheadStage.Collection;
    public bool Collect => Stage == OverheadStage.Collection;
    public ProbePlan Probes => ProbePlan.Parse(Injected ? "injected" : "manual", Injected ? "1024" : "1");
    private OverheadPlan(OverheadStage stage) => Stage = stage;
    public static OverheadPlan Parse(string value) => new(value switch
    {
        "baseline" => OverheadStage.Baseline,
        "loaded" => OverheadStage.Loaded,
        "probes" => OverheadStage.Probes,
        "callbacks" => OverheadStage.Callbacks,
        "collection" => OverheadStage.Collection,
        _ => throw new ArgumentException("Select baseline, loaded, probes, callbacks, or collection overhead.")
    });
}

interface ICpuEventSession : IDisposable
{
    void Enable();
    ulong Disable();
    SampleBatch Drain();
}

static class OverheadWorkload
{
    public const string SelectedSymbol = "int64 [CozNet.LinuxPerf] CpuWorkload::Run()[Optimized]";

    public static void Run(string path, OverheadPlan plan, MeasurementWindow window,
        IThreadCpuClock? cpuClock, Func<ICpuSampleSink, ICpuEventSession>? eventFactory)
    {
        if (Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") != "0" ||
            Environment.GetEnvironmentVariable("DOTNET_ReadyToRun") != "0")
            throw new ArgumentException("Overhead measurements require disabled tiering and ReadyToRun.");
        bool profilerEnabled = Environment.GetEnvironmentVariable("CORECLR_ENABLE_PROFILING") == "1";
        if (profilerEnabled != plan.Loaded || plan.Callbacks != (cpuClock is not null) || plan.Collect != (eventFactory is not null))
            throw new ArgumentException("The overhead stage does not match its profiler, CPU clock, or event source.");
        var probes = plan.Probes;
        long beforeWarmup = InjectedProbes.Calls;
        if (CpuWorkload.Run() != 49995000L) throw new InvalidOperationException("The overhead warmup changed its checksum.");
        long warmupCalls = InjectedProbes.Calls - beforeWarmup;
        if (warmupCalls != probes.CallsPerBatch) throw new InvalidOperationException("The overhead warmup has an incorrect helper count.");
        var profiler = plan.Loaded ? InjectedProbes.ReadProfilerState() : null;
        if (profiler is not null && (profiler.Status != (plan.Injected ? 2 : 1) || profiler.InstrumentedMethods != (plan.Injected ? 1 : 0)))
            throw new InvalidOperationException("The overhead profiler did not reach the requested load or rewrite state.");
        using var intervention = plan.Callbacks
            ? new SampledIntervention(SelectedSymbol, 0, new ManagedWaitBackend(), cpuClock!, 10000000, probes) : null;
        using var binding = plan.Callbacks ? InjectedProbes.Bind(intervention!.Probe) : null;
        using var ready = new ManualResetEventSlim();
        using var stop = new ManualResetEventSlim();
        Task<(long Batches, long Calls)>? plainWorker = null;
        long initialCalls = InjectedProbes.Calls;
        long completedBatches = 0, checksum = 0, workerBatches = 0, workerCalls = 0;
        double experimentMilliseconds = 0;
        InterventionResult? interventionResult = null;
        SampleBatch? records = null;
        CpuSample[] delivered = [];
        ulong? cpuNanoseconds = null;
        int liveSamples = 0;
        try
        {
            if (intervention is null)
            {
                plainWorker = Task.Factory.StartNew(() =>
                {
                    long batches = 0, initial = InjectedProbes.Calls;
                    ready.Set();
                    do
                    {
                        if (CpuWorkload.Run() != 49995000L) throw new InvalidOperationException("The overhead worker changed its checksum.");
                        batches++;
                    } while (!stop.IsSet);
                    return (batches, InjectedProbes.Calls - initial);
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                if (!ready.Wait(TimeSpan.FromSeconds(5))) throw new InvalidOperationException("The overhead worker did not start.");
            }
            else intervention.WaitUntilReady();
            var capture = new SampleCapture(intervention);
            using var events = eventFactory?.Invoke(capture);
            using var collector = events is null ? null : new LiveSampleCollector(events.Drain);
            events?.Enable();
            var elapsed = Stopwatch.StartNew();
            while (elapsed.ElapsedMilliseconds < window.Milliseconds)
            {
                checksum += CpuWorkload.Run();
                completedBatches++;
            }
            liveSamples = collector?.Latest?.Samples ?? 0;
            var live = intervention?.ObserveLive();
            experimentMilliseconds = elapsed.Elapsed.TotalMilliseconds;
            cpuNanoseconds = events?.Disable();
            records = collector?.Complete();
            delivered = capture.AfterCollectorJoin();
            interventionResult = plan.Collect ? intervention?.Complete(live!) : intervention?.CompleteWithoutSamples();
        }
        finally
        {
            stop.Set();
            if (plainWorker is not null) (workerBatches, workerCalls) = plainWorker.GetAwaiter().GetResult();
        }
        if (interventionResult is not null)
        {
            workerBatches = interventionResult.WorkerBatches;
            workerCalls = interventionResult.WorkerInjectedCalls;
        }
        long calls = InjectedProbes.Calls - initialCalls;
        File.WriteAllText(path, JsonSerializer.Serialize(new {
            stage = plan.Stage.ToString(), measurementMilliseconds = window.Milliseconds, experimentMilliseconds,
            completedBatches, checksum, calls, warmupCalls, workerBatches, workerCalls, profiler, cpuNanoseconds,
            liveSamples, records, delivered, intervention = interventionResult,
            runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString()
        }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        if (completedBatches == 0 || workerBatches == 0 || checksum != checked(completedBatches * 49995000L) ||
            calls != checked(completedBatches * probes.CallsPerBatch) || workerCalls != checked(workerBatches * probes.CallsPerBatch))
            throw new InvalidOperationException("Overhead progress, checksum, or helper counts changed.");
        if (interventionResult is not null && (interventionResult.CreditedTicks != 0 || interventionResult.GlobalDelayTicks != 0 ||
            interventionResult.WorkerWaitTicks != 0)) throw new InvalidOperationException("Zero-speedup overhead incurred causal delay.");
        if (!plan.Collect && (delivered.Length != 0 || records is not null || interventionResult?.Live.AppliedSamples > 0))
            throw new InvalidOperationException("A non-collection stage received CPU events.");
        if (plan.Collect && (records is null || records.Samples == 0 || liveSamples == 0 || records.SelectedMethodSamples == 0 ||
            cpuNanoseconds is null or 0 || records.LostRecords != 0 || records.LostSamples != 0 || records.Throttles != 0 ||
            records.Samples != delivered.Length || records.SelectedMethodSamples != delivered.Count(x => x.Method == SelectedSymbol) ||
            records.RepresentedCpuNanoseconds != delivered.Aggregate(0UL, (sum, x) => checked(sum + x.PeriodNanoseconds)) ||
            interventionResult!.Live.AppliedSamples == 0))
            throw new InvalidOperationException("Collection overhead did not preserve live CPU-event accounting.");
    }
}
