using System.Diagnostics;
using System.Text.Json;

static class SignalWorkload
{
    private sealed record RequestRecord(int Slot, ulong Sequence, double AcknowledgmentMilliseconds, SignalAcknowledgment? Result);
    private sealed record CollectionRecord(double CallMilliseconds, long Index, double[] PauseMilliseconds);
    public static int Run(string path, SignalPlan plan, bool forceGc, Func<IBoundedInterventionBackend> factory)
    {
        var requests = new List<RequestRecord>();
        var collections = new List<CollectionRecord>();
        var failures = new List<string>();
        var workerErrors = new Exception?[2];
        Exception? gcError = null;
        long[] batches = new long[2];
        using var ready = new CountdownEvent(2);
        using var start = new ManualResetEventSlim();
        using var stop = new ManualResetEventSlim();
        using var gcStop = new ManualResetEventSlim();
        using var deliveryStopped = new ManualResetEventSlim();
        IBoundedInterventionBackend? backend = null;
        Thread[] workers = [];
        Thread? gc = null;
        bool drained = true, joined = false, restored = false;
        double measurementMilliseconds = 0;
        long? processCpuStartTicks = null, processCpuEndTicks = null;
        try
        {
            if (Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") != "0" ||
                Environment.GetEnvironmentVariable("DOTNET_ReadyToRun") != "0" ||
                Environment.GetEnvironmentVariable("CORECLR_ENABLE_PROFILING") == "1")
                throw new InvalidOperationException("Signal measurements require disabled tiering, ReadyToRun, and startup profiling.");
            if (CpuWorkload.Run() != 49995000L) throw new InvalidOperationException("Signal warmup changed its checksum.");
            backend = factory();
            workers = Enumerable.Range(0, 2).Select(slot => new Thread(() =>
            {
                bool announced = false;
                SignalTargetLifetime.Run(backend, slot, deliveryStopped, () => drained, () =>
                {
                    announced = true;
                    ready.Signal();
                    start.Wait();
                    while (!stop.IsSet)
                    {
                        if (CpuWorkload.Run() != 49995000L) throw new InvalidOperationException("Signal work changed its checksum.");
                        batches[slot]++;
                    }
                }, error =>
                {
                    workerErrors[slot] = workerErrors[slot] is null ? error : new AggregateException(workerErrors[slot]!, error);
                    if (!announced) ready.Signal();
                });
            }) { IsBackground = true }).ToArray();
            foreach (var worker in workers) worker.Start();
            if (!ready.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Signal workers did not register.");
            if (workerErrors.Any(error => error is not null)) throw new InvalidOperationException("A signal worker failed registration.");
            if (forceGc)
            {
                gc = new Thread(() =>
                {
                    try
                    {
                        start.Wait();
                        while (!gcStop.IsSet)
                        {
                            var allocation = new byte[1024 * 1024];
                            var timer = Stopwatch.StartNew();
                            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                            double elapsed = timer.Elapsed.TotalMilliseconds;
                            var info = GC.GetGCMemoryInfo();
                            collections.Add(new(elapsed, info.Index, info.PauseDurations.ToArray().Select(pause => pause.TotalMilliseconds).ToArray()));
                            GC.KeepAlive(allocation);
                            gcStop.Wait(20);
                        }
                    }
                    catch (Exception error) { gcError = error; }
                }) { IsBackground = true };
                gc.Start();
            }
            processCpuStartTicks = Environment.CpuUsage.TotalTime.Ticks;
            var timer = Stopwatch.StartNew();
            start.Set();
            while (timer.ElapsedMilliseconds < 2000)
            {
                if (plan.Send)
                {
                    for (int slot = 0; slot < 2; slot++)
                    {
                        var acknowledgmentTimer = Stopwatch.StartNew();
                        ulong sequence = backend.Request(slot, plan);
                        drained = false;
                        SignalAcknowledgment? result;
                        do
                        {
                            result = backend.Read(slot, sequence);
                            if (result is null) Thread.Sleep(1);
                        } while (result is null && acknowledgmentTimer.ElapsedMilliseconds < 100);
                        requests.Add(new(slot, sequence, acknowledgmentTimer.Elapsed.TotalMilliseconds, result));
                        if (result is null) throw new TimeoutException("A signal request exceeded its acknowledgment deadline.");
                        drained = true;
                    }
                }
                Thread.Sleep(10);
            }
            measurementMilliseconds = timer.Elapsed.TotalMilliseconds;
            processCpuEndTicks = Environment.CpuUsage.TotalTime.Ticks;
        }
        catch (Exception error)
        {
            if (processCpuStartTicks is not null) processCpuEndTicks = Environment.CpuUsage.TotalTime.Ticks;
            failures.Add(error.ToString());
        }
        finally
        {
            deliveryStopped.Set();
            gcStop.Set();
            stop.Set();
            start.Set();
            joined = true;
            foreach (var worker in workers)
                if (!worker.Join(TimeSpan.FromSeconds(1))) joined = false;
            if (gc is not null && !gc.Join(TimeSpan.FromSeconds(1)))
            {
                joined = false;
                failures.Add("The GC worker did not join.");
            }
            if (backend is not null && joined && drained)
            {
                try { backend.RestoreAfterThreadJoin(); restored = true; }
                catch (Exception error) { failures.Add(error.ToString()); }
            }
        }
        foreach (var error in workerErrors)
            if (error is not null) failures.Add(error.ToString());
        if (gcError is not null) failures.Add(gcError.ToString());
        if (!joined || !drained || !restored) failures.Add("Signal teardown did not finish.");
        if (batches.Any(count => count == 0)) failures.Add("Both signal workers must complete arithmetic work.");
        if (plan.Send && Enumerable.Range(0, 2).Any(slot => !requests.Any(request => request.Slot == slot && request.Result?.Outcome is 2 or 3)))
            failures.Add("Both signal workers must acknowledge eligible interruptions.");
        if (requests.Any(request => request.AcknowledgmentMilliseconds > 100 || request.Result is null ||
            request.Result.Outcome == 4 || request.Result.MeasuredHandlerNanoseconds > 5000000 ||
            (plan.DelayNanoseconds == 0 && request.Result.WaitNanoseconds != 0)))
            failures.Add("A signal request failed its acknowledgment, handler, wait, or native-error bound.");
        if (requests.Zip(requests.Skip(1), (left, right) => left.Sequence < right.Sequence).Any(increasing => !increasing))
            failures.Add("Signal request sequences did not increase.");
        if (plan.DelayNanoseconds != 0 && !requests.Any(request => request.Result is { Outcome: 2, WaitNanoseconds: > 0 }))
            failures.Add("The positive-delay case did not complete an eligible native wait.");
        if (joined && forceGc && (collections.Count == 0 || collections.Any(record => record.CallMilliseconds > 250)))
            failures.Add("The forced-GC case failed its collection reach or call-duration bound.");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            backend = backend?.Name, sendSignals = plan.Send, requestedDelayNanoseconds = plan.DelayNanoseconds,
            nativeRange = backend?.Describe(), runtime = Environment.Version.ToString(),
            platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            forcedGc = forceGc, measurementMilliseconds, completedBatches = joined ? batches : null,
            processCpuStartTicks, processCpuEndTicks,
            requests, collections = joined ? collections : null, failures, passed = failures.Count == 0,
            joined, drained, restored, causalCredit = 0, cpuSamplesCollected = false,
            generalClrSafetyEstablished = false, handlerBoundNanoseconds = 5000000,
            acknowledgmentBoundMilliseconds = 100, gcCallBoundMilliseconds = 250,
            gcCallDurationIsSuspensionLatency = false
        }, new JsonSerializerOptions { WriteIndented = true }));
        return failures.Count == 0 ? 0 : 1;
    }
}
