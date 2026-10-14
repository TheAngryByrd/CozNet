static class ServiceReceiptTimingChecks
{
    public static void Run()
    {
        Require(ServiceReceiptTiming.Parse(1, 2, 3, 100000001, 0).UpperNanoseconds == 100000000,
            "The exact 100 ms receipt boundary must pass.");
        Require(ServiceReceiptTiming.Parse(ulong.MaxValue - 10, ulong.MaxValue - 8,
            ulong.MaxValue - 4, ulong.MaxValue, 0).UpperNanoseconds == 10, "Absolute clocks must not overflow elapsed time.");
        (ulong Started, ulong Native, ulong Captured, ulong Upper, int Error)[] invalid =
        [
            (0, 1, 2, 3, 0), (2, 1, 2, 3, 0), (1, 3, 2, 4, 0), (1, 2, 4, 3, 0),
            (1, 2, 3, 100000002, 0), (1, 2, 3, 4, 5)
        ];
        foreach (var value in invalid)
        {
            bool rejected = false;
            try { ServiceReceiptTiming.Parse(value.Started, value.Native, value.Captured, value.Upper, value.Error); }
            catch (InvalidDataException) { rejected = true; }
            Require(rejected, $"Invalid managed receipt was accepted: {value}.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
