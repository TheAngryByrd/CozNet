using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

[StructLayout(LayoutKind.Sequential)]
struct TimerNativeChild
{
    public ulong Ordinal, Requested, Armed, Started, Finished, Waited, Ip, WaitStatus, Valid;
}

[StructLayout(LayoutKind.Sequential)]
struct TimerNativeSnapshot
{
    public ulong Generation, Command, Budget, Children, Measured, Visibility, Stop, Error;
    public long TransportError;
    public ulong MayHaveDelivery, Captured;
    public long BackendError;
}

readonly record struct TimerClockRead(int Status, ulong Nanoseconds);
sealed record TimerNativeCapture(TimerNativeSnapshot Header, TimerNativeChild[] Children, TimerClockRead Clock);
sealed record TimerObservation(TimerNativeCapture Raw, IReadOnlyList<TargetLeaseReceivedChild> Received);

sealed class TimerSessionHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public TimerSessionHandle() : base(true) { }
    protected override bool ReleaseHandle() => false;
}

static unsafe partial class TimerNativeMethods
{
    private const string Library = "CozNet.Timer";
    [LibraryImport(Library, EntryPoint = "coz_timer_open")]
    internal static partial int Open(ulong begin, ulong size, uint targets, uint capacity, out TimerSessionHandle session);
    [LibraryImport(Library, EntryPoint = "coz_timer_register")]
    internal static partial int Register(TimerSessionHandle session, uint slot);
    [LibraryImport(Library, EntryPoint = "coz_timer_retire")]
    internal static partial int Retire(TimerSessionHandle session, uint slot);
    [LibraryImport(Library, EntryPoint = "coz_timer_submit")]
    internal static partial int Submit(TimerSessionHandle session, uint slot, ulong command, ulong budget, out ulong generation);
    [LibraryImport(Library, EntryPoint = "coz_timer_cancel")]
    internal static partial int Cancel(TimerSessionHandle session, uint slot, ulong generation);
    [LibraryImport(Library, EntryPoint = "coz_timer_snapshot")]
    internal static partial int Snapshot(TimerSessionHandle session, uint slot, ulong generation,
        out TimerNativeSnapshot snapshot, TimerNativeChild* children, uint capacity);
    [LibraryImport(Library, EntryPoint = "coz_timer_release")]
    internal static partial int Release(TimerSessionHandle session, uint slot, ulong generation);
    [LibraryImport(Library, EntryPoint = "coz_timer_clock")]
    internal static partial int Clock(TimerSessionHandle session, out ulong nanoseconds);
    [LibraryImport(Library, EntryPoint = "coz_timer_close")]
    internal static partial int Close(TimerSessionHandle session);
}

sealed class NativeTimerClient
{
    private readonly TimerSessionHandle session;
    private readonly TimerNativeChild[][] buffers;
    private readonly TargetLeaseReceipts?[] trackers;
    private readonly ulong[] generations;
    public TimerNativeCapture? LastRaw { get; private set; }
    public TimerNativeSnapshot? LastCleanup { get; private set; }
    public (int Status, ulong Generation)? LastSubmission { get; private set; }

    internal static void VerifyLayout()
    {
        if (Marshal.SizeOf<TimerNativeChild>() != 72 || Marshal.OffsetOf<TimerNativeChild>(nameof(TimerNativeChild.Valid)).ToInt64() != 64
            || Marshal.SizeOf<TimerNativeSnapshot>() != 96 || Marshal.OffsetOf<TimerNativeSnapshot>(nameof(TimerNativeSnapshot.BackendError)).ToInt64() != 88)
            throw new InvalidOperationException("The timer ABI layout differs.");
    }

