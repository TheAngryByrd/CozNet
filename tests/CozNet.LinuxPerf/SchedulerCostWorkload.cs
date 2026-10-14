using System.Diagnostics;
using System.Text.Json;

static class SchedulerCostWorkload
{
    public static int Run(string path, string mode, Action<Thread>? startWorker = null)
    {
        bool capture = mode switch { "enabled" => true, "disabled" => false, _ => throw new ArgumentException("Select enabled or disabled scheduler capture.") };
        const int iterations = 300000;
        var sessions = new ISchedulerEventSession?[3];
        var captures = new SchedulerCapture?[3];
        var checksums = new long[3];
        var failures = new List<string>();
        var errors = new Exception?[2];
        var ready = new CountdownEvent(2);
        var start = new ManualResetEventSlim();
        var done = new CountdownEvent(2);
        var retire = new ManualResetEventSlim();
        Thread[] workers = [];
        int startedWorkers = 0;
        long elapsedTicks = 0, processCpuTicks = 0, cleanupTicks = 0;
        bool stopped = true, joined = false;
        long Work()
        {
            long checksum = 0;
            for (int i = 0; i < iterations; i++) checksum += CpuWorkload.Run();
            return checksum;
        }
        try
        {
            if (CpuWorkload.Run() != 49995000L) throw new InvalidOperationException("Scheduler cost warmup checksum differs.");
            if (capture) sessions[0] = new LinuxSchedulerEventSession();
            workers = Enumerable.Range(0, 2).Select(slot => new Thread(() =>
            {
                bool announced = false;
                try
                {
                    if (capture) sessions[slot + 1] = new LinuxSchedulerEventSession();
                    announced = true;
                    ready.Signal();
                    start.Wait();
                    checksums[slot + 1] = Work();
                }
                catch (Exception error) { errors[slot] = error; }
                finally
                {
                    if (!announced) ready.Signal();
                    done.Signal();
                    retire.Wait();
                }
            }) { IsBackground = true }).ToArray();
            foreach (var worker in workers)
            {
                if (startWorker is null) worker.Start();
                else startWorker(worker);
                startedWorkers++;
            }
            if (!ready.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Scheduler cost workers did not become ready.");
            if (errors.Any(error => error is not null)) throw new InvalidOperationException("Scheduler cost registration failed.");
            using var process = Process.GetCurrentProcess();
            long cpuBefore = process.TotalProcessorTime.Ticks;
            long before = Stopwatch.GetTimestamp();
            start.Set();
            checksums[0] = Work();
            if (!done.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Scheduler cost workers did not complete.");
            elapsedTicks = Stopwatch.GetTimestamp() - before;
            processCpuTicks = process.TotalProcessorTime.Ticks - cpuBefore;
        }
        catch (Exception error) { failures.Add(error.ToString()); }
        finally
        {
            start.Set();
            for (int index = startedWorkers; index < workers.Length; index++) done.Signal();
            if (workers.Length > 0 && !done.Wait(TimeSpan.FromSeconds(10)))
            {
                failures.Add("Scheduler cost workers remain active.");
                stopped = false;
            }
            long before = Stopwatch.GetTimestamp();
            for (int index = 0; index < sessions.Length; index++)
            {
                var session = sessions[index];
                if (session is null) continue;
                try
                {
                    captures[index] = session.Finish();
                    failures.AddRange(captures[index]!.Failures);
                    session.Dispose();
                }
                catch (Exception error) { stopped = false; failures.Add(error.ToString()); }
            }
            cleanupTicks = Stopwatch.GetTimestamp() - before;
            if (stopped) retire.Set();
            joined = true;
            for (int index = 0; index < startedWorkers; index++)
                if (!workers[index].Join(TimeSpan.FromSeconds(1))) joined = false;
            if (joined)
            {
                ready.Dispose();
                start.Dispose();
                done.Dispose();
                retire.Dispose();
            }
        }
        foreach (var error in errors) if (error is not null) failures.Add(error.ToString());
        if (checksums.Any(value => value != iterations * 49995000L)) failures.Add("Fixed work checksum differs.");
        if (!joined || !stopped || elapsedTicks <= 0 || processCpuTicks <= 0) failures.Add("Scheduler cost measurement did not complete.");
        if (capture && captures.Any(value => value is null || value.FinalLost != 0)) failures.Add("Scheduler cost capture is incomplete.");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            mode, iterationsPerThread = iterations, checksums, elapsedTicks, processCpuTicks,
            stopwatchFrequency = Stopwatch.Frequency, cleanupTicks, captures, failures, joined, stopped,
            passed = failures.Count == 0, sendsSignals = false, usesClr = true, startedWorkers
        }, new JsonSerializerOptions { WriteIndented = true }));
        return failures.Count == 0 ? 0 : 1;
    }
}
