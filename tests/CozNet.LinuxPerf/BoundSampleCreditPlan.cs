sealed class BoundSampleCreditPlan
{
    public const ulong NominalPeriodNanoseconds = 1000000;
    public bool NominalEvents { get; }
    public string Name => NominalEvents ? "nominal-event-v1" : "same-epoch-interval-v1";
    private BoundSampleCreditPlan(bool nominalEvents) => NominalEvents = nominalEvents;

    public static BoundSampleCreditPlan Parse(string? value, bool pausableSource)
    {
        bool nominal = value switch
        {
            null or "interval" => false,
            "nominal-event" => true,
            _ => throw new ArgumentException("COZNET_SAMPLE_CREDIT must be interval or nominal-event.")
        };
        if (nominal && !pausableSource) throw new ArgumentException("Nominal event credit requires the selected-payment source.");
        return new(nominal);
    }
}
