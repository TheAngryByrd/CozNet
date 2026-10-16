using System.Diagnostics;

sealed record CooperativeReceiptPreparationResult(long Started, long Observed, long Accepted,
    int Children, long ConfirmedNanoseconds, bool Terminal, long RemainingNanoseconds)
{
    public void RequireSettled() => CooperativeManagedChecks.Require(Children == 10
        && ConfirmedNanoseconds == 500000 && Terminal && RemainingNanoseconds == 0,
        "The receipt exercise did not settle its ten-child lease exactly.");
}

sealed class CooperativeReceiptPreparation
{
    public bool Enabled { get; }
    public CooperativeReceiptPreparationResult? LastResult { get; private set; }
    private CooperativeReceiptPreparation(bool enabled) => Enabled = enabled;

    public static CooperativeReceiptPreparation Parse(string? value, bool selectedPayment, bool finiteMeasurement)
    {
        bool enabled = value switch
        {
            null or "0" => false,
            "1" => true,
            _ => throw new ArgumentException("COZNET_PREPARE_RECEIPTS must be 0 or 1.")
        };
        if (enabled && (!selectedPayment || !finiteMeasurement))
            throw new ArgumentException("Receipt preparation requires selected payment and finite measurement.");
        return new(enabled);
    }

    public CooperativeReceiptPreparationResult? Execute(Func<Func<CooperativeReceiptPreparationResult>, CooperativeReceiptPreparationResult> dispatch)
    {
        if (!Enabled) return null;
        LastResult = null;
        LastResult = dispatch(Exercise);
        LastResult.RequireSettled();
        return LastResult;
    }

    internal static CooperativeReceiptPreparationResult Exercise()
    {
        var fixture = CooperativeManagedChecks.Reserve(1000000);
        var adapter = CooperativeManagedChecks.Adapter(fixture);
        var tracker = new CooperativeReceipts(CooperativeLeaseIdentity.Parse(1, 7, 500000));
        var children = new CooperativeNativeChild[10];
        ulong finished = 1000;
        for (int index = 0; index < children.Length; ++index)
        {
            children[index] = new()
            {
                Ordinal = (ulong)index + 1, Requested = 50000, Armed = finished,
                Started = finished + 1, Finished = finished + 50001, Waited = 50000,
                Worker = 1, CodeVersion = 2, Site = 3, Valid = 1
            };
            finished = children[index].Finished;
        }
        var header = new CooperativeNativeSnapshot
        {
            Generation = 1, Command = 7, Budget = 500000, Children = 10, Measured = 500000,
            Visibility = 3, Stop = 1, Captured = finished + 1
        };
        var clock = new CooperativeClockRead(0, finished + 2);
        long started = Stopwatch.GetTimestamp();
        var receipt = tracker.Observe(header, children, clock);
        long observed = Stopwatch.GetTimestamp();
        var accepted = adapter.Accept(receipt, fixture.State);
        long ended = Stopwatch.GetTimestamp();
        long remaining = CooperativeManagedChecks.Remaining(fixture, accepted.State);
        return new(started, observed, ended, accepted.Adapter.AcceptedCount,
            accepted.Adapter.ConfirmedNanoseconds, accepted.Adapter.IsTerminal, remaining);
    }
}
