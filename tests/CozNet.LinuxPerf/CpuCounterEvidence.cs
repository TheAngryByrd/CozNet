readonly record struct CpuCounterReading(string SourceIdentity, ulong Epoch, bool Enabled,
    long Before, long After, long TimestampFrequency, ulong? CpuNanoseconds, string? Error);
readonly record struct CpuCounterTransition(ulong Epoch, bool Enabling, bool Completed,
    long Before, long After, long TimestampFrequency, string? Error);
sealed record CpuCounterSourceSnapshot(string SourceIdentity, string Units, bool Enabled, ulong Epoch,
    bool Overflowed, bool UnrecordedCleanup, int FailedReads, string? FirstReadFailure,
    IReadOnlyList<CpuCounterTransition> Transitions, CpuCounterReading? LastReading);

interface ICpuCounterEvidenceSource
{
    CpuCounterReading ReadCounter();
    CpuCounterSourceSnapshot DescribeCounters();
}

sealed class CpuCounterJournal
{
    private readonly TimeProvider clock;
    private readonly Func<ulong> read;
    private CpuCounterHistory transitions = CpuCounterHistory.Empty;
    private readonly int capacity;
    private ulong? highestCpu;
    private int failedReads;
    private string? firstReadFailure;
    private readonly string identity = Guid.NewGuid().ToString("N");
    public bool Enabled { get; private set; }
    public ulong Epoch { get; private set; }
    public bool Overflowed { get; private set; }
    public bool UnrecordedCleanup { get; private set; }
    public CpuCounterReading? LastReading { get; private set; }

    public CpuCounterJournal(TimeProvider clock, Func<ulong> read, int capacity = 65536)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(clock.TimestampFrequency);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        this.clock = clock;
        this.read = read;
        this.capacity = capacity;
    }

    public void Transition(bool enabling, Action operation)
    {
        if (Enabled == enabling) throw new InvalidOperationException("The CPU event transition does not change state.");
        if (transitions.Count == capacity)
        {
            Overflowed = true;
            throw new InvalidOperationException("The CPU event transition journal is full.");
        }
        long before = clock.GetTimestamp(), after = before;
        bool completed = false;
        string? error = null;
        try
        {
            operation();
            Enabled = enabling;
            if (enabling) Epoch = checked(Epoch + 1);
            completed = true;
            after = clock.GetTimestamp();
            if (after < before) throw new InvalidDataException("The CPU event transition clock regressed.");
        }
        catch (Exception failure) { error = failure.ToString(); throw; }
        finally
        {
            transitions = transitions.Append(new(Epoch, enabling, completed, before, after, clock.TimestampFrequency, error));
        }
    }

    public CpuCounterReading ReadCounter(bool propagateFailure = false)
    {
        long before = 0, after = 0;
        ulong? cpu = null;
        string? error = null;
        try
        {
            before = clock.GetTimestamp();
            cpu = read();
            after = clock.GetTimestamp();
            if (after < before) throw new InvalidDataException("The CPU counter clock regressed.");
            if (highestCpu is { } previous && cpu < previous)
                throw new InvalidDataException("The cumulative CPU counter regressed.");
        }
        catch (Exception failure)
        {
            error = failure.ToString();
            failedReads++;
            firstReadFailure ??= error;
            if (propagateFailure) throw;
        }
        finally
        {
            if (cpu is { } observed && (highestCpu is null || observed > highestCpu)) highestCpu = observed;
            LastReading = new(identity, Epoch, Enabled, before, after, clock.TimestampFrequency, cpu, error);
        }
        return LastReading!.Value;
    }

    public void MarkUnrecordedCleanup() => UnrecordedCleanup = true;

    public CpuCounterSourceSnapshot Snapshot() => new(identity, "task-clock-nanoseconds", Enabled, Epoch,
        Overflowed, UnrecordedCleanup, failedReads, firstReadFailure,
        transitions, LastReading);
}

sealed class EnabledCpuCountRange
{
    public ulong Lower { get; }
    public ulong Upper { get; }
    private EnabledCpuCountRange(ulong lower, ulong upper) { Lower = lower; Upper = upper; }

    public static (EnabledCpuCountRange? Value, string? Error) Parse(
        CpuCounterReading? startBefore, CpuCounterReading? startAfter,
        CpuCounterReading? endBefore, CpuCounterReading? endAfter,
        MeasurementWindowCapture.Boundary? start, MeasurementWindowCapture.Boundary? end)
    {
        if (startBefore is null || startAfter is null || endBefore is null || endAfter is null || start is null || end is null)
            return (null, "CPU counter readings or measurement endpoints are missing.");
        var readings = new[] { startBefore.Value, startAfter.Value, endBefore.Value, endAfter.Value };
        foreach (var reading in readings)
        {
            if (reading.Error is not null || reading.CpuNanoseconds is null || string.IsNullOrWhiteSpace(reading.SourceIdentity))
                return (null, "CPU counter evidence is unavailable or failed.");
            if (reading.Before < 0 || reading.After < reading.Before || reading.TimestampFrequency <= 0 ||
                reading.TimestampFrequency != start.TimestampFrequency || reading.TimestampFrequency != end.TimestampFrequency)
                return (null, "CPU counter clock evidence is inconsistent.");
            if (reading.SourceIdentity != readings[0].SourceIdentity)
                return (null, "CPU counter readings refer to different event instances.");
        }
        for (int index = 1; index < readings.Length; index++)
            if (readings[index].Before < readings[index - 1].After || readings[index].CpuNanoseconds < readings[index - 1].CpuNanoseconds ||
                readings[index].Epoch < readings[index - 1].Epoch)
                return (null, "CPU counter readings regressed or overlap.");
        if (readings[0].After > start.BeforeTimestamp || readings[1].Before < start.AfterTimestamp ||
            readings[2].After > end.BeforeTimestamp || readings[3].Before < end.AfterTimestamp)
            return (null, "CPU counter readings do not enclose the measurement endpoints.");
        return (new(readings[2].CpuNanoseconds!.Value - readings[1].CpuNanoseconds!.Value,
            readings[3].CpuNanoseconds!.Value - readings[0].CpuNanoseconds!.Value), null);
    }
}
