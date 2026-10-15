sealed class BoundSamplePublication
{
    public bool BatchServiceEnabled { get; }
    public long ServiceCalls { get; private set; }

    private BoundSamplePublication(bool batchServiceEnabled) => BatchServiceEnabled = batchServiceEnabled;

    public static BoundSamplePublication Parse(string? value) => value switch
    {
        null or "0" => new(false),
        "1" => new(true),
        _ => throw new ArgumentException("COZNET_BATCH_SERVICE must be 0 or 1.")
    };

    public void Publish<T>(IReadOnlyList<T> samples, int offset, Action<T> process, Action service)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(service);
        if (offset < 0 || offset > samples.Count) throw new ArgumentOutOfRangeException(nameof(offset));
        int end = samples.Count;
        for (int index = offset; index < end; ++index)
        {
            process(samples[index]);
            if (!BatchServiceEnabled) Service(service);
        }
        if (BatchServiceEnabled && offset < end) Service(service);
    }

    private void Service(Action service)
    {
        ServiceCalls = checked(ServiceCalls + 1);
        service();
    }
}
