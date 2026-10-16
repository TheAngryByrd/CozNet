using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

static class CooperativePhysicalWorkload
{
    public static int Run(string path, CooperativeWorkPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var work = new CooperativeCpuWork(plan.Mode);
        var measurement = new MeasurementWindowCapture(TimeProvider.System);
        var failures = new List<string>();
        CooperativeInteropChecks.Worker? worker = null;
        Task? peer = null;
        var started = new ManualResetEventSlim();
        var stopped = new ManualResetEventSlim();
        bool joined = false;
        long? selectedWorkStarted = null, selectedWorkFinished = null;
        MeasurementWindowCapture.Readings Read() => new(0, 0, 0, 1,
            new(work.SelectedProgress, 0, 0, null), new(work.PeerProgress, 0, 0, null));
        try
        {
            if (Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") != "0"
                || Environment.GetEnvironmentVariable("DOTNET_ReadyToRun") != "0"
                || Environment.GetEnvironmentVariable("CORECLR_ENABLE_PROFILING") == "1"
                || Environment.GetEnvironmentVariable("DOTNET_ENABLE_PROFILING") == "1")
                throw new InvalidDataException("Cooperative physical work requires disabled tiering, ReadyToRun, and startup profiling.");
            CooperativeCpuWork.Warmup();
            worker = new();
            worker.Run(CooperativeCpuWork.Warmup);
            peer = worker.Start(() =>
            {
                try { started.Set(); work.RunPeer(); }
                finally { stopped.Set(); }
            });
            if (!started.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("The physical peer did not start.");
            measurement.Begin(Read, () => { });
            selectedWorkStarted = Stopwatch.GetTimestamp();
            while (work.SelectedProgress < plan.Batches)
            {
                if (peer.IsCompleted) throw new InvalidDataException("The physical peer stopped during selected work.");
                work.RunSelected();
            }
            selectedWorkFinished = Stopwatch.GetTimestamp();
            measurement.Finish(Read);
            if (measurement.End!.Accounting.Peer.Progress <= measurement.Start!.Accounting.Peer.Progress)
                throw new InvalidDataException("The physical peer made no measured progress.");
        }
        catch (Exception error)
        {
            failures.Add(error.ToString());
            if (measurement.Start is not null)
                try { measurement.Finish(Read); } catch (Exception captureError) { failures.Add(captureError.ToString()); }
        }
        finally
        {
            work.RequestPeerStop();
            bool peerStopped = peer is null || stopped.Wait(TimeSpan.FromSeconds(5));
            if (!peerStopped) failures.Add("The physical peer did not stop.");
            else if (peer is not null)
                try { peer.GetAwaiter().GetResult(); } catch (Exception error) { failures.Add(error.ToString()); }
            if (peerStopped)
            {
                try { joined = worker is null || worker.Join(); }
                catch (Exception error) { failures.Add(error.ToString()); }
                if (!joined) failures.Add("The physical peer worker did not join.");
            }
            if (joined) { started.Dispose(); stopped.Dispose(); }
        }
        if (work.SelectedChecksum != checked(work.SelectedProgress * CooperativeCpuWork.ExpectedChecksum)
            || work.PeerChecksum != checked(work.PeerProgress * CooperativeCpuWork.ExpectedChecksum))
            failures.Add("Final physical checksum totals differ from completed batches.");
        bool completed = failures.Count == 0 && joined && measurement.Status == MeasurementCaptureStatus.Complete
            && work.SelectedProgress == plan.Batches;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            workloadProtocol = CooperativeCpuWork.Protocol, mode = plan.Mode.Name, requestedBatches = plan.Batches,
            completed, failures, joined, measurementWindow = measurement, selectedWorkStarted, selectedWorkFinished,
            selectedBatches = work.SelectedProgress, checksum = work.SelectedChecksum,
            peerBatches = work.PeerProgress, peerChecksum = work.PeerChecksum,
            expectedChecksum = CooperativeCpuWork.ExpectedChecksum, timestampFrequency = Stopwatch.Frequency,
            runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(), predictionAccuracyEstablished = false
        }, new JsonSerializerOptions { WriteIndented = true }));
        return completed ? 0 : 1;
    }
}
