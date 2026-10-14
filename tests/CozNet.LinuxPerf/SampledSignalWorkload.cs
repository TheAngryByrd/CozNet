using System.Diagnostics;
using System.Text.Json;
using CozNet;
using Microsoft.FSharp.Core;

sealed class SampledSignalPlan
{
    public int Percent { get; }
    public SelectedSignalCadenceMode Cadence { get; }
    public int WarmupMilliseconds { get; }
    public bool ForceGc { get; }
    private SampledSignalPlan(int percent, SelectedSignalCadenceMode cadence, int warmup, bool forceGc) =>
        (Percent, Cadence, WarmupMilliseconds, ForceGc) = (percent, cadence, warmup, forceGc);
    public static SampledSignalPlan Parse(string value) => Parse(value, "continuous");
    public static SampledSignalPlan Parse(string value, string cadence) => Parse(value, cadence, "cold");
    public static SampledSignalPlan Parse(string value, string cadence, string startup) => Parse(value, cadence, startup, "no-gc");
    public static SampledSignalPlan Parse(string value, string cadence, string startup, string gc) => new(value switch
    {
        "0" => 0, "50" => 50,
        _ => throw new ArgumentException("Select zero or 50 percent sampled signal speedup.")
    }, cadence switch
    {
        "continuous" => SelectedSignalCadenceMode.Continuous,
        "qualified-sample" => SelectedSignalCadenceMode.QualifiedSample,
        _ => throw new ArgumentException("Select continuous or qualified-sample signal cadence.")
    }, startup switch
    {
        "cold" => 0, "warm" => 250,
        _ => throw new ArgumentException("Select cold or warm sampled signal startup.")
    }, gc switch
    {
        "no-gc" => false,
        "gc" => true,
        _ => throw new ArgumentException("Select gc or no-gc sampled signal measurement.")
    });
}

static class SampledSignalWorkload
{
    private sealed record Pending(SignalDelayRequest Request, ulong Sequence, long Started, string Stage,
        ControllerCpuSnapshot? CpuStart, TargetCpuSnapshot? TargetCpuStart);
    private sealed record RequestRecord(int Slot, ulong Sequence, long RequestedNanoseconds,
        double AcknowledgmentMilliseconds, SignalAcknowledgment Result, bool DuringMeasurement, string Stage,
        long StartedTimestamp, long CompletedTimestamp, string StartedStage,
        ControllerCpuSnapshot? ControllerCpuStart, ControllerCpuSnapshot? ControllerCpuEnd,
        TargetCpuSnapshot? TargetCpuStart, TargetCpuSnapshot? TargetCpuEnd);
    private sealed record SampleRecord(CpuSample Sample, ulong Epoch, bool Accepted, long CreditNanoseconds,
        double ConsumedMilliseconds, string Stage, long ConsumedTimestamp);
    private sealed record PendingRecord(int Slot, ulong Sequence, long RequestedNanoseconds, long StartedTimestamp, string StartedStage);
    private readonly record struct RuntimeCounters(string Stage, long StartedTimestamp, long FinishedTimestamp,
        long ControllerAllocatedBytes, int Gen0Collections, int Gen1Collections, int Gen2Collections, long GcPauseTicks, long ProcessCpuTicks);
    private sealed record Boundary(string Stage, double Milliseconds, long GlobalNanoseconds, long CreditNanoseconds,
        long[] WaitNanoseconds, long[] DebtNanoseconds, bool[] Registered, int Pending, long[] Batches,
        long Timestamp, PendingRecord[] PendingRequests, RuntimeCounters RuntimeCounters);
    private readonly record struct ServiceInterval(long StartedTimestamp, long FinishedTimestamp, int SamplesBefore, int SamplesAfter);
    private sealed record DebtPeak(int Slot, Boundary Boundary, ServiceInterval Interval, ulong[] LastAcknowledgedSequences);
    private sealed record StageResult(Boundary Measurement, Boundary Settled, long ControllerCpuNanoseconds,
        int SamplingPauses, long SelectedChecks, long[] MaxDebtNanoseconds, ServiceInterval? LongestServiceInterval, DebtPeak?[] PeakDebt,
        RuntimeCounters? RuntimeCountersStart, long PeerChecks, long PeerCheckSkips);
    private sealed class Sink(Action<CpuSample> publish) : ICpuSampleSink
    {
        public void Publish(CpuSample sample) => publish(sample);
    }
    private static T Parsed<T>(FSharpResult<T, string> result) =>
        result.IsOk ? result.ResultValue : throw new InvalidOperationException(result.ErrorValue);

