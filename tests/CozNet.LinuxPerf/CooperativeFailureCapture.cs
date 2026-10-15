using System.Diagnostics;

enum CooperativeFailureCaptureState { NoRequest, Captured, Failed }

sealed class CooperativeFailureCapture
{
    public string Failure { get; }
    public long Started { get; }
    public long Finished { get; }
    public long TimestampFrequency => Stopwatch.Frequency;
    public CooperativeFailureCaptureState State { get; }
    public CooperativeRawCapture? Raw { get; }
    public string? CaptureFailure { get; }

    private CooperativeFailureCapture(string failure, long started, long finished, CooperativeFailureCaptureState state,
        CooperativeRawCapture? raw, string? captureFailure)
    {
        Failure = failure;
        Started = started;
        Finished = finished;
        State = state;
        Raw = raw;
        CaptureFailure = captureFailure;
    }

    public static CooperativeFailureCapture Capture(Exception failure, Func<CooperativeRawCapture?> read)
    {
        return CaptureAll(failure, read)[0];
    }

    public static IReadOnlyList<CooperativeFailureCapture> CaptureAll(
        Exception failure, params Func<CooperativeRawCapture?>[] readers)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentNullException.ThrowIfNull(readers);
        foreach (var reader in readers)
            ArgumentNullException.ThrowIfNull(reader);

        var observations = new (long Started, long Finished, CooperativeRawCapture? Raw, Exception? Error)[readers.Length];
        for (int index = 0; index < readers.Length; ++index)
            observations[index] = Read(readers[index]);

        string original = failure.ToString();
        var captures = new CooperativeFailureCapture[observations.Length];
        for (int index = 0; index < observations.Length; ++index)
        {
            var observation = observations[index];
            var state = observation.Error is not null ? CooperativeFailureCaptureState.Failed
                : observation.Raw is null ? CooperativeFailureCaptureState.NoRequest
                : CooperativeFailureCaptureState.Captured;
            captures[index] = new(original, observation.Started, observation.Finished,
                state, observation.Raw, observation.Error?.ToString());
        }
        return Array.AsReadOnly(captures);
    }

    private static (long Started, long Finished, CooperativeRawCapture? Raw, Exception? Error) Read(
        Func<CooperativeRawCapture?> read)
    {
        long started = Stopwatch.GetTimestamp();
        CooperativeRawCapture? raw = null;
        Exception? captureFailure = null;
        try
        {
            raw = read();
        }
        catch (Exception error)
        {
            captureFailure = error;
        }
        long finished = Stopwatch.GetTimestamp();
        return (started, finished, raw, captureFailure);
    }
}
