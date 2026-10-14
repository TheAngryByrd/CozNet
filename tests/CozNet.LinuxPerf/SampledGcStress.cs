using System.Diagnostics;

sealed record GcStressObservation(long StartedTimestamp, long FinishedTimestamp, long CollectionIndex, long[] PauseTicks);

sealed class SampledGcStress
{
    private readonly ManualResetEventSlim release = new();
    private readonly ManualResetEventSlim stop = new();
    private readonly List<GcStressObservation> observations = [];
    private readonly Thread worker;
    private Exception? error;
    private bool joined;

    private SampledGcStress() => worker = new Thread(Run) { IsBackground = true };

    public static SampledGcStress Start()
    {
        var stress = new SampledGcStress();
        try { stress.worker.Start(); }
        catch { stress.release.Dispose(); stress.stop.Dispose(); throw; }
        return stress;
    }

    public void Release()
    {
        if (joined) throw new InvalidOperationException("The GC worker has already stopped.");
        release.Set();
    }

    public bool StopAndJoin(TimeSpan timeout)
    {
        if (joined) return true;
        RequestStop();
        joined = worker.Join(timeout);
        if (joined) { stop.Dispose(); release.Dispose(); }
        return joined;
    }

    public void RequestStop()
    {
        if (joined) return;
        stop.Set();
        release.Set();
    }

    public (GcStressObservation[] Observations, string? Error) Snapshot()
    {
        if (!joined) throw new InvalidOperationException("GC observations require a joined worker.");
        return (observations.ToArray(), error?.ToString());
    }

    private void Run()
    {
        try
        {
            release.Wait();
            while (!stop.IsSet)
            {
                if (observations.Count >= 10000) throw new InvalidOperationException("The GC observation limit was exceeded.");
                var allocation = new byte[1024 * 1024];
                long started = Stopwatch.GetTimestamp();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                long finished = Stopwatch.GetTimestamp();
                var info = GC.GetGCMemoryInfo();
                observations.Add(new(started, finished, info.Index, info.PauseDurations.ToArray().Select(pause => pause.Ticks).ToArray()));
                GC.KeepAlive(allocation);
                stop.Wait(20);
            }
        }
        catch (Exception failure) { error = failure; }
    }

    public static bool ReachedMeasurement(IEnumerable<GcStressObservation> observations, long started, long finished) =>
        started > 0 && finished >= started && observations.Any(value => value.StartedTimestamp >= started && value.FinishedTimestamp >= value.StartedTimestamp
            && value.FinishedTimestamp <= finished && value.CollectionIndex > 0);

    public static bool WithinBounds(IEnumerable<GcStressObservation> observations, long frequency) =>
        frequency > 0 && observations.All(value => value.StartedTimestamp > 0 && value.CollectionIndex > 0
            && value.PauseTicks.All(ticks => ticks >= 0) && value.FinishedTimestamp >= value.StartedTimestamp
            && 1000.0 * (value.FinishedTimestamp - value.StartedTimestamp) / frequency <= 250);
}
