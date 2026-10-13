sealed class LiveSampleCollector : IDisposable
{
    private readonly ManualResetEventSlim stop = new(false);
    private readonly Task worker;
    private SampleBatch? latest;
    private bool completed;

    public LiveSampleCollector(Func<SampleBatch> drain)
    {
        worker = Task.Factory.StartNew(() =>
        {
            do
            {
                Volatile.Write(ref latest, drain());
            } while (!stop.Wait(1));
            Volatile.Write(ref latest, drain());
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public SampleBatch? Latest => Volatile.Read(ref latest);

    public void RequestStop()
    {
        if (!completed) stop.Set();
    }

    public SampleBatch Complete()
    {
        if (!completed)
        {
            RequestStop();
            try { worker.GetAwaiter().GetResult(); }
            finally { completed = true; stop.Dispose(); }
        }
        worker.GetAwaiter().GetResult();
        return Latest ?? throw new InvalidOperationException("The collector produced no snapshot.");
    }

    public void Dispose()
    {
        if (!completed) Complete();
    }
}
