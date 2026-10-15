using static CooperativeManagedChecks;

static class CooperativeProgressCaptureChecks
{
    public static void Run()
    {
        NativeCooperativeClient.VerifyLayout();
        QueryRetainsIdentityAndBracket();
        QueryFailureRetainsDiagnosticError();
        InvalidMarkersRemainDiagnosticFailures();
    }
    private static void QueryRetainsIdentityAndBracket()
    {
        foreach (ulong phase in Enumerable.Range(1, 10).Select(value => (ulong)value))
        {
            var raw = new CooperativeNativeProgress { Generation = 3, Ordinal = phase == 1 ? 0UL : 1UL, Phase = phase };
            var capture = CooperativeProgressCapture.Read(3, () => (0, raw));
            Require(capture.Started > 0 && capture.Finished >= capture.Started && capture.Status == 0 &&
                capture.Snapshot == raw && capture.Failure is null, "Progress capture changed a valid marker or timestamp bracket.");
        }
        foreach (int status in new[] { 1, 8 })
        {
            var capture = CooperativeProgressCapture.Read(3, () => (status, default));
            Require(capture.Status == status && capture.Failure is null && capture.Snapshot == default(CooperativeNativeProgress),
                "Progress capture lost an unavailable native result.");
        }
    }
    private static void QueryFailureRetainsDiagnosticError()
    {
        var capture = CooperativeProgressCapture.Read(3, () => throw new InvalidDataException("controlled query failure"));
        Require(capture.Started > 0 && capture.Finished >= capture.Started && capture.Status is null &&
            capture.Snapshot is null && capture.Failure!.Contains("controlled query failure"),
            "A diagnostic query exception escaped or lost its evidence.");
    }
    private static void InvalidMarkersRemainDiagnosticFailures()
    {
        foreach (ulong phase in Enumerable.Range(4, 5).Select(value => (ulong)value))
        {
            var raw = new CooperativeNativeProgress { Generation = 3, Phase = phase };
            var capture = CooperativeProgressCapture.Read(3, () => (0, raw));
            Require(capture.Failure is not null && capture.Snapshot == raw,
                "A child phase accepted ordinal zero or lost its evidence.");
        }
        foreach (var raw in new[] {
            new CooperativeNativeProgress { Generation = 4, Phase = 1 },
            new CooperativeNativeProgress { Generation = 3, Phase = 0 },
            new CooperativeNativeProgress { Generation = 3, Phase = 11 },
            new CooperativeNativeProgress { Generation = 3, Phase = 1, Ordinal = 1 },
            new CooperativeNativeProgress { Generation = 3, Phase = 4, Ordinal = 100001 } })
        {
            var capture = CooperativeProgressCapture.Read(3, () => (0, raw));
            Require(capture.Status == 0 && capture.Snapshot == raw && capture.Failure is not null,
                "An invalid marker lost its raw evidence or passed parsing.");
        }
    }
}
