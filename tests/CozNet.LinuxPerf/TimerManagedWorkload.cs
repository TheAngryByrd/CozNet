using System.Diagnostics;
using System.Text.Json;
using CozNet;
using Microsoft.FSharp.Core;

static class TimerManagedWorkload
{
    private sealed record CommandRecord(int Step, ulong Generation, long Budget, long ProgressBefore, long ProgressAfter,
        long Confirmed, bool Settled, TimerCommandInterval Interval, TimerObservation[] Snapshots);

    private static T Parsed<T>(FSharpResult<T, string> value) => value.IsOk ? value.ResultValue : throw new InvalidDataException(value.ErrorValue);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private static SignalAccounting Credit(LedgerThread selected, int step, ulong cpuCreditPerStep, SignalAccounting state)
    {
        var identity = Parsed(SignalAccountingModule.sampleIdentity(selected, state));
        var epoch = Parsed(SignalAccountingModule.requiredEpoch(selected, state));
        var baseline = Parsed(CpuSampleObservationModule.tryCreate(identity, (ulong)step * 2 - 1,
            (ulong)(step - 1) * cpuCreditPerStep, epoch, SampleThreadState.Runnable, SampleLocation.Selected));
        state = Parsed(SignalAccountingModule.observe(selected, baseline, state)).Item2;
        var sample = Parsed(CpuSampleObservationModule.tryCreate(identity, (ulong)step * 2, (ulong)step * cpuCreditPerStep,
            epoch, SampleThreadState.Runnable, SampleLocation.Selected));
        var observed = Parsed(SignalAccountingModule.observe(selected, sample, state));
        return observed.Item2;
    }

    private static (ulong Start, ulong Size) CaptureMap(string output)
    {
        File.WriteAllText(output, File.ReadAllText($"/tmp/perf-{Environment.ProcessId}.map"));
        return PerfMapSnapshot.Read(output).UniqueRange(OverheadWorkload.SelectedSymbol);
    }

    public static int Run(string path, bool forceGc = false)
        => Run(path, TimerWorkloadPlan.Arithmetic(forceGc), null);

