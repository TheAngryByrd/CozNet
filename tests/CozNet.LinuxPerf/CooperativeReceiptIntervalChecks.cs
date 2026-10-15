using static CooperativeManagedChecks;

static class CooperativeReceiptIntervalChecks
{
    public static void Run()
    {
        RejectedIntervalsRetainIdentityAndOriginalFailure();
        AcceptedIntervalsDoNotCreateFailureEvidence();
        var interval = CooperativeReceiptInterval.Parse(10, 110, 1000);
        Require(interval.Started == 10 && interval.Observed == 110 && interval.TimestampFrequency == 1000,
            "The exact 100 ms receipt boundary lost its clock evidence.");
        _ = CooperativeReceiptInterval.Parse(long.MaxValue - 1, long.MaxValue, 10);
        _ = CooperativeReceiptInterval.Parse(0, 0, 3);
        foreach (var value in new (long Start, long End, long Frequency)[]
        {
            (10, 111, 1000), (10, 9, 1000), (-1, 0, 1000), (0, 0, 0), (0, 0, -1),
            (0, 1, 3), (0, long.MaxValue, long.MaxValue)
        })
        {
            bool rejected = false;
            try { _ = CooperativeReceiptInterval.Parse(value.Start, value.End, value.Frequency); }
            catch (InvalidDataException) { rejected = true; }
            Require(rejected, $"Invalid receipt interval was accepted: {value}.");
        }
    }

    private static void AcceptedIntervalsDoNotCreateFailureEvidence()
    {
        var capture = new CooperativeReceiptIntervalCapture();
        var accepted = capture.Parse(new(2, 7, 11, 500000), 10, 110, 1000);
        Require(accepted.Started == 10 && accepted.Observed == 110 && accepted.TimestampFrequency == 1000
            && capture.Rejected is null, "An accepted receipt changed its timestamps or created rejection evidence.");
    }

    private static void RejectedIntervalsRetainIdentityAndOriginalFailure()
    {
        foreach (uint worker in new uint[] { 1, 2 })
        foreach (var value in new (long Start, long End, long Frequency)[]
        {
            (10, 111, 1000), (10, 9, 1000), (-1, 0, 1000), (0, 0, 0), (0, 0, -1),
            (0, 1, 3), (0, long.MaxValue, long.MaxValue)
        })
        {
            var capture = new CooperativeReceiptIntervalCapture();
            var request = new CooperativeReceiptRequest(worker, 7, 11, 500000);
            InvalidDataException? original = null;
            try { capture.Parse(request, value.Start, value.End, value.Frequency); }
            catch (InvalidDataException error) { original = error; }
            Require(original is not null && capture.Rejected == new CooperativeRejectedReceiptInterval(
                request, value.Start, value.End, value.Frequency), "Rejected receipt lost its request or raw clock arguments.");
            using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(capture.Rejected));
            var evidence = json.RootElement;
            Require(evidence.GetProperty("Request").GetProperty("Worker").GetUInt32() == worker
                && evidence.GetProperty("Request").GetProperty("Command").GetUInt64() == 7
                && evidence.GetProperty("Request").GetProperty("Generation").GetUInt64() == 11
                && evidence.GetProperty("Request").GetProperty("Budget").GetInt64() == 500000
                && evidence.GetProperty("Started").GetInt64() == value.Start
                && evidence.GetProperty("Observed").GetInt64() == value.End
                && evidence.GetProperty("TimestampFrequency").GetInt64() == value.Frequency,
                "Serialized rejection changed its request or raw clock arguments.");
            var retained = capture.Rejected;
            InvalidDataException? repeated = null;
            try { capture.Parse(new(2, 8, 12, 1), 0, 1, 1000); }
            catch (InvalidDataException error) { repeated = error; }
            Require(ReferenceEquals(original, repeated) && ReferenceEquals(retained, capture.Rejected),
                "A later receipt replaced the original rejection or allowed recovery.");
        }
    }
}
