using System.Diagnostics;
using System.Text.Json;
using CozNet;
using Microsoft.FSharp.Core;

sealed class SampledSignalPlan
{
    public int Percent { get; }
    private SampledSignalPlan(int percent) => Percent = percent;
    public static SampledSignalPlan Parse(string value) => new(value switch
    {
        "0" => 0, "50" => 50,
        _ => throw new ArgumentException("Select zero or 50 percent sampled signal speedup.")
    });
}

static class SampledSignalWorkload
{
    private sealed record Pending(SignalDelayRequest Request, ulong Sequence, long Started);
    private sealed record RequestRecord(int Slot, ulong Sequence, long RequestedNanoseconds,
        double AcknowledgmentMilliseconds, SignalAcknowledgment Result, bool DuringMeasurement);
    private sealed record SampleRecord(CpuSample Sample, ulong Epoch, bool Accepted, long CreditNanoseconds);
    private sealed record Boundary(double Milliseconds, long GlobalNanoseconds, long CreditNanoseconds,
        long[] WaitNanoseconds, long[] DebtNanoseconds, bool[] Registered, int Pending, long[] Batches);
    private sealed class Sink(Action<CpuSample> publish) : ICpuSampleSink
    {
        public void Publish(CpuSample sample) => publish(sample);
    }
    private static T Parsed<T>(FSharpResult<T, string> result) =>
        result.IsOk ? result.ResultValue : throw new InvalidOperationException(result.ErrorValue);

    public static int Run(string path, SampledSignalPlan plan,
        Func<IBoundedInterventionBackend> backendFactory,
        Func<ICpuSampleSink, ICpuEventSession> sessionFactory, IThreadCpuClock controllerClock)
    {
        var failures = new List<string>();
        var requests = new List<RequestRecord>();
        var samples = new List<SampleRecord>();
        var boundaries = new List<Boundary>();
        var errors = new Exception?[2];
        long[] batches = new long[2], waits = new long[2], maxDebt = new long[2];
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
        bool enabled = false, accept = false, measuring = false, joined = false, restored = false;
        ulong sequence = 0, represented = 0, epoch = 0;
        long credited = 0, controllerCpu = 0;
        int pauses = 0;
        SampleBatch? records = null;
        Boundary? measurement = null, settled = null;
        var timer = new Stopwatch();
        var state = SignalAccountingModule.create();
        var selected = Parsed(SignalAccountingModule.register(FSharpOption<int>.Some(plan.Percent), state));
        state = selected.Item2;
        var peer = Parsed(SignalAccountingModule.register(FSharpOption<int>.None, state));
        state = peer.Item2;
        LedgerThread[] handles = [selected.Item1, peer.Item1];
        Guid stream = Parsed(SignalAccountingModule.sampleIdentity(handles[0], state));

        Boundary Snapshot()
        {
            long global = SignalAccountingModule.globalNanoseconds(state);
            long[] debt = [registered[0] ? Math.Max(0, global - credited - waits[0]) : 0,
                registered[1] ? Math.Max(0, global - waits[1]) : 0];
            return new(timer.Elapsed.TotalMilliseconds, global, credited, (long[])waits.Clone(), debt,
                (bool[])registered.Clone(), SignalAccountingModule.pendingCount(state), [Volatile.Read(ref batches[0]), Volatile.Read(ref batches[1])]);
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
            }
            samples.Add(new(sample, epoch, accept, amount));
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
            double elapsed = Stopwatch.GetElapsedTime(operation.Started).TotalMilliseconds;
            if (result is null)
            {
                if (elapsed > 100) throw new TimeoutException("A sampled signal acknowledgment exceeded 100 milliseconds.");
                return;
            }
            nativeOutstanding[slot] = false;
            requests.Add(new(slot, operation.Sequence, SignalDelayRequestModule.nanoseconds(operation.Request), elapsed, result, measuring));
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
            if (slot == 0 && measuring)
            {
                session!.Enable();
                enabled = true;
                accept = true;
            }
        }
        bool Prepare(int slot)
        {
            if (pending[slot] is not null) return false;
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
            long started = Stopwatch.GetTimestamp();
            nativeOutstanding[slot] = true;
            ulong nativeSequence = backend!.Request(slot, SignalPlan.FromRequest(request));
            pending[slot] = new(request, nativeSequence, started);
            state = Parsed(SignalAccountingModule.sent(request, nativeSequence, state));
            return false;
        }

