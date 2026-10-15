sealed class BoundReceiptReadiness
{
    public bool DirectEnabled { get; }
    public long Calls { get; private set; }

    private BoundReceiptReadiness(bool enabled) => DirectEnabled = enabled;

    public static BoundReceiptReadiness Parse(string? value) => value switch
    {
        null or "0" => new(false),
        "1" => new(true),
        _ => throw new ArgumentException("COZNET_DIRECT_RECEIPT_READY must be 0 or 1.")
    };

    public bool Ready(uint slot, Func<uint, bool> direct, Func<uint, bool> controller)
    {
        if (!DirectEnabled) return controller(slot);
        Calls = checked(Calls + 1);
        return direct(slot);
    }
}
