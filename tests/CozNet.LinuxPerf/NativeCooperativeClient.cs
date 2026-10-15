using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

sealed class CooperativeSessionHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public CooperativeSessionHandle() : base(true) { }
    protected override bool ReleaseHandle() => false;
}
enum CooperativePollOutcome : uint { Idle, Serviced, Rejected, Failed }
sealed record CooperativeCleanupCapture(CooperativeNativeSnapshot Header, IReadOnlyList<CooperativeNativeChild> Children, int Status);

static unsafe partial class CooperativeNativeMethods
{
    private const string Library = "CozNet.Cooperative";
    [LibraryImport(Library, EntryPoint = "coz_cooperative_open")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Open(ulong version, uint targets, uint capacity, out CooperativeSessionHandle session);
    [LibraryImport(Library, EntryPoint = "coz_cooperative_register")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Register(CooperativeSessionHandle session, uint slot, ulong worker, ulong site);
    [LibraryImport(Library, EntryPoint = "coz_cooperative_poll")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Poll(CooperativeSessionHandle session, uint slot, ulong worker, ulong version, ulong site, out uint outcome);
    [LibraryImport(Library, EntryPoint = "coz_cooperative_submit")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Submit(CooperativeSessionHandle session, uint slot, ulong command, ulong budget, out ulong generation);
    [LibraryImport(Library, EntryPoint = "coz_cooperative_cancel")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Cancel(CooperativeSessionHandle session, uint slot, ulong generation);
    [LibraryImport(Library, EntryPoint = "coz_cooperative_snapshot")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Snapshot(CooperativeSessionHandle session, uint slot, ulong generation,
        out CooperativeNativeSnapshot snapshot, CooperativeNativeChild* children, uint capacity);
    [LibraryImport(Library, EntryPoint = "coz_cooperative_release")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Release(CooperativeSessionHandle session, uint slot, ulong generation);
    [LibraryImport(Library, EntryPoint = "coz_cooperative_retire")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Retire(CooperativeSessionHandle session, uint slot);
    [LibraryImport(Library, EntryPoint = "coz_cooperative_clock")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Clock(CooperativeSessionHandle session, out ulong nanoseconds);
    [LibraryImport(Library, EntryPoint = "coz_cooperative_revoke")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Revoke(CooperativeSessionHandle session);
    [LibraryImport(Library, EntryPoint = "coz_cooperative_close")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Close(CooperativeSessionHandle session);
}

sealed class NativeCooperativeClient
{
    private readonly CooperativeSessionHandle session;
    private readonly CooperativeNativeChild[][] buffers;
    private readonly CooperativeReceipts?[] trackers;
    private readonly ulong[] generations;
    private readonly ulong version;
    public CooperativeCleanupCapture? LastCleanup { get; private set; }
    public Exception? LastClockFailure { get; private set; }
    public static void VerifyLayout()
    {
        if (Marshal.SizeOf<CooperativeNativeChild>() != 88 || Marshal.OffsetOf<CooperativeNativeChild>(nameof(CooperativeNativeChild.Valid)).ToInt64() != 80
            || Marshal.SizeOf<CooperativeNativeSnapshot>() != 88 || Marshal.OffsetOf<CooperativeNativeSnapshot>(nameof(CooperativeNativeSnapshot.Captured)).ToInt64() != 80)
            throw new InvalidDataException("The cooperative ABI layout differs.");
    }
    public NativeCooperativeClient(ulong version, uint targets = 1, uint capacity = 64)
    {
        _ = CooperativeSite.Parse(1, version, 1);
        if (targets is 0 or > 64 || capacity is 0 or > 100000 || targets > 1000000 / capacity)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        VerifyLayout();
        this.version = version;
        buffers = Enumerable.Range(0, (int)targets).Select(_ => new CooperativeNativeChild[capacity]).ToArray();
        trackers = new CooperativeReceipts[targets];
        generations = new ulong[targets];
        Check(CooperativeNativeMethods.Open(version, targets, capacity, out session), "open");
    }
    public void RegisterCurrentThread(uint slot, CooperativeSite site)
    {
        ArgumentNullException.ThrowIfNull(site);
        if (site.CodeVersion != version) throw new InvalidDataException("The cooperative site does not match the session code version.");
        Check(CooperativeNativeMethods.Register(session, slot, site.Worker, site.Site), "register");
    }
    public CooperativePollOutcome Poll(uint slot, CooperativeSite observed)
    {
        ArgumentNullException.ThrowIfNull(observed);
        Check(CooperativeNativeMethods.Poll(session, slot, observed.Worker, observed.CodeVersion, observed.Site, out uint outcome), "poll");
        if (outcome > 3) throw new InvalidDataException("The native cooperative poll outcome is invalid.");
        return (CooperativePollOutcome)outcome;
    }
    public ulong Submit(uint slot, ulong command, ulong budget)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, (uint)generations.Length);
        var proposed = CooperativeLeaseIdentity.Parse(1, command, budget);
        if (generations[slot] != 0) throw new InvalidOperationException("The cooperative target still owns a generation.");
        int status = CooperativeNativeMethods.Submit(session, slot, proposed.Command, proposed.Budget, out ulong generation);
        if (generation != 0) generations[slot] = generation;
        Check(status, "submit");
        trackers[slot] = new(CooperativeLeaseIdentity.Parse(generation, command, budget));
        return generation;
    }
    public void Cancel(uint slot) => Check(CooperativeNativeMethods.Cancel(session, slot, Generation(slot)), "cancel");
    public bool TryRevoke()
    {
        int status = CooperativeNativeMethods.Revoke(session);
        if (status == 3) return false;
        Check(status, "revoke");
        return true;
    }
    public unsafe CooperativeNativeSnapshot Probe(uint slot)
    {
        int status = CooperativeNativeMethods.Snapshot(session, slot, Generation(slot), out var header, null, 0);
        if (status != 4) Check(status, "probe");
        return header;
    }
    public unsafe CooperativeObservation Read(uint slot, Func<CooperativeSessionHandle, CooperativeClockRead>? readClock = null)
    {
        ulong generation = Generation(slot);
        var buffer = buffers[slot];
        CooperativeNativeSnapshot header;
        int status;
        fixed (CooperativeNativeChild* children = buffer)
            status = CooperativeNativeMethods.Snapshot(session, slot, generation, out header, children, (uint)buffer.Length);
        CooperativeClockRead clock;
        LastClockFailure = null;
        try { clock = readClock is null ? ReadClock(session) : readClock(session); }
        catch (Exception error) { LastClockFailure = error; clock = new(7, 0); }
        int count = header.Children > (ulong)buffer.Length ? buffer.Length : (int)header.Children;
        return Receipts(slot).Observe(header, buffer.AsSpan(0, count), clock, status);
    }
    public unsafe void ReleaseAfterRetiredCopy(uint slot)
    {
        var buffer = buffers[slot];
        ulong generation = Generation(slot);
        CooperativeNativeSnapshot header;
        int status;
        fixed (CooperativeNativeChild* children = buffer)
            status = CooperativeNativeMethods.Snapshot(session, slot, generation, out header, children, (uint)buffer.Length);
        int count = header.Children > (ulong)buffer.Length ? buffer.Length : (int)header.Children;
        LastCleanup = new(header, Array.AsReadOnly(buffer.AsSpan(0, count).ToArray()), status);
        Check(status, "cleanup snapshot");
        if (header.Children > (ulong)buffer.Length) throw new InvalidDataException("The cleanup journal exceeds the caller buffer.");
        Check(CooperativeNativeMethods.Release(session, slot, generation), "release");
        generations[slot] = 0;
    }
    public void RetireCurrentThread(uint slot) => Check(CooperativeNativeMethods.Retire(session, slot), "retire");
    public void CloseAfterThreadJoin()
    {
        Check(CooperativeNativeMethods.Close(session), "close");
        session.SetHandleAsInvalid();
    }
    public CooperativeReceipts Receipts(uint slot) => trackers[slot] ?? throw new InvalidOperationException("The target has no receipt tracker.");
    public bool HasPending(uint slot) => generations[slot] != 0;
    internal static CooperativeClockRead ReadClock(CooperativeSessionHandle session)
    {
        int status = CooperativeNativeMethods.Clock(session, out ulong nanoseconds);
        return new(status, nanoseconds);
    }
    private ulong Generation(uint slot)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, (uint)generations.Length);
        return generations[slot] != 0 ? generations[slot] : throw new InvalidOperationException("The target has no outstanding generation.");
    }
    private static void Check(int status, string operation)
    {
        if (status != 0) throw new InvalidOperationException($"Cooperative {operation} failed with native status {status}.");
    }
}
