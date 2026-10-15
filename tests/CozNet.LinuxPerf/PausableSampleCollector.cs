using System.Runtime.ExceptionServices;

sealed class PausableSampleCollector : IDisposable
{
    internal sealed class Pause(ulong sequence)
    {
        public ulong Sequence { get; } = sequence;
    }

    private enum State { Running, PauseRequested, Paused, StopRequested, Stopped, Failed }
    private readonly int owner = Environment.CurrentManagedThreadId;
    private readonly object gate = new();
    private readonly AutoResetEvent wake = new(false);
    private readonly Task worker;
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private State state;
    private TaskCompletionSource<Pause>? pendingPause;
    private Pause? paused;
    private ExceptionDispatchInfo? failure;
    private ulong sequence;
    private bool drainOnStop, disposed;

    public PausableSampleCollector(Action drain)
    {
        ArgumentNullException.ThrowIfNull(drain);
        worker = Task.Factory.StartNew(() => Run(drain), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private void RequireOwner()
    {
        if (Environment.CurrentManagedThreadId != owner)
            throw new InvalidOperationException("Collector control requires its owning thread.");
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    public Task<Pause> RequestPause()
    {
        RequireOwner();
        lock (gate)
        {
            failure?.Throw();
            if (state != State.Running) throw new InvalidOperationException("Only a running collector can pause.");
            pendingPause = new(TaskCreationOptions.RunContinuationsAsynchronously);
            state = State.PauseRequested;
            wake.Set();
            return pendingPause.Task;
        }
    }

    public void Resume(Pause admission)
    {
        RequireOwner();
        lock (gate)
        {
            failure?.Throw();
            if (state != State.Paused || !ReferenceEquals(admission, paused))
                throw new InvalidOperationException("Resume requires the current collector pause.");
            paused = null;
            state = State.Running;
            wake.Set();
        }
    }

    public Task RequestStop()
    {
        RequireOwner();
        lock (gate)
        {
            if (state is State.Stopped or State.Failed or State.StopRequested) return completion.Task;
            drainOnStop = state != State.Paused;
            pendingPause?.TrySetException(new InvalidOperationException("Stop superseded the pending collector pause."));
            pendingPause = null;
            paused = null;
            state = State.StopRequested;
            wake.Set();
            return completion.Task;
        }
    }

    public void Complete()
    {
        RequireOwner();
        var stopped = RequestStop();
        worker.GetAwaiter().GetResult();
        disposed = true;
        wake.Dispose();
        stopped.GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        if (!disposed) Complete();
    }

    private void Run(Action drain)
    {
        try
        {
            while (true)
            {
                State observed;
                bool finalDrain;
                lock (gate) { observed = state; finalDrain = drainOnStop; }
                if (observed == State.Running)
                {
                    drain();
                    wake.WaitOne(1);
                }
                else if (observed == State.PauseRequested)
                {
                    drain();
                    lock (gate)
                    {
                        if (state != State.PauseRequested) continue;
                        paused = new(checked(++sequence));
                        state = State.Paused;
                        pendingPause!.SetResult(paused);
                        pendingPause = null;
                    }
                }
                else if (observed == State.Paused) wake.WaitOne();
                else
                {
                    if (finalDrain) drain();
                    lock (gate) state = State.Stopped;
                    completion.SetResult();
                    return;
                }
            }
        }
        catch (Exception error)
        {
            lock (gate)
            {
                failure = ExceptionDispatchInfo.Capture(error);
                state = State.Failed;
                paused = null;
                pendingPause?.TrySetException(error);
                pendingPause = null;
                completion.TrySetException(error);
            }
        }
    }
}