    public static int Run(string path, TimerWorkloadPlan plan, Func<ISignalTransitionOperations>? operationsFactory)
    {
        ArgumentNullException.ThrowIfNull(plan);
        bool forceGc = plan.ForceGc;
        var records = new List<CommandRecord>();
        var snapshots = new List<TimerObservation>();
        var skippedSteps = new List<int>();
        NativeTimerClient? client = null;
        TimerSelectedWorker? selectedWorker = null;
        ISignalTransitionOperations? operations = null;
        var addresses = new Dictionary<string, int>();
        Thread? target = null;
        SignalAccounting? accounting = null;
        SampledGcStress? gc = null;
        GcStressObservation[]? collections = null;
        string? gcError = null;
        bool gcJoined = false, gcReleased = false, gcReachedCommand = false;
        Exception? targetFailure = null;
        string? failure = null;
        var ready = new ManualResetEventSlim();
        var unblock = new ManualResetEventSlim(false, 0);
        int stop = 0;
        int enteringManagedWait = 0;
        bool stableWaitReached = false;
        long progress = 0;
        ulong outstanding = 0;
        TimerCommandInterval? unfinishedInterval = null;
        ulong start = 0, size = 0;
        bool joined = false, closed = false, stableMap = false;
        int completedLeases = 0, chainedLeases = 0, paidChildren = 0;
        try
        {
            Require(plan.Scenario.HasValue == (operationsFactory is not null), "The timer plan and transition operations do not match.");
            Require(OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") == "0"
                && Environment.GetEnvironmentVariable("DOTNET_ReadyToRun") == "0"
                && Environment.GetEnvironmentVariable("CORECLR_ENABLE_PROFILING") != "1",
                "Managed timer work requires Linux, disabled tiering and ReadyToRun, and no startup profiler.");
            Require(CpuWorkload.Run() == 49995000L, "Managed timer warmup changed the arithmetic checksum.");
            (start, size) = CaptureMap(path + ".initial.perf.map");
            operations = operationsFactory?.Invoke();
            client = new NativeTimerClient(start, size, targets: 2, capacity: 64);
            selectedWorker = new TimerSelectedWorker(client, start, size);
            selectedWorker.AwaitReady();
            var worker = new Thread(() =>
            {
                try
                {
                    client.RegisterCurrentThread(0);
                    ready.Set();
                    if (plan.Scenario == SignalScenario.NativeWait)
                    {
                        operations!.WaitInNative();
                        Interlocked.Increment(ref progress);
                    }
                    else if (plan.Scenario == SignalScenario.ManagedWait)
                    {
                        Volatile.Write(ref enteringManagedWait, 1);
                        if (!unblock.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("The managed timer wait did not receive release.");
                        Interlocked.Increment(ref progress);
                    }
                    else
                    {
                        long thread = operations?.NativeHop() ?? 0;
                        while (Volatile.Read(ref stop) == 0)
                        {
                            if (plan.Scenario == SignalScenario.Jit) SignalTransitionWorkload.CompileAndRun();
                            else
                            {
                                if (CpuWorkload.Run() != 49995000L) throw new InvalidDataException("A timer intervention changed the arithmetic checksum.");
                                if (plan.Scenario == SignalScenario.Transitions)
                                    for (int hop = 0; hop < 32; hop++)
                                        if (operations!.NativeHop() != thread) throw new InvalidDataException("A timer transition changed the native thread identity.");
                            }
                            Interlocked.Increment(ref progress);
                        }
                    }
                }
                catch (Exception error) { Volatile.Write(ref targetFailure, error); }
                finally
                {
                    ready.Set();
                    try { client.RetireCurrentThread(0); }
                    catch (Exception error)
                    {
                        Volatile.Write(ref targetFailure, error);
                        Thread.Sleep(Timeout.Infinite);
                    }
                }
            }) { IsBackground = true };
            worker.Start();
            target = worker;
            Require(ready.Wait(TimeSpan.FromSeconds(2)) && Volatile.Read(ref targetFailure) is null,
                "The managed timer target did not register.");
            if (plan.IsBlockedWait)
            {
                var reach = Stopwatch.StartNew();
                do
                {
                    stableWaitReached = plan.Scenario == SignalScenario.NativeWait ? operations!.NativeWaitEntered
                        : Volatile.Read(ref enteringManagedWait) == 1 && (worker.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0;
                    Require(Volatile.Read(ref targetFailure) is null, "The timer target failed before reaching its controlled wait.");
                    if (!stableWaitReached) Thread.Yield();
                } while (!stableWaitReached && reach.Elapsed < TimeSpan.FromSeconds(1));
                Require(stableWaitReached, "The timer target did not reach its controlled wait.");
            }
            var selected = Parsed(SignalAccountingModule.register(FSharpOption<int>.Some(50), SignalAccountingModule.create()));
            var peer = Parsed(SignalAccountingModule.register(FSharpOption<int>.None, selected.Item2));
            accounting = peer.Item2;
            if (forceGc) gc = SampledGcStress.Start();
            for (int step = 1; step <= plan.CreditSteps; step++)
            {
                Require(Volatile.Read(ref targetFailure) is null, "The managed timer target failed before submission.");
                accounting = Credit(selected.Item1, step, plan.CpuCreditPerStep, accounting);
                selectedWorker.Pay(selected.Item1, ref accounting);
                var reserved = Parsed(SignalAccountingModule.prepareService(peer.Item1, accounting));
                accounting = reserved.Item2;
                if (FSharpOption<SignalServiceLease>.get_IsNone(reserved.Item1))
                {
                    skippedSteps.Add(step);
                    continue;
                }
                var lease = reserved.Item1.Value;
                long budget = SignalServiceLeaseModule.nanoseconds(lease);
                long before = Interlocked.Read(ref progress);
                snapshots.Clear();
                try { outstanding = client.Submit(0, (ulong)step, checked((ulong)budget)); }
                finally
                {
                    if (client.LastSubmission is { Generation: > 0 } submission) outstanding = submission.Generation;
                }
                long submittedTimestamp = Stopwatch.GetTimestamp();
                unfinishedInterval = new(outstanding, submittedTimestamp, 0);
                if (gc is not null && !gcReleased)
                {
                    gc.Release();
                    gcReleased = true;
                }
                accounting = Parsed(SignalAccountingModule.sentService(lease, accounting));
                var adapter = new TimerAccountingAdapter(TimerAccountingBinding.Parse(lease, outstanding, (ulong)step, start, size));
                var deadline = Stopwatch.StartNew();
                ulong copiedChildren = ulong.MaxValue, copiedVisibility = ulong.MaxValue;
                while (true)
                {
                    Require(deadline.Elapsed < TimeSpan.FromSeconds(2), "The managed timer lease did not retire within two seconds.");
                    var header = client.Probe(0);
                    if (header.Children != copiedChildren || header.Visibility != copiedVisibility || header.BackendError != 0)
                    {
                        var observation = client.Read(0);
                        snapshots.Add(observation);
                        if (!adapter.IsTerminal)
                        {
                            var applied = adapter.Accept(observation, accounting);
                            adapter = applied.Adapter;
                            accounting = applied.State;
                        }
                        Require(observation.Raw.Header.BackendError == 0, "The timer backend failed after accounting settlement.");
                        copiedChildren = observation.Raw.Header.Children;
                        copiedVisibility = observation.Raw.Header.Visibility;
                    }
                    if (copiedVisibility == 3) break;
                    Thread.Yield();
                }
                var final = snapshots[^1];
                long receivedTimestamp = Stopwatch.GetTimestamp();
                unfinishedInterval = new(outstanding, submittedTimestamp, receivedTimestamp);
                Require(adapter.IsTerminal && SignalAccountingModule.pendingCount(accounting) == 0,
                    "The retired managed timer journal did not settle exactly once.");
                int paid = final.Received.Count(child => child.Raw.Waited > 0);
                Require(final.Received.All(child => child.Raw.Waited == 0
                    || (child.Raw.InstructionPointer >= start && child.Raw.InstructionPointer - start < size)),
                    "A paid timer child interrupted code outside the selected method.");
                long after = Interlocked.Read(ref progress);
                if (plan.Scenario is null)
                    Require(after > before, "The managed target made no arithmetic progress across the command observations.");
                if (plan.RequiresOnlyDeferred)
                    Require(final.Raw.Header.Stop == 2 && adapter.ConfirmedNanoseconds == 0 && paid == 0,
                        "An ineligible transition scenario applied timer delay.");
                foreach (var child in final.Received)
                {
                    var ip = child.Raw.InstructionPointer;
                    string kind = ip >= start && ip - start < size ? "selected" : operations?.ClassifyAddress(ip) ?? "other";
                    addresses[kind] = addresses.GetValueOrDefault(kind) + 1;
                }
                if (final.Raw.Header.Stop == 1) completedLeases++;
                if (final.Raw.Header.Stop == 1 && paid > 1) chainedLeases++;
                paidChildren += paid;
                records.Add(new(step, outstanding, budget, before, after, adapter.ConfirmedNanoseconds, adapter.IsTerminal,
                    new(outstanding, submittedTimestamp, receivedTimestamp), snapshots.ToArray()));
                client.ReleaseAfterRetiredCopy(0);
                outstanding = 0;
                unfinishedInterval = null;
                snapshots.Clear();
                if (final.Raw.Header.Stop == 1)
                {
                    var caughtUp = Parsed(SignalAccountingModule.prepareService(peer.Item1, accounting));
                    Require(FSharpOption<SignalServiceLease>.get_IsNone(caughtUp.Item1),
                        "A completed peer lease must publish overshoot without another reservation.");
                    accounting = caughtUp.Item2;
                    selectedWorker.Pay(selected.Item1, ref accounting);
                }
            }
            stableMap = CaptureMap(path + ".final.perf.map") == (start, size);
            Require(stableMap, "The selected managed code range changed during timer intervention.");
            Require(plan.ReachedWork(Interlocked.Read(ref progress), records.Count, chainedLeases, paidChildren,
                addresses.GetValueOrDefault("jit"), addresses.GetValueOrDefault("native-fixture"), stableWaitReached),
                "The fixed timer workload did not reach its required work, delivery regions, and timer reuse.");
            Require(plan.RequiresOnlyDeferred || selectedWorker.Payments.Any(payment => payment.Confirmed > 0),
                "The paid timer workload did not exercise selected-side debt payment.");
        }
        catch (Exception error) { failure = error.ToString(); }
        finally
        {
            try
            {
                gcJoined = gc is null || gc.StopAndJoin(TimeSpan.FromSeconds(2));
                if (gc is not null && gcJoined) (collections, gcError) = gc.Snapshot();
                if (!gcJoined) failure ??= "The GC worker did not join. Its observations remain unavailable.";
                if (gcError is not null) failure ??= gcError;
            }
            catch (Exception error) { failure ??= error.ToString(); }
            Volatile.Write(ref stop, 1);
            unblock.Set();
            if (plan.Scenario == SignalScenario.NativeWait && operations is not null)
            {
                try { operations.ReleaseNativeWait(); }
                catch (Exception error) { failure ??= error.ToString(); }
            }
            joined = target is null || target.Join(TimeSpan.FromSeconds(2));
            if (joined)
            {
                ready.Dispose();
                unblock.Dispose();
            }
            else failure ??= "Target cleanup remains unresolved. Native ownership was retained.";
            if (Volatile.Read(ref targetFailure) is { } targetError) failure ??= targetError.ToString();
            try { selectedWorker?.StopAndJoin(); }
            catch (Exception error) { failure ??= error.ToString(); }
            if (joined && (selectedWorker is null || selectedWorker.Joined && selectedWorker.Released) && client is not null)
            {
                try
                {
                    if (outstanding != 0)
                        client.ReleaseAfterRetiredCopy(0);
                    client.CloseAfterThreadJoin();
                    closed = true;
                }
                catch (Exception error) { failure ??= error.ToString(); }
            }
        }
        if (plan.IsBlockedWait && joined && Interlocked.Read(ref progress) != 1)
            failure ??= "The controlled wait did not complete exactly once after release.";
        if (forceGc)
        {
            gcReachedCommand = collections is not null && TimerGcReach.ReachedCommand(collections, records.Select(record => record.Interval));
            if (!gcReachedCommand) failure ??= "No complete forced collection occurred inside a recorded timer command interval.";
            if (collections is null || !SampledGcStress.WithinBounds(collections, Stopwatch.Frequency))
                failure ??= "Forced collection timing did not satisfy the 250 ms call bound.";
        }
        bool passed = failure is null && joined && closed && gcJoined;
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            passed, failure, joined, closed, start, size, stableMap, completedLeases, chainedLeases, paidChildren,
            scenario = plan.Name, completedOperations = Interlocked.Read(ref progress), fixedCreditSteps = plan.CreditSteps,
            cpuCreditPerStep = plan.CpuCreditPerStep, syntheticCredit = true, stableWaitReached, addresses,
            addressMap = operations?.DescribeAddressMap(),
            forceGc, gcReleased, gcJoined, gcReachedCommand, gcError, collections, stopwatchFrequency = Stopwatch.Frequency,
            skippedSteps, records, unfinishedInterval, unfinishedSnapshots = snapshots, lastRaw = client?.LastRaw, lastSubmission = client?.LastSubmission,
            selectedPayments = selectedWorker?.Payments, selectedPendingSnapshots = selectedWorker?.PendingSnapshots,
            selectedPendingInterval = selectedWorker?.PendingInterval, selectedJoined = selectedWorker?.Joined,
            selectedRetired = selectedWorker?.Retired,
            selectedReleased = selectedWorker?.Released, selectedProgress = selectedWorker?.Progress,
            selectedFailure = selectedWorker?.Failure,
            cleanup = client?.LastCleanup, pendingAccounting = accounting is null ? (int?)null : SignalAccountingModule.pendingCount(accounting),
            generalClrSafetyEstablished = false, sampledCapacityEstablished = false, predictionAccuracyEstablished = false
        }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        return passed ? 0 : 1;
    }
}
