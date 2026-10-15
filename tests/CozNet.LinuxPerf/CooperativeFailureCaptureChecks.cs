using static CooperativeManagedChecks;

static class CooperativeFailureCaptureChecks
{
    public static void Run()
    {
        MissingRequestPreservesFailure();
        CaptureErrorPreservesOriginalFailure();
        RawFailureRemainsDiagnostic();
        CapturePrecedesExceptionFormatting();
        AllReadersPrecedeExceptionFormatting();
        FailedReaderDoesNotPreventLaterCapture();
        EmptyReadersPreserveEmptyResult();
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

    private static void AllReadersPrecedeExceptionFormatting()
    {
        int calls = 0;
        var raw = new CooperativeRawCapture(new(), Array.Empty<CooperativeNativeChild>(), new(7, 0), 4);
        var captures = CooperativeFailureCapture.CaptureAll(new ObservedException(() => calls == 2),
            () => { ++calls; return raw; },
            () => { ++calls; return null; });
        Require(captures.Count == 2 && calls == 2
            && captures[0].State == CooperativeFailureCaptureState.Captured
            && ReferenceEquals(captures[0].Raw, raw) && captures[0].CaptureFailure is null
            && captures[1].State == CooperativeFailureCaptureState.NoRequest
            && captures[1].Raw is null && captures[1].CaptureFailure is null
            && captures.All(capture => capture.Failure == "original failure"),
            "Multiple captures changed reader order, evidence, or the original failure.");
        Require(captures[0].Started <= captures[0].Finished
            && captures[0].Finished <= captures[1].Started
            && captures[1].Started <= captures[1].Finished
            && captures.All(capture => capture.TimestampFrequency == System.Diagnostics.Stopwatch.Frequency),
            "Multiple captures lost their separate ordered timestamp brackets or clock units.");
    }

    private static void FailedReaderDoesNotPreventLaterCapture()
    {
        int calls = 0;
        var raw = new CooperativeRawCapture(new(), Array.Empty<CooperativeNativeChild>(), new(7, 0), 4);
        var captureError = new ObservedException(() => calls == 2);
        var captures = CooperativeFailureCapture.CaptureAll(new InvalidDataException("workload failure"),
            () => { ++calls; throw captureError; },
            () => { ++calls; return raw; });
        Require(calls == 2 && captures.Count == 2
            && captures[0].State == CooperativeFailureCaptureState.Failed
            && captures[0].Raw is null && captures[0].CaptureFailure == "original failure"
            && captures[1].State == CooperativeFailureCaptureState.Captured
            && ReferenceEquals(captures[1].Raw, raw) && captures[1].CaptureFailure is null
            && captures.All(capture => capture.Failure.Contains("workload failure", StringComparison.Ordinal)),
            "A failed reader prevented later capture or replaced the workload failure.");
    }

    private static void EmptyReadersPreserveEmptyResult()
    {
        var captures = CooperativeFailureCapture.CaptureAll(new InvalidDataException("workload failure"));
        Require(captures.Count == 0, "An empty reader set invented native evidence.");
    }
}
