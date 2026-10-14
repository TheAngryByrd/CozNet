sealed class TimerWorkerLifetime
{
    private Exception? error;
    private bool retired;
    public Exception? Error => Volatile.Read(ref error);
    public bool Retired => Volatile.Read(ref retired);
    public bool Joined { get; private set; }
    public bool Released { get; private set; }

    private void Record(Exception failure)
    {
        var previous = Error;
        Volatile.Write(ref error, previous is null ? failure : new AggregateException(previous, failure));
    }

    public void Run(Action register, Action ready, Action body, Action retire, Action retain)
    {
        try
        {
            register();
            ready();
            body();
        }
        catch (Exception failure) { Record(failure); }
        finally
        {
            ready();
            try
            {
                retire();
                Volatile.Write(ref retired, true);
            }
            catch (Exception failure)
            {
                Record(failure);
                retain();
            }
        }
    }

    public void StopAndJoin(Action stop, Func<bool> join, Action disposeReady, Action release)
    {
        if (!Released)
        {
            stop();
            Joined = join();
            if (!Joined || !Retired)
                throw new InvalidDataException("Selected timer cleanup remains unresolved. Native ownership was retained.", Error);
            disposeReady();
            release();
            Released = true;
        }
        if (Error is { } failure)
            throw new InvalidDataException("The selected timer target failed during cleanup.", failure);
    }
}
