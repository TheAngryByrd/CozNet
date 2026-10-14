using System.Diagnostics;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.Json;

enum SignalScenario { Jit, NativeWait, ManagedWait, Transitions }

static class SignalTransitionWorkload
{
    private sealed record RequestRecord(ulong Sequence, string AddressKind, double AcknowledgmentMilliseconds, long CompletedBefore, long CompletedAfter, SignalAcknowledgment? Result);
    private sealed record CollectionRecord(double CallMilliseconds, long Index, double[] PauseMilliseconds);
    public static SignalScenario Parse(string value) => value switch
    {
        "jit" => SignalScenario.Jit,
        "native-wait" => SignalScenario.NativeWait,
        "managed-wait" => SignalScenario.ManagedWait,
        "transitions" => SignalScenario.Transitions,
        _ => throw new ArgumentException("Select jit, native-wait, managed-wait, or transitions.")
    };
    public static int Run(string path, SignalScenario scenario, bool forceGc,
        Func<IBoundedInterventionBackend> backendFactory, Func<ISignalTransitionOperations> operationsFactory)
    {
        var requests = new List<RequestRecord>();
        var collections = new List<CollectionRecord>();
        var failures = new List<string>();
        Exception? workerError = null, gcError = null;
        IBoundedInterventionBackend? backend = null;
        ISignalTransitionOperations? operations = null;
        Thread? worker = null, gc = null;
        using var ready = new ManualResetEventSlim();
        using var start = new ManualResetEventSlim();
        using var stop = new ManualResetEventSlim();
        using var deliveryStopped = new ManualResetEventSlim();
        using var unblock = new ManualResetEventSlim(false, 0);
        bool drained = true, joined = false, restored = false, stableWaitReached = false;
        long completed = 0;
        int enteringManagedWait = 0;
        double measurementMilliseconds = 0;
        try
        {
            if (Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") != "0" ||
                Environment.GetEnvironmentVariable("DOTNET_ReadyToRun") != "0" ||
                Environment.GetEnvironmentVariable("CORECLR_ENABLE_PROFILING") == "1")
                throw new InvalidOperationException("Signal transitions require disabled tiering, ReadyToRun, and startup profiling.");
            if (CpuWorkload.Run() != 49995000L) throw new InvalidOperationException("Signal transition warmup changed its checksum.");
            backend = backendFactory();
            operations = operationsFactory();
            worker = new Thread(() => SignalTargetLifetime.Run(backend, 0, deliveryStopped, () => drained,
                () =>
                {
                    ready.Set();
                    start.Wait();
                    if (scenario == SignalScenario.NativeWait)
                    {
                        operations.WaitInNative();
                        Interlocked.Increment(ref completed);
                    }
                    else if (scenario == SignalScenario.ManagedWait)
                    {
                        Volatile.Write(ref enteringManagedWait, 1);
                        if (!unblock.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("The managed wait did not receive release.");
                        Interlocked.Increment(ref completed);
                    }
                    else
                    {
                        long thread = operations.NativeHop();
                        while (!stop.IsSet)
                        {
                            if (scenario == SignalScenario.Jit) CompileAndRun();
                            else if (CpuWorkload.Run() != 49995000L || operations.NativeHop() != thread)
                                throw new InvalidOperationException("The managed/native transition changed its checksum or thread.");
                            Interlocked.Increment(ref completed);
                        }
                    }
                },
                error => { workerError = workerError is null ? error : new AggregateException(workerError, error); ready.Set(); })) { IsBackground = true };
            worker.Start();
            if (!ready.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("The signal transition worker did not register.");
            if (workerError is not null) throw new InvalidOperationException("The signal transition worker failed registration.", workerError);
            if (forceGc)
            {
                gc = new Thread(() =>
                {
                    try
                    {
                        start.Wait();
                        while (!stop.IsSet)
                        {
                            var allocation = new byte[1024 * 1024];
                            var timer = Stopwatch.StartNew();
                            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                            double elapsed = timer.Elapsed.TotalMilliseconds;
                            var info = GC.GetGCMemoryInfo();
                            collections.Add(new(elapsed, info.Index, info.PauseDurations.ToArray().Select(pause => pause.TotalMilliseconds).ToArray()));
                            GC.KeepAlive(allocation);
                            stop.Wait(20);
                        }
                    }
                    catch (Exception error) { gcError = error; }
                }) { IsBackground = true };
                gc.Start();
            }
            start.Set();
            if (scenario is SignalScenario.NativeWait or SignalScenario.ManagedWait)
            {
                var reach = Stopwatch.StartNew();
                do
                {
                    stableWaitReached = scenario == SignalScenario.NativeWait ? operations.NativeWaitEntered :
                        Volatile.Read(ref enteringManagedWait) == 1 && (worker.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0;
                    if (!stableWaitReached) Thread.Sleep(1);
                } while (!stableWaitReached && reach.ElapsedMilliseconds < 1000);
                if (!stableWaitReached) throw new TimeoutException("The transition worker did not enter its controlled wait.");
            }
            var measurement = Stopwatch.StartNew();
            var plan = SignalPlan.Parse("delay");
            while (measurement.ElapsedMilliseconds < 2000)
            {
                long before = Interlocked.Read(ref completed);
                var acknowledgment = Stopwatch.StartNew();
                ulong sequence = backend.Request(0, plan);
                drained = false;
                SignalAcknowledgment? result;
                do
                {
                    result = backend.Read(0, sequence);
                    if (result is null) Thread.Sleep(1);
                } while (result is null && acknowledgment.ElapsedMilliseconds < 100);
                double elapsed = acknowledgment.Elapsed.TotalMilliseconds;
                drained = result is not null;
                string kind = result is null ? "missing" : backend.IsEligibleAddress(result.InstructionPointer) ? "selected" : operations.ClassifyAddress(result.InstructionPointer);
                requests.Add(new(sequence, kind, elapsed, before, Interlocked.Read(ref completed), result));
                if (result is null) throw new TimeoutException("A transition request exceeded its acknowledgment deadline.");
                if (scenario == SignalScenario.NativeWait && !operations.NativeWaitEntered)
                    throw new InvalidOperationException("The native wait ended before the controller released it.");
                if (scenario == SignalScenario.ManagedWait && Interlocked.Read(ref completed) != 0)
                    throw new InvalidOperationException("The managed wait ended before the controller released it.");
                Thread.Sleep(1);
            }
            measurementMilliseconds = measurement.Elapsed.TotalMilliseconds;
        }
        catch (Exception error) { failures.Add(error.ToString()); }
        finally
        {
            deliveryStopped.Set();
            stop.Set();
            start.Set();
            unblock.Set();
            if (operations is not null)
            {
                try { operations.ReleaseNativeWait(); }
                catch (Exception error) { failures.Add(error.ToString()); }
            }
            joined = worker is null || worker.Join(TimeSpan.FromSeconds(1));
            if (gc is not null && !gc.Join(TimeSpan.FromSeconds(1))) joined = false;
            if (backend is not null && joined && drained)
            {
                try { backend.RestoreAfterThreadJoin(); restored = true; }
                catch (Exception error) { failures.Add(error.ToString()); }
            }
        }
        if (workerError is not null) failures.Add(workerError.ToString());
        if (gcError is not null) failures.Add(gcError.ToString());
        if (!joined || !drained || !restored) failures.Add("Signal transition teardown did not finish.");
        if (completed == 0 || requests.Count < 10) failures.Add("The transition case did not reach work and request progress.");
        if (requests.Any(request => request.Result is null || request.AcknowledgmentMilliseconds > 100 ||
            request.Result.Outcome == 4 || request.Result.MeasuredHandlerNanoseconds > 5000000 ||
            ((request.AddressKind == "selected") != (request.Result.Outcome is 2 or 3)) ||
            (request.Result.Outcome == 1 && request.Result.WaitNanoseconds != 0)))
            failures.Add("A transition request failed its timing, eligibility, or native-error check.");
        if (requests.Zip(requests.Skip(1), (left, right) => left.Sequence < right.Sequence).Any(increasing => !increasing))
            failures.Add("Transition request sequences did not increase.");
        if (scenario == SignalScenario.Jit && (completed < 10 || !requests.Any(request => request.AddressKind == "jit")))
            failures.Add("The JIT case did not complete methods and interrupt executable JIT code.");
        if (scenario == SignalScenario.Transitions && (!requests.Any(request => request.AddressKind == "selected" && request.Result?.WaitNanoseconds > 0) ||
            !requests.Any(request => request.AddressKind == "native-fixture")))
            failures.Add("The transition case did not interrupt both selected managed work and native fixture code.");
        if (scenario != SignalScenario.Transitions && requests.Any(request => request.Result?.Outcome != 1))
            failures.Add("An ineligible transition case applied signal delay.");
        if (joined && forceGc && (collections.Count == 0 || collections.Any(record => record.CallMilliseconds > 250)))
            failures.Add("The transition GC case failed its collection reach or call-duration bound.");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            scenario = scenario.ToString(), forcedGc = forceGc, backend = backend?.Name, nativeRange = backend?.Describe(),
            addressMap = operations?.DescribeAddressMap(),
            runtime = Environment.Version.ToString(), platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            completedOperations = joined ? completed : (long?)null, measurementMilliseconds, stableWaitReached,
            requests, collections = joined ? collections : null, joined, drained, restored, failures, passed = failures.Count == 0,
            handlerBoundNanoseconds = 5000000, acknowledgmentBoundMilliseconds = 100, gcCallBoundMilliseconds = 250,
            requestedDelayNanoseconds = 50000, causalAccountingConnected = false, generalClrSafetyEstablished = false
        }, new JsonSerializerOptions { WriteIndented = true }));
        return failures.Count == 0 ? 0 : 1;
    }
    private static void CompileAndRun()
    {
        var method = new DynamicMethod("SignalJit", typeof(int), [typeof(int)]);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        for (int i = 0; i < 256; i++) { il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); }
        il.Emit(OpCodes.Ret);
        var compiled = method.CreateDelegate<Func<int, int>>();
        RuntimeHelpers.PrepareDelegate(compiled);
        if (compiled(17) != 273) throw new InvalidOperationException("The dynamic method changed its checksum.");
    }
}
