using System.Runtime.ExceptionServices;

sealed class OwnerSampleCollector(Action drain) : IPausableSampleCollector
{
    private enum State { Running, Draining, Paused, Stopped, Failed }
    private readonly Action drain = drain ?? throw new ArgumentNullException(nameof(drain));
    private readonly int owner = Environment.CurrentManagedThreadId;
    private State state;
    private CollectorPause? paused;
    private ExceptionDispatchInfo? failure;
    private ulong sequence;

    private void RequireOwner()
    {
        if (Environment.CurrentManagedThreadId != owner)
            throw new InvalidOperationException("Collector control requires its owning thread.");
    }

    public void Drain()
    {
        RequireOwner();
        failure?.Throw();
        if (state != State.Running)
            throw new InvalidOperationException("Only a running owner collector can drain.");
        state = State.Draining;
        try
        {
            drain();
            state = State.Running;
        }
        catch (Exception error)
        {
            failure = ExceptionDispatchInfo.Capture(error);
            state = State.Failed;
            throw;
        }
    }

    public Task<CollectorPause> RequestPause()
    {
        Drain();
        paused = new(checked(++sequence));
        state = State.Paused;
        return Task.FromResult(paused);
    }

    public void Resume(CollectorPause admission)
    {
        RequireOwner();
        failure?.Throw();
        if (state != State.Paused || !ReferenceEquals(admission, paused))
            throw new InvalidOperationException("Resume requires the current collector pause.");
        paused = null;
        state = State.Running;
    }

    public void Complete()
    {
        RequireOwner();
        failure?.Throw();
        if (state == State.Draining)
            throw new InvalidOperationException("Collection cannot complete during a drain.");
        if (state == State.Running) Drain();
        paused = null;
        state = State.Stopped;
    }

    public void Dispose() => Complete();
}
