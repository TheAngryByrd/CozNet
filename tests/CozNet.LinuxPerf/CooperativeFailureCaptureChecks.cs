using static CooperativeManagedChecks;

static class CooperativeFailureCaptureChecks
{
    public static void Run()
    {
        MissingRequestPreservesFailure();
        CaptureErrorPreservesOriginalFailure();
        RawFailureRemainsDiagnostic();
        CapturePrecedesExceptionFormatting();
    }

    private static void MissingRequestPreservesFailure()
    {
        var original = new InvalidDataException("debt failure");
        int calls = 0;
        var result = CooperativeFailureCapture.Capture(original, () => { ++calls; return null; });
        Require(result.State == CooperativeFailureCaptureState.NoRequest && result.Raw is null
            && result.CaptureFailure is null && result.Failure == original.ToString() && calls == 1
            && result.Finished >= result.Started && result.TimestampFrequency == System.Diagnostics.Stopwatch.Frequency,
            "A failure without a native request lost its original evidence or clock units.");
    }

    private static void CaptureErrorPreservesOriginalFailure()
    {
        var original = new InvalidDataException("debt failure");
        var captureError = new IOException("snapshot failure");
        int calls = 0;
        var result = CooperativeFailureCapture.Capture(original, () => { ++calls; throw captureError; });
        Require(result.State == CooperativeFailureCaptureState.Failed && result.Raw is null
            && result.Failure == original.ToString() && result.CaptureFailure == captureError.ToString() && calls == 1
            && result.Finished >= result.Started, "A diagnostic error replaced the original failure or retried capture.");
    }

    private static void RawFailureRemainsDiagnostic()
    {
        var original = new InvalidDataException("debt failure");
        var raw = new CooperativeRawCapture(new(), Array.Empty<CooperativeNativeChild>(), new(7, 0), 4);
        var result = CooperativeFailureCapture.Capture(original, () => raw);
        Require(result.State == CooperativeFailureCaptureState.Captured && ReferenceEquals(result.Raw, raw)
            && result.CaptureFailure is null && result.Failure == original.ToString(),
            "Raw native failure evidence was discarded or promoted to an accepted receipt.");
    }

    private sealed class ObservedException(Func<bool> captured) : Exception
    {
        public override string ToString()
        {
            Require(captured(), "Exception formatting delayed native capture.");
            return "original failure";
        }
    }

    private static void CapturePrecedesExceptionFormatting()
    {
        bool captured = false;
        var result = CooperativeFailureCapture.Capture(new ObservedException(() => captured), () => { captured = true; return null; });
        Require(result.Failure == "original failure", "Capture lost the original formatted failure.");
    }
}