    public NativeTimerClient(ulong begin, ulong size, uint targets = 1, uint capacity = 64)
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("The timer backend requires Linux x64.");
        VerifyLayout();
        if (targets is 0 or > 64 || capacity is 0 or > 100000 || targets > 1000000 / capacity)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        buffers = Enumerable.Range(0, (int)targets).Select(_ => new TimerNativeChild[capacity]).ToArray();
        trackers = new TargetLeaseReceipts[targets];
        generations = new ulong[targets];
        Check(TimerNativeMethods.Open(begin, size, targets, capacity, out session), "open");
    }

    public void RegisterCurrentThread(uint slot) => Check(TimerNativeMethods.Register(session, slot), "register");
    public void RetireCurrentThread(uint slot) => Check(TimerNativeMethods.Retire(session, slot), "retire");
    public ulong Submit(uint slot, ulong command, ulong budget)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, (uint)buffers.Length);
        var tracker = TargetLeaseReceipts.Create(1, command, budget);
        int result = TimerNativeMethods.Submit(session, slot, command, budget, out ulong generation);
        LastSubmission = (result, generation);
        if (generation != 0)
        {
            generations[slot] = generation;
            trackers[slot] = generation == 1 ? tracker : TargetLeaseReceipts.Create(generation, command, budget);
        }
        Check(result, "submit");
        return generation;
    }
    public void Cancel(uint slot) => Check(TimerNativeMethods.Cancel(session, slot, Generation(slot)), "cancel");

    public unsafe TimerNativeSnapshot Probe(uint slot)
    {
        int result = TimerNativeMethods.Snapshot(session, slot, Generation(slot), out var header, null, 0);
        if (result != 105) Check(result, "probe");
        return header;
    }

    public unsafe TimerObservation Read(uint slot, Func<TimerSessionHandle, TimerClockRead>? readClock = null)
    {
        ulong generation = Generation(slot);
        var buffer = buffers[slot];
        TimerNativeSnapshot header;
        fixed (TimerNativeChild* children = buffer)
            Check(TimerNativeMethods.Snapshot(session, slot, generation, out header, children, (uint)buffer.Length), "snapshot");
        TimerClockRead clock = readClock is null ? ReadClock(session) : readClock(session);
        if (header.Children > (ulong)buffer.Length) throw new InvalidDataException("The native child count exceeds the caller buffer.");
        LastRaw = new(header, buffer.AsSpan(0, (int)header.Children).ToArray(), clock);
        var rawHeader = ToRaw(header);
        var rawChildren = LastRaw.Children.Select(ToRaw).ToArray();
        var received = trackers[slot]!.Observe(rawHeader, rawChildren, clock.Nanoseconds, clock.Status);
        return new(LastRaw, received);
    }

    public unsafe void ReleaseAfterRetiredCopy(uint slot)
    {
        ulong generation = Generation(slot);
        var buffer = buffers[slot];
        fixed (TimerNativeChild* children = buffer)
        {
            Check(TimerNativeMethods.Snapshot(session, slot, generation, out var header, children, (uint)buffer.Length), "cleanup snapshot");
            LastCleanup = header;
        }
        Check(TimerNativeMethods.Release(session, slot, generation), "release");
        generations[slot] = 0;
    }
    public void CloseAfterThreadJoin()
    {
        Check(TimerNativeMethods.Close(session), "close");
        session.SetHandleAsInvalid();
    }
    public TargetLeaseReceipts Receipts(uint slot) => trackers[slot] ?? throw new InvalidOperationException("The target has no receipt tracker.");
    private ulong Generation(uint slot)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, (uint)generations.Length);
        return generations[slot] != 0 ? generations[slot] : throw new InvalidOperationException("The target has no outstanding generation.");
    }
    private static TimerClockRead ReadClock(TimerSessionHandle session)
    {
        int status = TimerNativeMethods.Clock(session, out ulong nanoseconds);
        return new(status, nanoseconds);
    }
    private static TargetLeaseRawHeader ToRaw(TimerNativeSnapshot header)
    {
        if (header.Visibility > 3 || header.Stop > 5 || header.Error > 6 || header.MayHaveDelivery > 1
            || header.TransportError is < int.MinValue or > int.MaxValue)
            throw new InvalidDataException("The native timer header has an invalid discriminator or transport error.");
        return new(header.Generation, header.Command, header.Budget, header.Children, header.Measured,
            (int)header.Visibility, (int)header.Stop, (int)header.Error, (int)header.TransportError, header.MayHaveDelivery != 0, header.Captured);
    }
    private static TargetLeaseRawChild ToRaw(TimerNativeChild child)
    {
        if (child.WaitStatus > 2 || child.Valid > 1) throw new InvalidDataException("The native timer child has an invalid discriminator.");
        return new(child.Ordinal, child.Requested, child.Armed, child.Started, child.Finished, child.Waited, child.Ip, (int)child.WaitStatus, child.Valid != 0);
    }
    private static void Check(int status, string operation)
    {
        if (status != 0) throw new InvalidOperationException($"Timer {operation} failed with native status {status}.");
    }
}
