using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

static class MatchedPhysicalWorkload
{
    public static int Run(string path, PhysicalWorkMode mode)
    {
        var work = new MatchedCpuWorkers(mode);
        var capture = new MeasurementWindowCapture(TimeProvider.System);
        Thread?[] workers = new Thread?[2];
        Exception?[] workerErrors = new Exception?[2];
        bool[] joined = new bool[2];
        var failures = new List<Exception>();
        long started = 0, finished = 0;

        MeasurementWindowCapture.Readings Read() => new(0, 0, 0, 1,
            new(work.Progress(0), 0, 0, null), new(work.Progress(1), 0, 0, null));

        try
        {
            if (Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") != "0"
                || Environment.GetEnvironmentVariable("DOTNET_ReadyToRun") != "0"
                || Environment.GetEnvironmentVariable("CORECLR_ENABLE_PROFILING") == "1")
                throw new InvalidDataException("Matched physical work requires disabled tiering and ReadyToRun, and no startup profiler.");
            MatchedCpuWorkers.Warmup();
            for (int index = 0; index < 2; index++)
            {
                int slot = index;
                var worker = new Thread(() =>
                {
                    try
                    {
                        work.SignalReady(slot);
                        work.Run(slot);
                    }
                    catch (Exception error) { Volatile.Write(ref workerErrors[slot], error); }
                    finally { work.SignalReady(slot); }
                }) { IsBackground = true };
                worker.Start();
                workers[slot] = worker;
            }
            for (int slot = 0; slot < 2; slot++)
                if (!work.WaitReady(slot, TimeSpan.FromSeconds(2)) || Volatile.Read(ref workerErrors[slot]) is not null)
                    throw new InvalidDataException("A matched physical worker failed setup.");
            capture.Begin(Read, () =>
            {
                started = Stopwatch.GetTimestamp();
                work.Release();
            });
            long remaining;
            while ((remaining = 2000 - (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds) > 0)
                Thread.Sleep((int)remaining);
            finished = Stopwatch.GetTimestamp();
            capture.Finish(Read);
            if (capture.End!.Accounting.Selected.Progress == 0 || capture.End.Accounting.Peer.Progress == 0)
                throw new InvalidDataException("Both matched physical workers must complete progress.");
        }
        catch (Exception error)
        {
            failures.Add(error);
            if (started != 0 && finished == 0) finished = Stopwatch.GetTimestamp();
            if (capture.Start is not null)
                try { capture.Finish(Read); } catch (Exception captureError) { failures.Add(captureError); }
        }
        finally
        {
            work.RequestStop();
            for (int slot = 0; slot < 2; slot++)
            {
                joined[slot] = workers[slot] is null || workers[slot]!.Join(TimeSpan.FromSeconds(2));
                if (joined[slot]) work.DisposeReady(slot);
                else failures.Add(new InvalidDataException("A matched physical worker did not join."));
                if (Volatile.Read(ref workerErrors[slot]) is { } error) failures.Add(error);
            }
            if (joined.All(value => value)) work.Dispose();
        }

        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            mode = mode.Name, failures = failures.Select(error => error.ToString()), joined,
            measurementStartedTimestamp = started, measurementFinishedTimestamp = finished,
            measurementCapture = new { status = capture.Status.ToString(), start = capture.Start, end = capture.End },
            progress = work.SnapshotProgress(), expectedChecksum = MatchedCpuWorkers.ExpectedChecksum,
            timestampFrequency = Stopwatch.Frequency, measurementMilliseconds = 2000,
            runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            predictionAccuracyEstablished = false
        }, new JsonSerializerOptions { WriteIndented = true }));
        return failures.Count == 0 ? 0 : 1;
    }
}