    public static int Run(string path, SampledSignalPlan plan,
        Func<IBoundedInterventionBackend> backendFactory,
        Func<ICpuSampleSink, ICpuEventSession> sessionFactory, IThreadCpuClock controllerClock,
        SampledSignalTrace? trace = null, bool captureTargetCpu = false)
    {
        var failures = new List<string>();
        var requests = new List<RequestRecord>();
        var samples = new List<SampleRecord>();
        var boundaries = new List<Boundary>();
        var errors = new Exception?[2];
        long[] batches = new long[2], waits = new long[2], maxDebt = new long[2];
        long[] batchBaseline = new long[2];
        DebtPeak?[] peakDebt = new DebtPeak?[2];
        ulong[] lastAcknowledged = new ulong[2];
        ServiceInterval? longestServiceInterval = null;
        Pending?[] pending = new Pending?[2];
        bool[] nativeOutstanding = new bool[2];
        bool[] registered = [true, true];
        using var ready = new CountdownEvent(2);
        using var start = new ManualResetEventSlim();
        using var stop = new ManualResetEventSlim();
        using var deliveryStopped = new ManualResetEventSlim();
        IBoundedInterventionBackend? backend = null;
        ICpuEventSession? session = null;
        Thread[] workers = [];
        SampledGcStress? gcStress = null;
        bool gcJoined = true;
        GcStressObservation[]? gcObservations = null;
        long measurementStartedTimestamp = 0;
        bool enabled = false, accept = false, measuring = false, runningStage = false, joined = false, restored = false;
        string stage = "setup";
        ulong sequence = 0, represented = 0, epoch = 0;
        long credited = 0, controllerCpu = 0;
        int pauses = 0;
        SampleBatch? records = null;
        Boundary? measurement = null, settled = null, failureBoundary = null;
        StageResult? warmup = null;
        SampleBatch? measurementStartRecords = null;
        RuntimeCounters? runtimeCountersStart = null;
        var timer = new Stopwatch();
        var cadence = new SelectedSignalCadence(plan.Cadence);
        var peerCadence = new PeerSignalCadence();
        long selectedChecks = 0;
        long peerChecks = 0, peerCheckSkips = 0;
        var state = SignalAccountingModule.create();
        var selected = Parsed(SignalAccountingModule.register(FSharpOption<int>.Some(plan.Percent), state));
        state = selected.Item2;
        var peer = Parsed(SignalAccountingModule.register(FSharpOption<int>.None, state));
        state = peer.Item2;
        LedgerThread[] handles = [selected.Item1, peer.Item1];
        Guid stream = Parsed(SignalAccountingModule.sampleIdentity(handles[0], state));

        RuntimeCounters ReadRuntimeCounters()
        {
            long started = Stopwatch.GetTimestamp();
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
            long paused = GC.GetTotalPauseDuration().Ticks;
            long cpu = Environment.CpuUsage.TotalTime.Ticks;
            return new(stage, started, Stopwatch.GetTimestamp(), allocated, gen0, gen1, gen2, paused, cpu);
        }

        Boundary Snapshot()
        {
            long global = SignalAccountingModule.globalNanoseconds(state);
            long[] debt = [registered[0] ? Math.Max(0, global - credited - waits[0]) : 0,
                registered[1] ? Math.Max(0, global - waits[1]) : 0];
            return new(stage, timer.Elapsed.TotalMilliseconds, global, credited, (long[])waits.Clone(), debt,
                (bool[])registered.Clone(), SignalAccountingModule.pendingCount(state),
                [Volatile.Read(ref batches[0]) - batchBaseline[0], Volatile.Read(ref batches[1]) - batchBaseline[1]],
                Stopwatch.GetTimestamp(), Enumerable.Range(0, 2).Where(slot => pending[slot] is not null).Select(slot =>
                {
                    var operation = pending[slot]!;
                    return new PendingRecord(slot, operation.Sequence, SignalDelayRequestModule.nanoseconds(operation.Request),
                        operation.Started, operation.Stage);
                }).ToArray(), ReadRuntimeCounters());
        }
        void Publish(CpuSample sample)
        {
            if (samples.Count >= 20000) throw new InvalidOperationException("The sampled signal capture limit was exceeded.");
            long amount = 0;
            if (accept)
            {
                sequence = checked(sequence + 1);
                represented = checked(represented + sample.PeriodNanoseconds);
                var location = sample.Method == OverheadWorkload.SelectedSymbol ? SampleLocation.Selected
                    : sample.Method is null ? SampleLocation.Unmapped : SampleLocation.Other;
                var observation = Parsed(CpuSampleObservationModule.tryCreate(stream, sequence, represented,
                    epoch, SampleThreadState.Runnable, location));
                var transition = Parsed(SignalAccountingModule.observe(handles[0], observation, state));
                amount = DelayTicksModule.value(transition.Item1);
                state = transition.Item2;
                credited = checked(credited + amount);
                cadence.Observe(epoch);
            }
            samples.Add(new(sample, epoch, accept, amount, timer.Elapsed.TotalMilliseconds, stage, Stopwatch.GetTimestamp()));
        }
        void Drain()
        {
            records = session!.Drain();
            if (records.LostRecords != 0 || records.LostSamples != 0 || records.Throttles != 0)
                throw new InvalidOperationException("The sampled signal source lost or throttled events.");
        }
        void Complete(int slot)
        {
            var operation = pending[slot];
            if (operation is null) return;
            var result = backend!.Read(slot, operation.Sequence);
            long completed = Stopwatch.GetTimestamp();
            double elapsed = Stopwatch.GetElapsedTime(operation.Started, completed).TotalMilliseconds;
            if (result is null)
            {
                if (elapsed > 100) throw new TimeoutException("A sampled signal acknowledgment exceeded 100 milliseconds.");
                return;
            }
            nativeOutstanding[slot] = false;
            ControllerCpuSnapshot? cpuEnd = null;
            TargetCpuSnapshot? targetCpuEnd = null;
            try
            {
                if (captureTargetCpu) targetCpuEnd = ((ITargetCpuObservationBackend)backend).ReadTargetCpu(slot);
                if (trace is not null) cpuEnd = ControllerCpuSnapshot.Capture(controllerClock);
            }
            finally
            {
                requests.Add(new(slot, operation.Sequence, SignalDelayRequestModule.nanoseconds(operation.Request), elapsed, result, measuring, stage,
                    operation.Started, completed, operation.Stage, operation.CpuStart, cpuEnd, operation.TargetCpuStart, targetCpuEnd));
            }
            if (elapsed > 100 || result.MeasuredHandlerNanoseconds > 5000000 || result.Outcome == 4
                || result.Sequence != operation.Sequence || result.Outcome is < 1 or > 3
                || (result.Outcome == 1 && result.WaitNanoseconds != 0)
                || (result.Outcome is 2 or 3 && !backend.IsEligibleAddress(result.InstructionPointer)))
                throw new InvalidOperationException("A sampled signal acknowledgment failed its protocol bounds.");
            var settlement = result.Outcome == 1 ? SignalSettlement.Deferred
                : SignalSettlement.NewMeasured(Parsed(DelayTicksModule.tryCreate(checked((long)result.WaitNanoseconds))));
            state = Parsed(SignalAccountingModule.complete(operation.Request, operation.Sequence, settlement, state));
            waits[slot] = checked(waits[slot] + (long)result.WaitNanoseconds);
            pending[slot] = null;
            lastAcknowledged[slot] = operation.Sequence;
            if (slot == 0 && runningStage)
            {
                session!.Enable();
                enabled = true;
                accept = true;
            }
        }
        bool Prepare(int slot)
        {
            if (pending[slot] is not null) return false;
            if (slot == 1 && runningStage) peerChecks++;
            var transition = Parsed(SignalAccountingModule.prepare(handles[slot], state));
            state = transition.Item2;
            if (transition.Item1 is null) return true;
            var request = transition.Item1.Value;
            if (slot == 0 && enabled)
            {
                accept = false;
                session!.Disable();
                enabled = false;
                Drain();
                epoch = Parsed(SignalAccountingModule.requiredEpoch(handles[0], state));
                pauses++;
            }
            if (requests.Count >= 100000) throw new InvalidOperationException("The signal request capture limit was exceeded.");
            var cpuStart = trace is null ? null : ControllerCpuSnapshot.Capture(controllerClock);
            var targetCpuStart = captureTargetCpu ? ((ITargetCpuObservationBackend)backend!).ReadTargetCpu(slot) : null;
            long started = Stopwatch.GetTimestamp();
            nativeOutstanding[slot] = true;
            ulong nativeSequence = backend!.Request(slot, SignalPlan.FromRequest(request));
            pending[slot] = new(request, nativeSequence, started, stage, cpuStart, targetCpuStart);
            state = Parsed(SignalAccountingModule.sent(request, nativeSequence, state));
            return false;
        }

        Boundary RunStage(string name, int milliseconds)
        {
            stage = name;
            trace?.Mark(name + "-start");
            if (name == "measurement") measurementStartedTimestamp = Stopwatch.GetTimestamp();
            runtimeCountersStart = ReadRuntimeCounters();
            measuring = name == "measurement";
            accept = runningStage = true;
            session!.Enable();
            enabled = true;
            long initialCpu = controllerClock.ReadNanoseconds();
            timer.Restart();
            long previousServiceTimestamp = Stopwatch.GetTimestamp();
            start.Set();
            gcStress?.Release();
            double nextBoundary = 10;
            while (timer.ElapsedMilliseconds < milliseconds)
            {
                int samplesBefore = samples.Count;
                Drain();
                Complete(0);
                Complete(1);
                if (pending[0] is null && cadence.TryCheck())
                {
                    selectedChecks++;
                    Prepare(0);
                }
                if (peerCadence.NeedsCheck(state))
                {
                    Prepare(1);
                    peerCadence.RecordChecked(state);
                }
                else peerCheckSkips++;
                long finished = Stopwatch.GetTimestamp();
                var interval = new ServiceInterval(previousServiceTimestamp, finished, samplesBefore, samples.Count);
                previousServiceTimestamp = finished;
                if (longestServiceInterval is not { } longest
                    || finished - interval.StartedTimestamp > longest.FinishedTimestamp - longest.StartedTimestamp)
                    longestServiceInterval = interval;
                long global = SignalAccountingModule.globalNanoseconds(state);
                long selectedDebt = Math.Max(0, global - credited - waits[0]);
                long peerDebt = Math.Max(0, global - waits[1]);
                if (selectedDebt > maxDebt[0] || peerDebt > maxDebt[1])
                {
                    var peak = Snapshot();
                    for (int slot = 0; slot < 2; slot++)
                        if (peak.DebtNanoseconds[slot] > maxDebt[slot])
                        {
                            maxDebt[slot] = peak.DebtNanoseconds[slot];
                            peakDebt[slot] = new(slot, peak, interval, (ulong[])lastAcknowledged.Clone());
                        }
                }
                if (timer.Elapsed.TotalMilliseconds >= nextBoundary)
                {
                    var current = Snapshot();
                    boundaries.Add(current);
                    nextBoundary = current.Milliseconds + 10;
                }
            }
            var boundary = Snapshot();
            controllerCpu = controllerClock.ReadNanoseconds() - initialCpu;
            measuring = accept = runningStage = false;
            trace?.Mark(name + "-end");
            return boundary;
        }

        Boundary SettleStage(string name)
        {
            stage = name;
            if (enabled) { session!.Disable(); enabled = false; }
            Drain();
            for (int slot = 0; slot < 2; slot++)
            {
                var deadline = Stopwatch.StartNew();
                bool retired = false;
                while (!retired && deadline.ElapsedMilliseconds < 1000)
                {
                    Complete(0);
                    Complete(1);
                    if (Prepare(slot))
                    {
                        state = Parsed(SignalAccountingModule.unregister(handles[slot], state));
                        registered[slot] = false;
                        retired = true;
                    }
                }
                if (!retired) throw new TimeoutException("Diagnostic signal settlement exceeded one second.");
            }
            return Snapshot();
        }

        try
        {
            trace?.Mark("run-start");
            if (Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") != "0"
                || Environment.GetEnvironmentVariable("DOTNET_ReadyToRun") != "0"
                || Environment.GetEnvironmentVariable("CORECLR_ENABLE_PROFILING") == "1")
                throw new InvalidOperationException("Sampled signals require disabled tiering, ReadyToRun, and startup profiling.");
            if (CpuWorkload.Run() != 49995000L) throw new InvalidOperationException("The sampled signal warmup checksum differs.");
            backend = backendFactory();
            if (captureTargetCpu && backend is not ITargetCpuObservationBackend)
                throw new PlatformNotSupportedException("The intervention backend does not support target CPU observations.");
            workers = Enumerable.Range(0, 2).Select(slot => new Thread(() =>
            {
                bool announced = false;
                SignalTargetLifetime.Run(backend, slot, deliveryStopped, () => !nativeOutstanding.Any(value => value), () =>
                {
                    if (slot == 0) session = sessionFactory(new Sink(Publish));
                    announced = true;
                    ready.Signal();
                    start.Wait();
                    while (!stop.IsSet)
                    {
                        if (CpuWorkload.Run() != 49995000L) throw new InvalidOperationException("The sampled signal checksum differs.");
                        Interlocked.Increment(ref batches[slot]);
                    }
                }, error =>
                {
                    errors[slot] = errors[slot] is null ? error : new AggregateException(errors[slot]!, error);
                    if (!announced) ready.Signal();
                });
            }) { IsBackground = true }).ToArray();
            foreach (var worker in workers) worker.Start();
            if (!ready.Wait(TimeSpan.FromSeconds(5)) || errors.Any(error => error is not null))
                throw new InvalidOperationException("The sampled signal workers failed to become ready.");
            if (plan.ForceGc) gcStress = SampledGcStress.Start();
            if (plan.WarmupMilliseconds > 0)
            {
                var warmBoundary = RunStage("warmup", plan.WarmupMilliseconds);
                var warmSettled = SettleStage("warmup-settlement");
                warmup = new(warmBoundary, warmSettled, controllerCpu, pauses, selectedChecks, (long[])maxDebt.Clone(),
                    longestServiceInterval, peakDebt, runtimeCountersStart, peerChecks, peerCheckSkips);
                state = SignalAccountingModule.create();
                selected = Parsed(SignalAccountingModule.register(FSharpOption<int>.Some(plan.Percent), state));
                peer = Parsed(SignalAccountingModule.register(FSharpOption<int>.None, selected.Item2));
                state = peer.Item2;
                handles = [selected.Item1, peer.Item1];
                stream = Parsed(SignalAccountingModule.sampleIdentity(handles[0], state));
                sequence = represented = 0;
                epoch = 1;
                credited = 0;
                waits = new long[2];
                maxDebt = new long[2];
                peakDebt = new DebtPeak?[2];
                lastAcknowledged = new ulong[2];
                longestServiceInterval = null;
                registered = [true, true];
                cadence = new SelectedSignalCadence(plan.Cadence);
                peerCadence = new PeerSignalCadence();
                pauses = 0;
                selectedChecks = 0;
                peerChecks = peerCheckSkips = 0;
                batchBaseline = [Volatile.Read(ref batches[0]), Volatile.Read(ref batches[1])];
            }
            measurementStartRecords = records;
            measurement = RunStage("measurement", 2000);
            settled = SettleStage("settlement");
        }
        catch (Exception error)
        {
            failureBoundary = Snapshot();
            failures.Add(error.ToString());
        }
        finally
        {
            accept = measuring = runningStage = false;
            gcStress?.RequestStop();
            if (session is not null)
            {
                try { session.Dispose(); }
                catch (Exception error) { failures.Add(error.ToString()); }
            }
            deliveryStopped.Set();
            stop.Set();
            start.Set();
            joined = true;
            foreach (var worker in workers) if (!worker.Join(TimeSpan.FromSeconds(1))) joined = false;
            if (gcStress is not null)
            {
                gcJoined = gcStress.StopAndJoin(TimeSpan.FromSeconds(1));
                if (gcJoined)
                {
                    var gcResult = gcStress.Snapshot();
                    gcObservations = gcResult.Observations;
                    if (gcResult.Error is not null) failures.Add(gcResult.Error);
                }
                else failures.Add("The sampled GC worker did not join.");
            }
            if (backend is not null && joined && !nativeOutstanding.Any(value => value))
            {
                try { backend.RestoreAfterThreadJoin(); restored = true; }
                catch (Exception error) { failures.Add(error.ToString()); }
            }
        }
        foreach (var error in errors) if (error is not null) failures.Add(error.ToString());
        if (!joined || !restored || nativeOutstanding.Any(value => value)) failures.Add("Sampled signal teardown did not finish.");
        if (measurement is null || settled is null || records is null
            || records.SelectedMethodSamples - (measurementStartRecords?.SelectedMethodSamples ?? 0) == 0
            || measurement.Batches.Any(count => count == 0)) failures.Add("The sampled signal workload did not reach its measurement requirements.");
        if (plan.Percent == 0 && (credited != 0 || requests.Count != 0)) failures.Add("Zero speedup created signal debt.");
        if (plan.Percent > 0 && (measurement?.CreditNanoseconds is not > 0 || measurement.WaitNanoseconds[1] == 0))
            failures.Add("Selected credit did not cause peer waits during measurement.");
        if (plan.ForceGc && (gcObservations is null || measurement is null
            || !SampledGcStress.ReachedMeasurement(gcObservations, measurementStartedTimestamp, measurement.Timestamp)))
            failures.Add("No forced collection completed inside the sampled measurement window.");
        if (gcObservations is not null && !SampledGcStress.WithinBounds(gcObservations, Stopwatch.Frequency))
            failures.Add("A forced collection exceeded 250 milliseconds or had invalid timestamps.");
        if (trace is not null)
        {
            try { trace.Mark("run-end"); }
            catch (Exception error) { failures.Add(error.ToString()); }
        }
        bool protocolPassed = failures.Count == 0;
        bool capacityPassed = protocolPassed && maxDebt.All(value => value <= 2000000)
            && measurement!.DebtNanoseconds.All(value => value <= 2000000);
        var capacityViolations = Enumerable.Range(0, 2).Where(slot => maxDebt[slot] > 2000000)
            .Select(slot => $"Slot {slot} exceeded the 2000000 ns observed debt limit: {maxDebt[slot]} ns.").ToArray();
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            backend = backend?.Name, backendDetails = backend?.Describe(), percent = plan.Percent, selectedCadence = plan.Cadence.ToString(), selectedChecks,
            peerChecks, peerCheckSkips,
            runtime = Environment.Version.ToString(),
            runtimeCountersStart, runtimeSettings = new { serverGc = System.Runtime.GCSettings.IsServerGC,
                latencyMode = System.Runtime.GCSettings.LatencyMode.ToString(), processorCount = Environment.ProcessorCount },
            warmupMilliseconds = plan.WarmupMilliseconds, warmup, measurement, settled, failureBoundary, measurementStartRecords,
            boundaries, maxDebtNanoseconds = maxDebt, debtBoundNanoseconds = 2000000,
            stopwatchFrequency = Stopwatch.Frequency, longestServiceInterval, peakDebt,
            traceAnchors = trace?.Anchors, diagnosticTrace = trace is not null,
            controllerCpuTiming = trace is not null,
            targetCpuTiming = captureTargetCpu,
            controllerCpuNanoseconds = controllerCpu, samplingPauses = pauses, records, samples, requests,
            failures, capacityViolations, joined, restored, protocolPassed, capacityPassed, passed = protocolPassed && capacityPassed,
            forcedGc = plan.ForceGc, gcObservations, gcJoined, measurementStartedTimestamp, gcCallBoundMilliseconds = 250,
            gcCallDurationIsSuspensionLatency = false, predictionAccuracyEstablished = false, generalClrSafetyEstablished = false,
            requestBoundNanoseconds = 50000, handlerBoundNanoseconds = 5000000, acknowledgmentBoundMilliseconds = 100
        }, new JsonSerializerOptions { WriteIndented = true }));
        return protocolPassed && capacityPassed ? 0 : 1;
    }
}