        try
        {
            if (Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") != "0"
                || Environment.GetEnvironmentVariable("DOTNET_ReadyToRun") != "0"
                || Environment.GetEnvironmentVariable("CORECLR_ENABLE_PROFILING") == "1")
                throw new InvalidOperationException("Sampled signals require disabled tiering, ReadyToRun, and startup profiling.");
            if (CpuWorkload.Run() != 49995000L) throw new InvalidOperationException("The sampled signal warmup checksum differs.");
            backend = backendFactory();
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
            accept = measuring = true;
            session!.Enable();
            enabled = true;
            controllerCpu = controllerClock.ReadNanoseconds();
            timer.Start();
            start.Set();
            double nextBoundary = 10;
            while (timer.ElapsedMilliseconds < 2000)
            {
                Drain();
                Complete(0);
                Complete(1);
                Prepare(0);
                Prepare(1);
                long global = SignalAccountingModule.globalNanoseconds(state);
                maxDebt[0] = Math.Max(maxDebt[0], Math.Max(0, global - credited - waits[0]));
                maxDebt[1] = Math.Max(maxDebt[1], Math.Max(0, global - waits[1]));
                if (timer.Elapsed.TotalMilliseconds >= nextBoundary)
                {
                    var current = Snapshot();
                    boundaries.Add(current);
                    nextBoundary = current.Milliseconds + 10;
                }
            }
            measurement = Snapshot();
            controllerCpu = controllerClock.ReadNanoseconds() - controllerCpu;
            measuring = accept = false;
            if (enabled) { session.Disable(); enabled = false; }
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
            settled = Snapshot();
        }
        catch (Exception error) { failures.Add(error.ToString()); }
        finally
        {
            accept = measuring = false;
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
            if (backend is not null && joined && !nativeOutstanding.Any(value => value))
            {
                try { backend.RestoreAfterThreadJoin(); restored = true; }
                catch (Exception error) { failures.Add(error.ToString()); }
            }
        }
        foreach (var error in errors) if (error is not null) failures.Add(error.ToString());
        if (!joined || !restored || nativeOutstanding.Any(value => value)) failures.Add("Sampled signal teardown did not finish.");
        if (measurement is null || settled is null || records is null || records.SelectedMethodSamples == 0
            || measurement.Batches.Any(count => count == 0)) failures.Add("The sampled signal workload did not reach its measurement requirements.");
        if (plan.Percent == 0 && (credited != 0 || requests.Count != 0)) failures.Add("Zero speedup created signal debt.");
        if (plan.Percent > 0 && (measurement?.CreditNanoseconds is not > 0 || measurement.WaitNanoseconds[1] == 0))
            failures.Add("Selected credit did not cause peer waits during measurement.");
        bool protocolPassed = failures.Count == 0;
        bool capacityPassed = protocolPassed && maxDebt.All(value => value <= 2000000)
            && measurement!.DebtNanoseconds.All(value => value <= 2000000);
        var capacityViolations = Enumerable.Range(0, 2).Where(slot => maxDebt[slot] > 2000000)
            .Select(slot => $"Slot {slot} exceeded the 2000000 ns observed debt limit: {maxDebt[slot]} ns.").ToArray();
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            backend = backend?.Name, percent = plan.Percent, runtime = Environment.Version.ToString(),
            measurement, settled, boundaries, maxDebtNanoseconds = maxDebt, debtBoundNanoseconds = 2000000,
            controllerCpuNanoseconds = controllerCpu, samplingPauses = pauses, records, samples, requests,
            failures, capacityViolations, joined, restored, protocolPassed, capacityPassed, passed = protocolPassed && capacityPassed,
            forcedGc = false, predictionAccuracyEstablished = false, generalClrSafetyEstablished = false,
            requestBoundNanoseconds = 50000, handlerBoundNanoseconds = 5000000, acknowledgmentBoundMilliseconds = 100
        }, new JsonSerializerOptions { WriteIndented = true }));
        return protocolPassed && capacityPassed ? 0 : 1;
    }
}
