enum CooperativeServiceActivity { Skipped, Idle, Pending, Receipt, Submit, ReceiptAndSubmit }

readonly record struct CooperativeServiceTimingBucket(CooperativeServiceActivity Activity, long Calls, long Failures,
    long ElapsedTicks, long MaximumTicks);

sealed class CooperativeServiceTiming
{
    private readonly int owner = Environment.CurrentManagedThreadId;
    private readonly CooperativeServiceTimingBucket[] all = Empty();
    private readonly CooperativeServiceTimingBucket[] measurement = Empty();
    public IReadOnlyList<CooperativeServiceTimingBucket> All => all.ToArray();
    public IReadOnlyList<CooperativeServiceTimingBucket> Measurement => measurement.ToArray();

    private static CooperativeServiceTimingBucket[] Empty() => Enum.GetValues<CooperativeServiceActivity>()
        .Select(activity => new CooperativeServiceTimingBucket(activity, 0, 0, 0, 0)).ToArray();

    public static CooperativeServiceTiming? Parse(string? value, bool finiteMeasurement) => value switch
    {
        null or "0" => null,
        "1" when finiteMeasurement => new(),
        "1" => throw new ArgumentException("Service timing requires finite measurement."),
        _ => throw new ArgumentException("COZNET_SERVICE_TIMING must be 0 or 1.")
    };

    public void Record(CooperativeServiceActivity activity, long started, long finished, bool failed, bool measured)
    {
        if (Environment.CurrentManagedThreadId != owner)
            throw new InvalidOperationException("Service timing requires its owning thread.");
        if (!Enum.IsDefined(activity) || started < 0 || finished < started)
            throw new ArgumentException("Service timing has an invalid activity or interval.");
        long elapsed = finished - started;
        int index = (int)activity;
        var next = Add(all[index], elapsed, failed);
        var nextMeasured = measured ? Add(measurement[index], elapsed, failed) : measurement[index];
        all[index] = next;
        measurement[index] = nextMeasured;
    }

    private static CooperativeServiceTimingBucket Add(CooperativeServiceTimingBucket previous, long elapsed, bool failed)
        => new(previous.Activity, checked(previous.Calls + 1), checked(previous.Failures + (failed ? 1 : 0)),
            checked(previous.ElapsedTicks + elapsed), Math.Max(previous.MaximumTicks, elapsed));
}
