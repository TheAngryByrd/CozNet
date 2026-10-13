using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

static class PhysicalWorkload
{
    public static void Run(string path, string mode, MeasurementWindow window)
    {
        bool optimized = mode switch
        {
            "baseline" => false,
            "optimized" => true,
            _ => throw new ArgumentException("Select baseline or optimized physical work.")
        };
        if (CpuWorkload.Run() != 49995000L || CpuWorkload.RunOptimized() != 49995000L)
            throw new InvalidOperationException("The physical optimization changed the checksum.");
        using var ready = new ManualResetEventSlim();
        using var stop = new ManualResetEventSlim();
        var worker = Task.Factory.StartNew(() =>
        {
            long batches = 0;
            ready.Set();
            while (!stop.IsSet)
            {
                if (CpuWorkload.Run() != 49995000L)
                    throw new InvalidOperationException("The physical worker changed the checksum.");
                batches++;
            }
            return batches;
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        long completedBatches = 0, checksum = 0;
        double milliseconds;
        try
        {
            if (!ready.Wait(TimeSpan.FromSeconds(5)))
                throw new InvalidOperationException("The physical worker did not start.");
            var elapsed = Stopwatch.StartNew();
            while (elapsed.ElapsedMilliseconds < window.Milliseconds)
            {
                checksum += optimized ? CpuWorkload.RunOptimized() : CpuWorkload.Run();
                completedBatches++;
            }
            milliseconds = elapsed.Elapsed.TotalMilliseconds;
        }
        finally
        {
            stop.Set();
            worker.GetAwaiter().GetResult();
        }
        if (completedBatches == 0 || checksum != checked(completedBatches * 49995000L))
            throw new InvalidOperationException("Physical progress does not match its checksum.");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            mode, completedBatches, checksum, experimentMilliseconds = milliseconds,
            measurementMilliseconds = window.Milliseconds,
            workerBatches = worker.Result, runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString()
        }));
    }
}
