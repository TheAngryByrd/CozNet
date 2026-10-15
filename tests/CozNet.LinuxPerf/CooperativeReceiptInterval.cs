sealed class CooperativeReceiptInterval
{
    public long Started { get; }
    public long Observed { get; }
    public long TimestampFrequency { get; }

    private CooperativeReceiptInterval(long started, long observed, long frequency)
        => (Started, Observed, TimestampFrequency) = (started, observed, frequency);

    public static CooperativeReceiptInterval Parse(long started, long observed, long frequency)
    {
        if (started < 0 || observed < started || frequency <= 0
            || ((Int128)observed - started) * 10 > frequency)
            throw new InvalidDataException("The cooperative request-to-receipt interval exceeds 100 ms or has invalid clock evidence.");
        return new(started, observed, frequency);
    }
}
