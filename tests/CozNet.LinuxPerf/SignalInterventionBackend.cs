using System.Runtime.InteropServices;
using CozNet;

sealed class SignalPlan
{
    public bool Send { get; }
    public ulong DelayNanoseconds { get; }
    private SignalPlan(bool send, ulong delay) => (Send, DelayNanoseconds) = (send, delay);
    public static SignalPlan FromRequest(SignalDelayRequest request) => new(true, checked((ulong)SignalDelayRequestModule.nanoseconds(request)));
    public static SignalPlan Parse(string value) => value switch
    {
        "baseline" => new(false, 0),
        "zero" => new(true, 0),
        "delay" => new(true, 50000),
        _ => throw new ArgumentException("Select baseline, zero, or delay signal service.")
    };
}

sealed record SignalAcknowledgment(ulong Sequence, ulong Outcome, ulong InstructionPointer,
    ulong MeasuredHandlerNanoseconds, ulong WaitNanoseconds, long WaitResult);

interface IBoundedInterventionBackend
{
    string Name { get; }
    object Describe();
    bool IsEligibleAddress(ulong address);
    void RegisterCurrentThread(int slot);
    ulong Request(int slot, SignalPlan plan);
    SignalAcknowledgment? Read(int slot, ulong sequence);
    void RetireCurrentThread(int slot);
    void RestoreAfterThreadJoin();
}

sealed class LinuxSignalBackend : IBoundedInterventionBackend
{
    private readonly ulong start, size;
    public string Name => "linux-x64-signal";
    public object Describe() => new { nativeStart = start, nativeSize = size, symbol = OverheadWorkload.SelectedSymbol };
    public bool IsEligibleAddress(ulong address) => address >= start && address - start < size;
    public LinuxSignalBackend()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("The signal backend requires Linux x64.");
        (start, size) = PerfMapSnapshot.Read($"/tmp/perf-{Environment.ProcessId}.map").UniqueRange(OverheadWorkload.SelectedSymbol);
        if (Marshal.SizeOf<NativeResult>() != 48) throw new InvalidOperationException("The native signal result layout differs.");
        Check(Install(), "install");
    }
    public void RegisterCurrentThread(int slot) => Check(Register(slot, start, size), "register");
    public ulong Request(int slot, SignalPlan plan)
    {
        if (!plan.Send) throw new InvalidOperationException("The baseline does not send requests.");
        Check(RequestNative(slot, plan.DelayNanoseconds, out ulong sequence), "request");
        return sequence;
    }
    public SignalAcknowledgment? Read(int slot, ulong sequence)
    {
        int code = ReadNative(slot, sequence, out var value);
        if (code == 11) return null;
        Check(code, "read");
        if (value.Sequence != sequence || value.Outcome is < 1 or > 4)
            throw new InvalidDataException("The native signal result does not match its request.");
        return new(value.Sequence, value.Outcome, value.Ip, value.Residence, value.Wait, value.WaitResult);
    }
    public void RetireCurrentThread(int slot) => Check(Retire(slot), "retire");
    public void RestoreAfterThreadJoin() => Check(Restore(), "restore");
    private static void Check(int code, string operation)
    {
        if (code != 0) throw new InvalidOperationException($"Signal {operation} failed with native error {code}.");
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeResult
    {
        public ulong Sequence, Outcome, Ip, Residence, Wait;
        public long WaitResult;
    }
    [DllImport("CozNet.Signal", EntryPoint = "coz_signal_install", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Install();
    [DllImport("CozNet.Signal", EntryPoint = "coz_signal_register", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Register(int slot, ulong start, ulong size);
    [DllImport("CozNet.Signal", EntryPoint = "coz_signal_request", CallingConvention = CallingConvention.Cdecl)]
    private static extern int RequestNative(int slot, ulong delay, out ulong sequence);
    [DllImport("CozNet.Signal", EntryPoint = "coz_signal_read", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ReadNative(int slot, ulong sequence, out NativeResult result);
    [DllImport("CozNet.Signal", EntryPoint = "coz_signal_retire", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Retire(int slot);
    [DllImport("CozNet.Signal", EntryPoint = "coz_signal_restore", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Restore();
}
