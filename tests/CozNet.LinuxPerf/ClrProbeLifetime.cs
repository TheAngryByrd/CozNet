using System.Runtime.InteropServices;

sealed class ClrProbeBinding
{
    public ulong Generation { get; }
    public nuint Module { get; }
    public nuint Function { get; }
    public uint Method { get; }
    public uint Sites { get; }
    private ClrProbeBinding(ulong generation, nuint module, nuint function, uint method, uint sites)
        => (Generation, Module, Function, Method, Sites) = (generation, module, function, method, sites);
    public static ClrProbeBinding Parse(ulong generation, nuint module, nuint function, uint method, uint sites)
    {
        if (generation == 0 || module == 0 || function == 0 || (method & 0xff000000) != 0x06000000
            || (method & 0x00ffffff) == 0 || sites != 2)
            throw new InvalidDataException("The controlled CLR binding requires a generation, module, function, method token, and two sites.");
        return new(generation, module, function, method, sites);
    }
    public static ClrProbeBinding ReadCurrent()
    {
        if (ClrProbeNative.GetBinding(out ulong generation, out nuint module, out nuint function, out uint method, out uint sites) == 0)
            throw new InvalidDataException("The CLR probe binding is not admitted.");
        return Parse(generation, module, function, method, sites);
    }
    public bool Matches(ulong generation, nuint module, nuint function, uint method, uint sites) =>
        Generation == generation && Module == module && Function == function && Method == method && Sites == sites;
}

static class ClrProbeNative
{
    [DllImport("CozNet.Profiler", EntryPoint = "CozGetProbeBinding")]
    internal static extern int GetBinding(out ulong generation, out nuint module, out nuint function, out uint method, out uint sites);
    internal static bool IsCurrent(ClrProbeBinding expected) =>
        GetBinding(out ulong generation, out nuint module, out nuint function, out uint method, out uint sites) != 0
        && expected.Matches(generation, module, function, method, sites);
}

sealed class ClrProbeLifetime
{
    private readonly ClrProbeBinding binding;
    private readonly Func<ClrProbeBinding, bool> isCurrent;
    private Exception? firstFailure;
    public Exception? FirstFailure => Volatile.Read(ref firstFailure);
    public ClrProbeBinding Binding => binding;
    public ClrProbeLifetime(ClrProbeBinding binding)
        : this(binding, ClrProbeNative.IsCurrent) { }
    internal ClrProbeLifetime(ClrProbeBinding binding, Func<ClrProbeBinding, bool> isCurrent)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(isCurrent);
        this.binding = binding;
        this.isCurrent = isCurrent;
    }
    public void RequireCurrent()
    {
        if (FirstFailure is not null) throw new InvalidDataException("The CLR-bound experiment has already failed.", FirstFailure);
        try
        {
            if (!isCurrent(binding)) throw new InvalidDataException("The CLR probe lifetime is no longer admitted.");
        }
        catch (Exception error) { Interlocked.CompareExchange(ref firstFailure, error, null); throw; }
        if (FirstFailure is not null) throw new InvalidDataException("The CLR-bound experiment failed on another thread.", FirstFailure);
    }
    public TResult Execute<TState, TResult>(TState state, Func<TState, TResult> action)
    {
        RequireCurrent();
        try
        {
            TResult result = action(state);
            RequireCurrent();
            return result;
        }
        catch (Exception error) { Interlocked.CompareExchange(ref firstFailure, error, null); throw; }
    }
    public void RequireOrigin(ulong generation, uint site)
    {
        if (generation != binding.Generation || site is not (1 or 2))
        {
            var error = new InvalidDataException("The injected probe arguments do not match the admitted CLR binding.");
            Interlocked.CompareExchange(ref firstFailure, error, null);
            throw error;
        }
    }
}
