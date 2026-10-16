sealed class BoundControllerPlan
{
    public bool OwnerEnabled { get; }
    private BoundControllerPlan(bool ownerEnabled) => OwnerEnabled = ownerEnabled;

    public static BoundControllerPlan Parse(string? value, bool selectedPayment, bool schedulerTiming, bool dispatchTiming)
    {
        bool enabled = value switch
        {
            null or "0" => false,
            "1" => true,
            _ => throw new ArgumentException("COZNET_OWNER_CONTROLLER must be 0 or 1.")
        };
        if (enabled && (!selectedPayment || schedulerTiming || dispatchTiming))
            throw new ArgumentException("Owner controller requires selected payment without scheduler or dispatch timing.");
        return new(enabled);
    }
}
