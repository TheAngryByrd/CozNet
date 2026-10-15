using System.Diagnostics;
using System.Text.Json;
using CozNet;
using Microsoft.FSharp.Core;

static class SampledTimerWorkload
{
    private sealed class Sink(Action<CpuSample> publish) : ICpuSampleSink
    {
        public void Publish(CpuSample sample) => publish(sample);
    }
    private sealed record SampleRecord(CpuSample Sample, ulong Epoch, bool Accepted, long Credit, long Timestamp);
    private sealed record Boundary(long Timestamp, long Global, long Credit, long[] Waits, long[] ActiveWaits, long[] Debt);
    private sealed record Initialization(string Source, SampleRecord[] Samples, TimerServiceController.Journal[] Journals, Boundary Boundary);

    public static int Run(string path, int percent, Func<ICpuSampleSink, ICpuEventSession> sessionFactory,
        IThreadCpuClock? phaseClock = null, bool initializeManagedPaths = false, bool initializeNativeTransport = false,
        TimerInitialInterval? initialInterval = null, bool forceGc = false,
        Func<ISchedulerEventSession>? schedulerFactory = null)
    {
        if (percent is not (0 or 50)) throw new ArgumentOutOfRangeException(nameof(percent));
        var failures = new List<Exception>();
        var phases = phaseClock is null ? null : new ControllerPhaseCapture(phaseClock, retainRecent: true);
        var samples = new List<SampleRecord>();
        var boundaries = new List<Boundary>();
        var lifetimes = new[] { new TimerWorkerLifetime(), new TimerWorkerLifetime() };
        var ready = new[] { new ManualResetEventSlim(), new ManualResetEventSlim() };
        var start = new ManualResetEventSlim();
        var scheduling = new TimerSchedulerCaptureGroup(schedulerFactory);
        TimerSchedulerCaptureGroup.Completion? schedulerCompletion = null;
        Thread?[] workers = new Thread?[2];
        long[] progress = new long[2], peaks = new long[2];
        int stop = 0;
        NativeTimerClient? client = null;
        TimerServiceController? controller = null;
        ICpuEventSession? session = null;
        SampleBatch? records = null;
        bool enabled = false, accept = false, running = false, closed = false, stableMap = false;
        ulong sequence = 0, represented = 0, epoch = 1;
        long credit = 0;
        string stage = "setup";
        var state = SignalAccountingModule.create();
        var selected = Parsed(SignalAccountingModule.register(FSharpOption<int>.Some(percent), state));
        var peer = Parsed(SignalAccountingModule.register(FSharpOption<int>.None, selected.Item2));
        state = peer.Item2;
        LedgerThread[] handles = [selected.Item1, peer.Item1];
        Guid stream = Parsed(SignalAccountingModule.sampleIdentity(handles[0], state));
        var cadence = new SelectedSignalCadence(SelectedSignalCadenceMode.QualifiedSample);
        ulong begin = 0, size = 0;
        Boundary? failureBoundary = null;
        string? failureStage = null;
        Initialization? initialization = null;
        TimerTransportPreparation.Result? transportInitialization = null;
        int iteration = 0;
        SampledGcStress? gcStress = null;
        GcStressObservation[]? gcObservations = null;
        string? gcError = null;
        bool gcJoined = true, gcReachedMeasurement = false;
        long measurementStartedTimestamp = 0, measurementFinishedTimestamp = 0;
        long[]? measurementProgress = null;
        long progressCapturedBeforeTimestamp = 0, progressCapturedAfterTimestamp = 0;
        var measurement = new MeasurementWindowCapture(TimeProvider.System);

        MeasurementWindowCapture.Readings ReadMeasurement()
        {
            var pending = controller?.PendingJournals ?? [];
            MeasurementWindowCapture.Worker ReadWorker(uint slot)
            {
                var operation = pending.SingleOrDefault(journal => journal.Slot == slot);
                return new(Interlocked.Read(ref progress[slot]), ReleasedWait(slot), controller?.ConfirmedWait(slot) ?? 0,
                    operation is null ? null : new(operation.Command, operation.Generation));
            }
            long global = SignalAccountingModule.globalNanoseconds(state);
            var selectedWorker = ReadWorker(0);
            var peerWorker = ReadWorker(1);
            return new(global, credit, sequence, epoch, selectedWorker, peerWorker);
        }

        void CaptureProgress()
        {
            if (measurement.Start is null) return;
            accept = running = false;
            measurement.Finish(ReadMeasurement);
            if (measurement.End is not { } end) return;
            progressCapturedBeforeTimestamp = end.BeforeTimestamp;
            measurementProgress = [end.Accounting.Selected.Progress, end.Accounting.Peer.Progress];
            progressCapturedAfterTimestamp = end.AfterTimestamp;
        }

        void Publish(CpuSample sample)
        {
            Require(samples.Count < 20000, "The sampled timer record limit was exceeded.");
            long amount = 0;
            if (accept)
            {
                sequence = checked(sequence + 1);
                represented = checked(represented + sample.PeriodNanoseconds);
                var location = sample.Method == OverheadWorkload.SelectedSymbol ? SampleLocation.Selected
                    : sample.Method is null ? SampleLocation.Unmapped : SampleLocation.Other;
                var observation = Parsed(CpuSampleObservationModule.tryCreate(stream, sequence, represented,
                    epoch, SampleThreadState.Runnable, location));
                var observed = Parsed(SignalAccountingModule.observe(handles[0], observation, state));
                state = observed.Item2;
                amount = DelayTicksModule.value(observed.Item1);
                credit = checked(credit + amount);
                cadence.Observe(epoch);
            }
            samples.Add(new(sample, epoch, accept, amount, Stopwatch.GetTimestamp()));
        }
        void Drain()
        {
            records = session!.Drain();
            Require(records.LostRecords == 0 && records.LostSamples == 0 && records.Throttles == 0,
                "The sampled timer source lost or throttled events.");
        }
        void Disable()
        {
            accept = false;
            if (enabled) { session!.Disable(); enabled = false; }
            Drain();
        }
        void Poll(uint slot)
        {
            var result = controller!.Poll(slot, ref state, stage, iteration);
            if (!result.Released) return;
            if (slot == 0 && running)
            {
                session!.Enable();
                enabled = accept = true;
            }
        }
        void Prepare(uint slot)
        {
            if (controller!.HasPending(slot)) return;
            var reserved = slot == 0 ? Parsed(SignalAccountingModule.prepareSelectedService(handles[slot], state))
                : Parsed(SignalAccountingModule.prepareService(handles[slot], state));
            state = reserved.Item2;
            if (reserved.Item1 is null) return;
            if (slot == 0)
            {
                Disable();
                epoch = Parsed(SignalAccountingModule.requiredEpoch(handles[0], state));
            }
            controller.Submit(slot, reserved.Item1.Value, ref state, stage, iteration);
        }
        long ReleasedWait(uint slot) => controller?.ReleasedWait(slot) ?? 0;

        Boundary Snapshot()
        {
            long global = SignalAccountingModule.globalNanoseconds(state);
            long[] active = [controller?.ConfirmedWait(0) ?? 0, controller?.ConfirmedWait(1) ?? 0];
            long[] waits = [ReleasedWait(0), ReleasedWait(1)];
            long[] debt = [Math.Max(0, global - credit - waits[0] - active[0]), Math.Max(0, global - waits[1] - active[1])];
            return new(Stopwatch.GetTimestamp(), global, credit, waits, active, debt);
        }
        (long Selected, long Peer) Debt()
        {
            long global = SignalAccountingModule.globalNanoseconds(state);
            return (Math.Max(0, global - credit - ReleasedWait(0) - controller!.ConfirmedWait(0)),
                Math.Max(0, global - ReleasedWait(1) - controller.ConfirmedWait(1)));
        }
        void Initialize()
        {
            stage = "synthetic-initialization";
            selected = Parsed(SignalAccountingModule.register(FSharpOption<int>.Some(50), SignalAccountingModule.create()));
            peer = Parsed(SignalAccountingModule.register(FSharpOption<int>.None, selected.Item2));
            state = peer.Item2;
            handles = [selected.Item1, peer.Item1];
            stream = Parsed(SignalAccountingModule.sampleIdentity(handles[0], state));
            epoch = 0;
            using var source = new ServicePathPreparation(new Sink(Publish));
            session = source;
            controller = new(new TimerPathPreparation(), handles[0], handles[1], 100, 20, TimeProvider.System);
            source.Enable();
            enabled = accept = true;
            Drain();
            Prepare(0);
            Prepare(1);
            Poll(1);
            Prepare(1);
            Prepare(0);
            Poll(0);
            Prepare(0);
            var boundary = Snapshot();
            Require(credit == 40000 && ReleasedWait(0) == 30000 && ReleasedWait(1) == 70000
                && boundary.Debt.All(debt => debt == 0) && controller.Journals.Count == 2
                && controller.PendingJournals.Length == 0 && SignalAccountingModule.pendingCount(state) == 0,
                "Synthetic timer initialization did not settle its fixed credit and waits.");
            initialization = new("synthetic", samples.ToArray(), controller.Journals.ToArray(), boundary);
            selected = Parsed(SignalAccountingModule.register(FSharpOption<int>.Some(percent), SignalAccountingModule.create()));
            peer = Parsed(SignalAccountingModule.register(FSharpOption<int>.None, selected.Item2));
            state = peer.Item2;
            handles = [selected.Item1, peer.Item1];
            stream = Parsed(SignalAccountingModule.sampleIdentity(handles[0], state));
            samples.Clear();
            records = null;
            session = null;
            controller = null;
            sequence = represented = 0;
            epoch = 1;
            credit = 0;
            accept = enabled = false;
            cadence = new SelectedSignalCadence(SelectedSignalCadenceMode.QualifiedSample);
            stage = "setup";
        }
        try
        {
            Require(OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") == "0"
                && Environment.GetEnvironmentVariable("DOTNET_ReadyToRun") == "0"
                && Environment.GetEnvironmentVariable("CORECLR_ENABLE_PROFILING") != "1",
                "Sampled timer work requires Linux, disabled tiering and ReadyToRun, and no startup profiler.");
            if (initializeManagedPaths) Initialize();
            Require(CpuWorkload.Run() == 49995000L, "Sampled timer warmup changed the checksum.");
            string mapPath = $"/tmp/perf-{Environment.ProcessId}.map";
            File.Copy(mapPath, path + ".initial.perf.map");
            (begin, size) = PerfMapSnapshot.Read(mapPath).UniqueRange(OverheadWorkload.SelectedSymbol);
            client = new(begin, size, targets: initializeNativeTransport ? 3U : 2U, capacity: 64, initialInterval: initialInterval);
            if (initializeNativeTransport)
            {
                transportInitialization = TimerTransportPreparation.Run(client, 2, begin, size);
                Require(transportInitialization.Passed, "The dedicated native timer initialization failed.");
            }
            controller = new(client, handles[0], handles[1], begin, size, TimeProvider.System, phases);
            scheduling.Register(0);
            for (int index = 0; index < 2; index++)
            {
                int slot = index;
                workers[slot] = new Thread(() => lifetimes[slot].Run(
                    () =>
                    {
                        client.RegisterCurrentThread((uint)slot);
                        scheduling.Register(slot + 1);
                        if (slot == 0) session = sessionFactory(new Sink(Publish));
                    }, ready[slot].Set,
                    () =>
                    {
                        Require(start.Wait(TimeSpan.FromSeconds(5)), "The sampled timer start gate timed out.");
                        while (Volatile.Read(ref stop) == 0)
                        {
                            Require(CpuWorkload.Run() == 49995000L, "A sampled timer intervention changed the checksum.");
                            Interlocked.Increment(ref progress[slot]);
                        }
                    }, () => scheduling.Retire(() => client.RetireCurrentThread((uint)slot), TimeSpan.FromSeconds(5)),
                    () => Thread.Sleep(Timeout.Infinite))) { IsBackground = true };
                workers[slot]!.Start();
            }
            foreach (var signal in ready) Require(signal.Wait(TimeSpan.FromSeconds(2)), "A sampled timer worker did not register.");
            Require(session is not null && lifetimes.All(lifetime => lifetime.Error is null), "The sampled timer workers failed setup.");
            if (forceGc) gcStress = SampledGcStress.Start();
            stage = "measurement";
            phases?.Begin("setup", "phase-clock", 0);
            phases?.End();
            var timer = new Stopwatch();
            measurement.Begin(ReadMeasurement, () =>
            {
                running = true;
                session!.Enable();
                enabled = accept = true;
                timer.Start();
                measurementStartedTimestamp = Stopwatch.GetTimestamp();
                start.Set();
                gcStress?.Release();
            });
            long nextBoundary = 0;
            while (timer.ElapsedMilliseconds < 2000)
            {
                iteration++;
                Require(lifetimes.All(lifetime => lifetime.Error is null), "A sampled timer worker failed.");
                phases?.Begin(stage, "drain", iteration);
                int samplesBefore = samples.Count;
                Drain();
                phases?.End(retain: iteration == 1 || samples.Count != samplesBefore);
                Poll(0);
                Poll(1);
                if (cadence.TryCheck()) Prepare(0);
                Prepare(1);
                var debt = Debt();
                bool newPeak = debt.Selected > peaks[0] || debt.Peer > peaks[1];
                peaks[0] = Math.Max(peaks[0], debt.Selected);
                peaks[1] = Math.Max(peaks[1], debt.Peer);
                if (timer.ElapsedMilliseconds >= nextBoundary || newPeak || !TimerDebtLimit.Allows(debt.Selected, debt.Peer))
                {
                    boundaries.Add(Snapshot());
                    nextBoundary = timer.ElapsedMilliseconds + 10;
                }
                Require(TimerDebtLimit.Allows(debt.Selected, debt.Peer), "Sampled timer debt exceeded 2000000 ns.");
            }
            measurementFinishedTimestamp = Stopwatch.GetTimestamp();
            CaptureProgress();
            Require(samples.Any(sample => sample.Accepted && sample.Sample.Method == OverheadWorkload.SelectedSymbol),
                "No selected-method CPU samples reached the timer workload.");
            Require(percent == 0 ? credit == 0 && controller.Journals.Count == 0 : credit > 0,
                "The sampled timer speedup did not produce the expected credit.");
            File.Copy(mapPath, path + ".final.perf.map");
            stableMap = PerfMapSnapshot.Read(mapPath).UniqueRange(OverheadWorkload.SelectedSymbol) == (begin, size);
            Require(stableMap, "The selected method changed its native instruction range.");
        }
        catch (Exception error)
        {
            if (measurementStartedTimestamp != 0 && measurementFinishedTimestamp == 0)
                measurementFinishedTimestamp = Stopwatch.GetTimestamp();
            failures.Add(error);
            try { CaptureProgress(); } catch (Exception captureError) { failures.Add(captureError); }
            try { phases?.End(false); } catch (Exception phaseError) { failures.Add(phaseError); }
            failureStage = stage;
            failureBoundary = Snapshot();
            boundaries.Add(failureBoundary);
            for (int slot = 0; slot < 2; slot++) peaks[slot] = Math.Max(peaks[slot], failureBoundary.Debt[slot]);
        }
        finally
        {
            running = false;
            stage = "shutdown";
            gcStress?.RequestStop();
            schedulerCompletion = scheduling.Finish();
            failures.AddRange(schedulerCompletion.Errors);
            if (session is not null)
                try { Disable(); } catch (Exception error) { failures.Add(error); }
            if (controller is not null)
                for (uint slot = 0; slot < 2; slot++)
                {
                    try { Poll(slot); } catch (Exception error) { failures.Add(error); }
                    if (controller.HasPending(slot) && !controller.PendingJournals.Single(journal => journal.Slot == slot).LedgerSettled)
                        try { controller.Cancel(slot, ref state); } catch (Exception error) { failures.Add(error); }
                }
            Volatile.Write(ref stop, 1);
            start.Set();
            for (int slot = 0; slot < 2; slot++)
            {
                int target = slot;
                if (workers[slot] is null) continue;
                try
                {
                    lifetimes[slot].StopAndJoin(() => { }, () => workers[target]!.Join(TimeSpan.FromSeconds(2)),
                        ready[slot].Dispose, () =>
                        {
                            if (controller!.HasPending((uint)target)) Poll((uint)target);
                            Require(!controller.HasPending((uint)target), "Retired timer ownership remains unresolved.");
                        });
                }
                catch (Exception error) { failures.Add(error); }
            }
            if (gcStress is not null)
            {
                try
                {
                    gcJoined = gcStress.StopAndJoin(TimeSpan.FromSeconds(2));
                    if (gcJoined) (gcObservations, gcError) = gcStress.Snapshot();
                    else failures.Add(new InvalidDataException("The sampled timer GC worker did not join. Observations remain unavailable."));
                    if (gcError is not null) failures.Add(new InvalidDataException(gcError));
                }
                catch (Exception error) { gcJoined = false; failures.Add(error); }
            }
            if (client is not null && (transportInitialization is null || transportInitialization.Released)
                && workers.Select((worker, slot) => worker is null || lifetimes[slot].Joined && lifetimes[slot].Released).All(safe => safe))
            {
                var cleanup = TimerSessionCleanup.Run(session, client.CloseAfterThreadJoin, start.Dispose);
                closed = cleanup.NativeClosed;
                failures.AddRange(cleanup.Errors);
            }
        }
        boundaries.Add(Snapshot());
        if (forceGc)
        {
            gcReachedMeasurement = gcObservations is not null
                && SampledGcStress.ReachedMeasurement(gcObservations, measurementStartedTimestamp, measurementFinishedTimestamp);
            if (!gcReachedMeasurement) failures.Add(new InvalidDataException("No complete forced collection occurred inside sampled timer measurement."));
            if (gcObservations is null || !SampledGcStress.WithinBounds(gcObservations, Stopwatch.Frequency))
                failures.Add(new InvalidDataException("Sampled timer collection timing did not satisfy the 250 ms call bound."));
        }
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            percent, failures = failures.Select(error => error.ToString()), failureStage, failureBoundary, samples, boundaries, peaks, progress, waits = new[] { ReleasedWait(0), ReleasedWait(1) }, credit, records, stableMap, closed,
            lifetimes = lifetimes.Select(lifetime => new { lifetime.Joined, lifetime.Retired, lifetime.Released, error = lifetime.Error?.ToString() }),
            journals = controller?.Journals, pending = controller?.PendingJournals,
            controllerPhases = phases?.Entries,
            controllerPhasesOverwritten = phases?.Overwritten,
            initialization,
            transportInitialization,
            initialIntervalNanoseconds = (initialInterval ?? TimerInitialInterval.Default).Nanoseconds,
            forceGc, gcObservations, gcError, gcJoined, gcReachedMeasurement, gcCallBoundMilliseconds = 250,
            measurementStartedTimestamp, measurementFinishedTimestamp,
            measurementCapture = new { status = measurement.Status.ToString(), start = measurement.Start, end = measurement.End },
            measurementProgress, progressCapturedBeforeTimestamp, progressCapturedAfterTimestamp,
            schedulerTiming = schedulerFactory is not null, schedulerStopped = schedulerCompletion?.Stopped,
            schedulerCaptures = schedulerCompletion?.Captures,
            timestampFrequency = Stopwatch.Frequency, predictionAccuracyEstablished = false
        }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        return failures.Count == 0 && closed ? 0 : 1;
    }
    private static T Parsed<T>(FSharpResult<T, string> result) => result.IsOk ? result.ResultValue : throw new InvalidDataException(result.ErrorValue);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
