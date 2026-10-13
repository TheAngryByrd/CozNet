using System.Diagnostics;
using System.Numerics;
using System.Collections.Concurrent;
using CozNet;
using Microsoft.FSharp.Core;

sealed record InterventionProgress(ulong AppliedSamples, long CreditedTicks, long WorkerWaitTicks, long GlobalDelayTicks);
sealed record WaitObservation(bool Worker, long RequestedTicks, long ActualTicks, long AccountedTicks);

sealed record InterventionResult(int SpeedupPercent, string WaitBackend, long ProbeCpuIntervalNanoseconds,
    InterventionProgress Live, ulong SelectedPeriodNanoseconds,
    long CreditedTicks, long WorkerWaitTicks, long WorkerAccountedWaitTicks, long WorkerCheckTicks, long WorkerChecks, long WorkerBatches,
    long GlobalDelayTicks, long ClockFrequency, long SelectedCpuClockReads, IReadOnlyList<WaitObservation> Waits);

sealed class SampledIntervention : ICpuSampleSink, IDisposable
{
    private readonly CooperativeRuntime runtime;
    private readonly CooperativeThread selected;
    private readonly Guid stream;
    private readonly string selectedMethod;
    private readonly int percent;
    private readonly string waitBackend;
    private readonly long probeInterval;
    private readonly ProbeCadence selectedCadence;
    private readonly ManualResetEventSlim ready = new();
    private readonly ManualResetEventSlim stop = new();
    private readonly CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
    private readonly Task<(long Ticks, long Checks, long Batches)> worker;
    private readonly ConcurrentQueue<CpuSample> pending = new();
    private readonly ConcurrentQueue<WaitObservation> waits = new();
    private readonly ThreadLocal<WaitState> waitState = new(() => new WaitState());
    private ulong sequence, representedCpu, selectedPeriods;
    private long credited;
    private long workerWaitTicks;
    private long workerAccountedWaitTicks;
    private int workerId;
    private bool completed;

    private sealed class WaitState
    {
        public bool Started;
        public long Start, Requested, Actual;
    }

    private long Timestamp()
    {
        var state = waitState.Value!;
        long now = Stopwatch.GetTimestamp();
        if (!state.Started)
        {
            state.Start = now;
            state.Started = true;
        }
        else
        {
            state.Started = false;
            long accounted = now - state.Start;
            bool isWorker = Environment.CurrentManagedThreadId == Volatile.Read(ref workerId);
            if (isWorker) Interlocked.Add(ref workerAccountedWaitTicks, accounted);
            waits.Enqueue(new WaitObservation(isWorker, state.Requested, state.Actual, accounted));
        }
        return now;
    }

    private static T Parsed<T>(FSharpResult<T, string> result) => result.IsOk
        ? result.ResultValue : throw new InvalidOperationException(result.ErrorValue);

