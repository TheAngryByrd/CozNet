using System.Diagnostics;

interface IBoundSchedulerBackend
{
    string ClockDomain { get; }
    ISchedulerEventSession StartCurrentThread();
    ulong ReadClockNanoseconds();
}

sealed record SchedulerClockObservation(long Before, ulong? Nanoseconds, long After, long Frequency, string? Failure)
{
    internal static SchedulerClockObservation Read(Func<ulong> read)
    {
        long before = Stopwatch.GetTimestamp();
        try
        {
            ulong nanoseconds = read();
            long after = Stopwatch.GetTimestamp();
            if (nanoseconds == 0 || after < before)
                return new(before, nanoseconds, after, Stopwatch.Frequency, "Invalid scheduler clock observation.");
            return new(before, nanoseconds, after, Stopwatch.Frequency, null);
        }
        catch (Exception error)
        {
            return new(before, null, Stopwatch.GetTimestamp(), Stopwatch.Frequency, error.ToString());
        }
    }
}

sealed class BoundSchedulerCapture
{
    internal sealed record Result(string ClockDomain, bool Stopped, SchedulerClockObservation? Begin,
        SchedulerClockObservation End, SchedulerCapture?[] Captures, string[] Failures);

    private readonly IBoundSchedulerBackend backend;
    private readonly SchedulerCaptureGroup group;
    private readonly string clockDomain;
    private SchedulerClockObservation? begin;
    private Result? result;

    public BoundSchedulerCapture(IBoundSchedulerBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        string domain = backend.ClockDomain;
        if (string.IsNullOrWhiteSpace(domain))
            throw new ArgumentException("Scheduler capture requires a clock domain.", nameof(backend));
        this.backend = backend;
        clockDomain = domain;
        group = new(backend.StartCurrentThread);
    }

    public void Register(int role) => group.Register(role);

    public void Begin()
    {
        if (begin is not null || result is not null)
            throw new InvalidOperationException("Scheduler observation has already started or finished.");
        begin = SchedulerClockObservation.Read(backend.ReadClockNanoseconds);
    }

    public Result Finish()
    {
        if (result is not null) return result;
        var end = SchedulerClockObservation.Read(backend.ReadClockNanoseconds);
        var completed = group.Finish();
        var failures = completed.Errors.Select(error => error.ToString()).ToList();
        if (begin is null) failures.Add("Scheduler observation did not start.");
        else if (begin.Failure is not null) failures.Add(begin.Failure);
        if (end.Failure is not null) failures.Add(end.Failure);
        if (begin?.Nanoseconds is { } first && end.Nanoseconds is { } last && last < first)
            failures.Add("Scheduler clock observations regressed.");
        result = new(clockDomain, completed.Stopped, begin, end, completed.Captures, failures.ToArray());
        return result;
    }

    public void Retire(Action retire) => group.Retire(retire, TimeSpan.Zero);
}
