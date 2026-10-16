enum CooperativeCpuActivity
{
    Drain, Skipped, Idle, Pending, Receipt, Submit, ReceiptAndSubmit
}

readonly record struct CooperativeCpuBucket(CooperativeCpuActivity Activity, long Calls, long Failures,
    long CpuNanoseconds, long MaximumNanoseconds);

sealed class CooperativeComponentCpu
{
    private readonly int owner = Environment.CurrentManagedThreadId;
    private readonly IThreadCpuClock clock;
    private readonly CooperativeCpuBucket[] all = Empty();
    private readonly CooperativeCpuBucket[] measurement = Empty();
    private long last, started;
    private bool active, measured;
    public string? Failure { get; private set; }
    public bool Active => active;
    public IReadOnlyList<CooperativeCpuBucket> All => all.ToArray();
    public IReadOnlyList<CooperativeCpuBucket> Measurement => measurement.ToArray();

    private static CooperativeCpuBucket[] Empty() => Enum.GetValues<CooperativeCpuActivity>()
        .Select(activity => new CooperativeCpuBucket(activity, 0, 0, 0, 0)).ToArray();

    private CooperativeComponentCpu(IThreadCpuClock clock)
    {
        this.clock = clock;
        last = Read();
    }

    public static CooperativeComponentCpu? Parse(string? value, bool finiteMeasurement, Func<IThreadCpuClock>? factory) => value switch
    {
        null or "0" => null,
        "1" when !finiteMeasurement => throw new ArgumentException("Component CPU capture requires finite measurement."),
        "1" => new((factory ?? throw new PlatformNotSupportedException("No component CPU clock is available."))()),
        _ => throw new ArgumentException("COZNET_COMPONENT_CPU must be 0 or 1.")
    };

    private void RequireOwner()
    {
        if (Environment.CurrentManagedThreadId != owner)
            throw new InvalidOperationException("Component CPU capture requires its owning thread.");
        if (Failure is not null) throw new InvalidOperationException("Component CPU capture has failed.");
    }

    private long Read()
    {
        long value = clock.ReadNanoseconds();
        if (value < last) throw new InvalidOperationException("Component CPU readings regressed or are negative.");
        last = value;
        return value;
    }

    public void Begin(bool inMeasurement)
    {
        RequireOwner();
        try
        {
            if (active) throw new InvalidOperationException("Component CPU regions cannot overlap.");
            started = Read();
            measured = inMeasurement;
            active = true;
        }
        catch (Exception error) { Failure = error.ToString(); throw; }
    }

    public void End(CooperativeCpuActivity activity, bool failed)
    {
        RequireOwner();
        try
        {
            if (!active || !Enum.IsDefined(activity))
                throw new InvalidOperationException("Component CPU region has no valid completion.");
            long elapsed = checked(Read() - started);
            int index = (int)activity;
            var next = Add(all[index], elapsed, failed);
            var nextMeasured = measured ? Add(measurement[index], elapsed, failed) : measurement[index];
            all[index] = next;
            measurement[index] = nextMeasured;
            active = false;
        }
        catch (Exception error) { Failure = error.ToString(); throw; }
    }

    public void EndService(CooperativeServiceActivity activity, bool failed) => End(activity switch
    {
        CooperativeServiceActivity.Skipped => CooperativeCpuActivity.Skipped,
        CooperativeServiceActivity.Idle => CooperativeCpuActivity.Idle,
        CooperativeServiceActivity.Pending => CooperativeCpuActivity.Pending,
        CooperativeServiceActivity.Receipt => CooperativeCpuActivity.Receipt,
        CooperativeServiceActivity.Submit => CooperativeCpuActivity.Submit,
        CooperativeServiceActivity.ReceiptAndSubmit => CooperativeCpuActivity.ReceiptAndSubmit,
        _ => throw new ArgumentOutOfRangeException(nameof(activity))
    }, failed);

    private static CooperativeCpuBucket Add(CooperativeCpuBucket previous, long elapsed, bool failed) => new(previous.Activity,
        checked(previous.Calls + 1), checked(previous.Failures + (failed ? 1 : 0)),
        checked(previous.CpuNanoseconds + elapsed), Math.Max(previous.MaximumNanoseconds, elapsed));
}
