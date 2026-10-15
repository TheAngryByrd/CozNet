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
        var state = captureFailure is not null ? CooperativeFailureCaptureState.Failed
            : raw is null ? CooperativeFailureCaptureState.NoRequest : CooperativeFailureCaptureState.Captured;
        return new(failure.ToString(), started, finished, state, raw, captureFailure?.ToString());
    }
}
