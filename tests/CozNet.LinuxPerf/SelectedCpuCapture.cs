sealed class SelectedCpuCapture
{
    private readonly int owner = Environment.CurrentManagedThreadId;
    private readonly IThreadCpuClock clock;
    public string Status { get; private set; } = "prepared";
    public ControllerCpuSnapshot? StartReading { get; private set; }
    public ControllerCpuSnapshot? EndReading { get; private set; }
    public string? Failure { get; private set; }

    private SelectedCpuCapture(IThreadCpuClock clock)
    {
        this.clock = clock;
        _ = ControllerCpuSnapshot.Capture(clock);
    }

    public static SelectedCpuCapture? Parse(string? value, bool fixedWork, Func<IThreadCpuClock>? factory) => value switch
    {
        null or "0" => null,
        "1" when !fixedWork => throw new ArgumentException("Selected CPU capture requires finite fixed work."),
        "1" => new((factory ?? throw new PlatformNotSupportedException("No selected-thread CPU clock is available."))()),
        _ => throw new ArgumentException("COZNET_SELECTED_CPU must be 0 or 1.")
    };

    public void Start() => Observe(false);
    public void Finish() => Observe(true);

    private void Observe(bool finish)
    {
        if (Environment.CurrentManagedThreadId != owner)
            throw new InvalidOperationException("Selected CPU capture requires its owning thread.");
        try
        {
            if (Status != (finish ? "running" : "prepared"))
                throw new InvalidOperationException("Selected CPU capture has an invalid transition.");
            var reading = ControllerCpuSnapshot.Capture(clock);
            if (finish)
            {
                if (reading.CpuNanoseconds < StartReading!.CpuNanoseconds || reading.BeforeTimestamp < StartReading.AfterTimestamp)
                    throw new InvalidOperationException("Selected CPU observations regressed.");
                EndReading = reading;
                Status = "complete";
            }
            else
            {
                StartReading = reading;
                Status = "running";
            }
        }
        catch (Exception error)
        {
            Failure ??= error.ToString();
            Status = "failed";
            throw;
        }
    }
}
