static class TimerSessionCleanup
{
    internal sealed record Result(bool NativeClosed, Exception[] Errors);

    public static Result Run(IDisposable? samples, Action closeNative, Action disposeStartGate)
    {
        var errors = new List<Exception>();
        bool closed = false;
        try { samples?.Dispose(); }
        catch (Exception error) { errors.Add(error); }
        try { closeNative(); closed = true; }
        catch (Exception error) { errors.Add(error); }
        try { disposeStartGate(); }
        catch (Exception error) { errors.Add(error); }
        return new(closed, errors.ToArray());
    }
}