    public SampledIntervention(string selectedMethod, int percent, IWaitBackend backend,
        IThreadCpuClock cpuClock, long probeIntervalNanoseconds)
    {
        probeInterval = probeIntervalNanoseconds;
        selectedCadence = new ProbeCadence(cpuClock, probeInterval);
        waitBackend = backend.Name;
        var timing = DelayRuntimeTimingModule.real;
        var wait = FSharpFunc<DelayTicks, FSharpFunc<CancellationToken, Unit>>.FromConverter(duration =>
            FSharpFunc<CancellationToken, Unit>.FromConverter(token =>
            {
                long start = Stopwatch.GetTimestamp();
                try { backend.Wait(duration, token); return default!; }
                finally
                {
                    long actual = Stopwatch.GetTimestamp() - start;
                    bool isWorker = Environment.CurrentManagedThreadId == Volatile.Read(ref workerId);
                    if (isWorker) Interlocked.Add(ref workerWaitTicks, actual);
                    waitState.Value!.Requested = DelayTicksModule.value(duration);
                    waitState.Value!.Actual = actual;
                }
            }));
        wait.Invoke(Parsed(DelayTicksModule.tryCreate(Stopwatch.Frequency / 1000))).Invoke(CancellationToken.None);
        waits.Clear();
        var timestamp = FSharpFunc<Unit, long>.FromConverter(_ => Timestamp());
        runtime = new CooperativeRuntime(new DelayRuntimeTiming(timing.Frequency, timestamp, wait));
        this.selectedMethod = selectedMethod;
        this.percent = percent;
        selected = Parsed(runtime.Register(null));
        stream = Parsed(runtime.AttachSamples(selected, percent, 0, 0));
        worker = Task.Factory.StartNew(() =>
        {
            Volatile.Write(ref workerId, Environment.CurrentManagedThreadId);
            var handle = Parsed(runtime.Register(null));
            var cadence = new ProbeCadence(cpuClock, probeInterval);
            ready.Set();
            long ticks = 0, checks = 0, batches = 0;
            do
            {
                if (CpuWorkload.Run() != 49995000L)
                    throw new InvalidOperationException("The intervention worker changed the workload checksum.");
                batches++;
                if (!cadence.IsDue()) continue;
                long start = Stopwatch.GetTimestamp();
                Parsed(runtime.Check(handle, deadline.Token));
                ticks = checked(ticks + Stopwatch.GetTimestamp() - start);
                checks++;
            } while (!stop.IsSet);
            Parsed(runtime.Check(handle, deadline.Token));
            Parsed(runtime.Unregister(handle));
            return (ticks, checks, batches);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public void WaitUntilReady()
    {
        if (!ready.Wait(TimeSpan.FromSeconds(5)))
            throw new InvalidOperationException("The intervention worker did not register before sampling.");
    }

    public void Publish(CpuSample sample)
    {
        pending.Enqueue(sample);
    }

    public void Probe()
    {
        if (!selectedCadence.IsDue()) return;
        FlushAndCheck();
    }

    private void FlushAndCheck()
    {
        while (pending.TryDequeue(out var sample)) Apply(sample);
        Parsed(runtime.Check(selected, deadline.Token));
    }

    private void Apply(CpuSample sample)
    {
        sequence = checked(sequence + 1);
        representedCpu = checked(representedCpu + sample.PeriodNanoseconds);
        bool isSelected = sample.Method == selectedMethod;
        if (isSelected) selectedPeriods = checked(selectedPeriods + sample.PeriodNanoseconds);
        var location = isSelected ? SampleLocation.Selected
            : sample.Method is null ? SampleLocation.Unmapped : SampleLocation.Other;
        var observation = Parsed(CpuSampleObservationModule.tryCreate(stream, sequence,
            representedCpu, 0, SampleThreadState.Runnable, location));
        credited = checked(credited + DelayTicksModule.value(Parsed(runtime.CreditObservation(selected, observation))));
    }

    public InterventionProgress ObserveLive() => new(sequence, credited, Interlocked.Read(ref workerWaitTicks),
        DelayTicksModule.value(runtime.GlobalDelay));

    public InterventionResult Complete(InterventionProgress live)
    {
        FlushAndCheck();
        stop.Set();
        var result = worker.GetAwaiter().GetResult();
        Parsed(runtime.Check(selected, deadline.Token));
        Parsed(runtime.Unregister(selected));
        completed = true;
        long expected = (long)((BigInteger)selectedPeriods * Stopwatch.Frequency * percent / 100000000000L);
        long global = DelayTicksModule.value(runtime.GlobalDelay);
        if (credited != expected || global < credited || (percent == 0 && global != 0))
            throw new InvalidOperationException("Live sample credit does not match the selected event periods.");
        if (result.Checks == 0 || result.Batches == 0 || (percent > 0 && credited == 0))
            throw new InvalidOperationException("The sampled intervention did not reach work, probes, and credits.");
        if (workerAccountedWaitTicks < credited || (percent == 0 && (workerWaitTicks != 0 || workerAccountedWaitTicks != 0)))
            throw new InvalidOperationException($"The worker probes did not settle sampled credit: accounted={workerAccountedWaitTicks}, backend={workerWaitTicks}, credit={credited}.");
        if (live.AppliedSamples == 0 || (percent > 0 && (live.CreditedTicks == 0 || live.WorkerWaitTicks == 0)))
            throw new InvalidOperationException("No sampled credit and worker wait were observed before sampling stopped.");
        return new InterventionResult(percent, waitBackend, probeInterval, live, selectedPeriods, credited, workerWaitTicks, workerAccountedWaitTicks, result.Ticks,
            result.Checks, result.Batches, global, Stopwatch.Frequency, selectedCadence.CpuClockReads, waits.ToArray());
    }

    public void Dispose()
    {
        try
        {
            if (!completed)
            {
                stop.Set();
                worker.GetAwaiter().GetResult();
                Parsed(runtime.Check(selected, deadline.Token));
                Parsed(runtime.Unregister(selected));
            }
        }
        finally
        {
            ready.Dispose();
            stop.Dispose();
            deadline.Dispose();
            waitState.Dispose();
        }
    }
}
