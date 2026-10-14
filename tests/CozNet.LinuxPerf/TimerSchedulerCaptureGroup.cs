sealed class TimerSchedulerCaptureGroup(Func<ISchedulerEventSession>? factory)
{
    internal sealed record Completion(bool Stopped, SchedulerCapture?[] Captures, Exception[] Errors);

    private readonly object gate = new();
    private readonly ManualResetEventSlim stopped = new(factory is null);
    private readonly ISchedulerEventSession?[] sessions = new ISchedulerEventSession?[3];
    private Completion? completion;

    public void Register(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, sessions.Length);
        lock (gate)
        {
            if (completion is not null) throw new InvalidOperationException("Timer scheduler registration has stopped.");
            if (sessions[index] is not null) throw new InvalidOperationException("Timer scheduler slot is already registered.");
            if (factory is not null) sessions[index] = factory() ?? throw new InvalidOperationException("Timer scheduler factory returned no session.");
        }
    }

    public Completion Finish()
    {
        lock (gate)
        {
            if (completion is not null) return completion;
            var captures = new SchedulerCapture?[sessions.Length];
            var errors = new List<Exception>();
            bool allStopped = true;
            for (int index = 0; index < sessions.Length; index++)
            {
                if (sessions[index] is not { } session) continue;
                try
                {
                    var capture = session.Finish();
                    captures[index] = capture;
                    foreach (var failure in capture.Failures) errors.Add(new InvalidDataException(failure));
                    if (capture.FinalLost != 0 || capture.Records.Any(record => record.Kind == SchedulerRecordKind.Loss))
                        errors.Add(new InvalidDataException("Timer scheduler capture has missing or lost records."));
                }
                catch (Exception error) { errors.Add(error); }
                try { session.Dispose(); }
                catch (Exception error) { allStopped = false; errors.Add(error); }
            }
            completion = new(allStopped, captures, errors.ToArray());
            if (allStopped) stopped.Set();
            return completion;
        }
    }

    public void Retire(Action retire, TimeSpan timeout)
    {
        if (!stopped.Wait(timeout))
            throw new InvalidOperationException("Timer scheduler collection remains active; target ownership was retained.");
        retire();
    }
}
