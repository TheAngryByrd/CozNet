sealed class TimerInitialInterval
{
    public ulong Nanoseconds { get; }
    private TimerInitialInterval(ulong nanoseconds) => Nanoseconds = nanoseconds;
    public static TimerInitialInterval Default { get; } = new(1000000);
    public static TimerInitialInterval Parse(string value) => value switch
    {
        "1ms" => Default,
        "10us" => new(10000),
        _ => throw new ArgumentException("Select 1ms or 10us for the initial timer interval.", nameof(value))
    };
}
