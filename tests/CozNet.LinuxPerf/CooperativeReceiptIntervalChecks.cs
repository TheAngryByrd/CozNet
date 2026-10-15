using static CooperativeManagedChecks;

static class CooperativeReceiptIntervalChecks
{
    public static void Run()
    {
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
}
