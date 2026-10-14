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

    private static SignalAccounting Credit(LedgerThread selected, int step, SignalAccounting state)
    {
        var identity = Parsed(SignalAccountingModule.sampleIdentity(selected, state));
        var sample = Parsed(CpuSampleObservationModule.tryCreate(identity, (ulong)step, (ulong)step * 220000,
            0, SampleThreadState.Runnable, SampleLocation.Selected));
        var observed = Parsed(SignalAccountingModule.observe(selected, sample, state));
        var credited = Parsed(SignalAccountingModule.prepare(selected, observed.Item2));
        Require(FSharpOption<SignalDelayRequest>.get_IsNone(credited.Item1), "Synthetic selected credit must not wait.");
        return credited.Item2;
    }

    private static (ulong Start, ulong Size) CaptureMap(string output)
    {
        File.WriteAllText(output, File.ReadAllText($"/tmp/perf-{Environment.ProcessId}.map"));
        return PerfMapSnapshot.Read(output).UniqueRange(OverheadWorkload.SelectedSymbol);
    }

    public static int Run(string path, bool forceGc = false)
    {
        var records = new List<CommandRecord>();
        var snapshots = new List<TimerObservation>();
        var skippedSteps = new List<int>();
        NativeTimerClient? client = null;
        Thread? target = null;
        SignalAccounting? accounting = null;
        SampledGcStress? gc = null;
        GcStressObservation[]? collections = null;
        string? gcError = null;
        bool gcJoined = false, gcReleased = false, gcReachedCommand = false;
        Exception? targetFailure = null;
        string? failure = null;
        var ready = new ManualResetEventSlim();
        int stop = 0;
        long progress = 0;
        ulong outstanding = 0;
        TimerCommandInterval? unfinishedInterval = null;
        ulong start = 0, size = 0;
        bool joined = false, closed = false, stableMap = false;
        int completedLeases = 0, chainedLeases = 0, paidChildren = 0;
        try
        {
            Require(OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") == "0"
                && Environment.GetEnvironmentVariable("DOTNET_ReadyToRun") == "0"
                && Environment.GetEnvironmentVariable("CORECLR_ENABLE_PROFILING") != "1",
                "Managed timer work requires Linux, disabled tiering and ReadyToRun, and no startup profiler.");
            Require(CpuWorkload.Run() == 49995000L, "Managed timer warmup changed the arithmetic checksum.");
            (start, size) = CaptureMap(path + ".initial.perf.map");
            client = new NativeTimerClient(start, size, capacity: 64);
            var worker = new Thread(() =>
            {
                try
                {
                    client.RegisterCurrentThread(0);
                    ready.Set();
                    while (Volatile.Read(ref stop) == 0)
                    {
                        if (CpuWorkload.Run() != 49995000L) throw new InvalidDataException("A timer intervention changed the arithmetic checksum.");
                        Interlocked.Increment(ref progress);
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
            var selected = Parsed(SignalAccountingModule.register(FSharpOption<int>.Some(50), SignalAccountingModule.create()));
            var peer = Parsed(SignalAccountingModule.register(FSharpOption<int>.None, selected.Item2));
            accounting = peer.Item2;
            if (forceGc) gc = SampledGcStress.Start();
            for (int step = 1; step <= 16; step++)
            {
                Require(Volatile.Read(ref targetFailure) is null, "The managed timer target failed before submission.");
                accounting = Credit(selected.Item1, step, accounting);
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
                outstanding = client.Submit(0, (ulong)step, checked((ulong)budget));
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
                Require(after > before, "The managed target made no arithmetic progress during a timer lease.");
                if (final.Raw.Header.Stop == 1) completedLeases++;
                if (final.Raw.Header.Stop == 1 && paid > 1) chainedLeases++;
                paidChildren += paid;
                records.Add(new(step, outstanding, budget, before, after, adapter.ConfirmedNanoseconds, adapter.IsTerminal,
                    new(outstanding, submittedTimestamp, receivedTimestamp), snapshots.ToArray()));
                client.ReleaseAfterRetiredCopy(0);
                outstanding = 0;
                unfinishedInterval = null;
                snapshots.Clear();
            }
            stableMap = CaptureMap(path + ".final.perf.map") == (start, size);
            Require(stableMap, "The selected managed code range changed during timer intervention.");
            Require(records.Count > 1 && chainedLeases > 0 && paidChildren > 1,
                "The fixed workload did not reach reused timer generations and a completed multi-child paid lease.");
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
            joined = target is null || target.Join(TimeSpan.FromSeconds(2));
            if (joined) ready.Dispose();
            else failure ??= "Target cleanup remains unresolved. Native ownership was retained.";
            if (Volatile.Read(ref targetFailure) is { } targetError) failure ??= targetError.ToString();
            if (joined && client is not null)
            {
                try
                {
                    if (outstanding != 0 || client.LastSubmission is { Status: not 0, Generation: > 0 })
                        client.ReleaseAfterRetiredCopy(0);
                    client.CloseAfterThreadJoin();
                    closed = true;
                }
                catch (Exception error) { failure ??= error.ToString(); }
            }
        }
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
            completedArithmeticCalls = Interlocked.Read(ref progress), fixedCreditSteps = 16, syntheticCredit = true,
            forceGc, gcReleased, gcJoined, gcReachedCommand, gcError, collections, stopwatchFrequency = Stopwatch.Frequency,
            skippedSteps, records, unfinishedInterval, unfinishedSnapshots = snapshots, lastRaw = client?.LastRaw, lastSubmission = client?.LastSubmission,
            cleanup = client?.LastCleanup, pendingAccounting = accounting is null ? (int?)null : SignalAccountingModule.pendingCount(accounting),
            generalClrSafetyEstablished = false, sampledCapacityEstablished = false, predictionAccuracyEstablished = false
        }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        return passed ? 0 : 1;
    }
}
