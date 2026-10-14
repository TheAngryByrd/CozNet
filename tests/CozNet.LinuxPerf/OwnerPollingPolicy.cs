sealed class OwnerPollingPolicy
{
    public static OwnerPollingPolicy Yielding { get; } = new("yield", 0, 1);
    public static OwnerPollingPolicy Bounded { get; } = new("poll-64", 1, 64);
    public string Name { get; }
    public int NativeValue { get; }
    public int IterationsBeforeYield { get; }
    private OwnerPollingPolicy(string name, int nativeValue, int iterations)
        => (Name, NativeValue, IterationsBeforeYield) = (name, nativeValue, iterations);
    public static OwnerPollingPolicy Parse(string value) => value switch
    {
        "yield" => Yielding,
        "poll-64" => Bounded,
        _ => throw new ArgumentException("Select yield or poll-64 for native owner polling.")
    };
}
