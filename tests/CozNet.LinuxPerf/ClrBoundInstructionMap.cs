using System.Runtime.InteropServices;

sealed class ClrNativeRange
{
    public ulong Start { get; }
    public ulong Size { get; }
    private ClrNativeRange(ulong start, ulong size) => (Start, Size) = (start, size);
    public static ClrNativeRange Parse(ulong start, ulong size)
    {
        if (start == 0 || size == 0 || size > ulong.MaxValue - start)
            throw new InvalidDataException("The bound native method requires one nonzero range without overflow.");
        return new(start, size);
    }
    public bool Contains(ulong address) => address >= Start && address - Start < Size;
}

sealed class ClrBoundInstructionMap : IInstructionMap
{
    private readonly ClrProbeLifetime lifetime;
    private readonly string symbol;
    public ClrNativeRange Range { get; }
    private ClrBoundInstructionMap(ClrProbeLifetime lifetime, string symbol, ClrNativeRange range)
        => (this.lifetime, this.symbol, Range) = (lifetime, symbol, range);
    public static ClrBoundInstructionMap Capture(ClrProbeLifetime lifetime, string symbol)
        => Capture(lifetime, symbol, ReadRange);
    internal static ClrBoundInstructionMap Capture(ClrProbeLifetime lifetime, string symbol, Func<ClrProbeBinding, ClrNativeRange> read)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(read);
        return lifetime.Execute((Lifetime: lifetime, Symbol: symbol, Read: read), static value =>
            new ClrBoundInstructionMap(value.Lifetime, value.Symbol, value.Read(value.Lifetime.Binding)
                ?? throw new InvalidDataException("The CLR did not return a native method range.")));
    }
    public string? Resolve(ulong instructionPointer) => lifetime.Execute((Map: this, Address: instructionPointer),
        static value => value.Map.Range.Contains(value.Address) ? value.Map.symbol : null);
    private static ClrNativeRange ReadRange(ClrProbeBinding binding)
    {
        if (GetRange(binding.Generation, binding.Module, binding.Function, binding.Method, binding.Sites, out nuint start, out nuint size) == 0)
            throw new InvalidDataException("The CLR native method range is not admitted for this binding.");
        return ClrNativeRange.Parse(start, size);
    }
    [DllImport("CozNet.Profiler", EntryPoint = "CozGetProbeCodeRange")]
    internal static extern int GetRange(ulong generation, nuint module, nuint function, uint method, uint sites, out nuint start, out nuint size);
}
