sealed class SignalWaitMode
{
    public static SignalWaitMode Sleep { get; } = new("sleep", 0);
    public static SignalWaitMode Spin { get; } = new("spin", 1);
    public string Name { get; }
    public int NativeValue { get; }
    private SignalWaitMode(string name, int nativeValue) => (Name, NativeValue) = (name, nativeValue);
    public static SignalWaitMode Parse(string value) => value switch
    {
        "sleep" => Sleep,
        "spin" => Spin,
        _ => throw new ArgumentException("Select sleep or spin signal waiting.")
    };
}
