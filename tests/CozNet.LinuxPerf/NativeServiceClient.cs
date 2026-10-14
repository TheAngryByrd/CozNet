using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

[StructLayout(LayoutKind.Sequential)]
struct ServiceNativeAcknowledgment
{
    public ulong Sequence, Outcome, Ip, Handler, Wait;
    public long WaitResult;
    public ulong Started, Queued, HandlerStarted, HandlerFinished, Observed;
}

[StructLayout(LayoutKind.Sequential)]
struct ServiceNativeChild
{
    public ulong Ordinal, Requested;
    public ServiceNativeAcknowledgment Acknowledgment;
}

[StructLayout(LayoutKind.Sequential)]
struct ServiceNativeSnapshot
{
    public ulong IdentityFirst, IdentitySecond, Command, Budget, Measured, Phase, Error;
    public long TransportError;
    public ulong Cancellation, Outstanding, Children, Rejected, Captured;
}

sealed record ReceivedServiceChild(ServiceNativeChild Native, ulong FirstSnapshotCaptured,
    ulong FirstManagedReceiptUpper, int ClockStatus);
sealed record ServiceNativeObservation(ServiceNativeSnapshot Header, ReceivedServiceChild[] Children,
    ServiceNativeAcknowledgment[] Rejected);

sealed class ServiceSessionHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public ServiceSessionHandle() : base(true) { }
    protected override bool ReleaseHandle() => ServiceNativeMethods.CloseRaw(handle) == 0;
}

static unsafe partial class ServiceNativeMethods
{
    private const string Library = "CozNet.Service";
    [LibraryImport(Library, EntryPoint = "coz_service_open")]
    internal static partial int Open(ulong begin, ulong size, int mode, ulong capacity, out ServiceSessionHandle session);
    [LibraryImport(Library, EntryPoint = "coz_service_register")]
    internal static partial int Register(ServiceSessionHandle session, int slot);
    [LibraryImport(Library, EntryPoint = "coz_service_retire")]
    internal static partial int Retire(ServiceSessionHandle session, int slot);
    [LibraryImport(Library, EntryPoint = "coz_service_submit")]
    internal static partial int Submit(ServiceSessionHandle session, int slot, ulong first, ulong second, ulong command, ulong budget);
    [LibraryImport(Library, EntryPoint = "coz_service_cancel")]
    internal static partial int Cancel(ServiceSessionHandle session, int slot, ulong first, ulong second);
    [LibraryImport(Library, EntryPoint = "coz_service_snapshot")]
    internal static partial int Snapshot(ServiceSessionHandle session, int slot, ulong first, ulong second,
        out ServiceNativeSnapshot header, ServiceNativeChild* children, ulong childCapacity,
        ServiceNativeAcknowledgment* rejected, ulong rejectedCapacity);
    [LibraryImport(Library, EntryPoint = "coz_service_release")]
    internal static partial int Release(ServiceSessionHandle session, int slot, ulong first, ulong second);
    [LibraryImport(Library, EntryPoint = "coz_service_clock")]
    internal static partial int Clock(ServiceSessionHandle session, out ulong nanoseconds);
    [LibraryImport(Library, EntryPoint = "coz_service_close")]
    internal static partial int Close(ServiceSessionHandle session);
    [LibraryImport(Library, EntryPoint = "coz_service_close")]
    internal static partial int CloseRaw(nint session);
}

sealed class NativeServiceClient
{
    private readonly ServiceSessionHandle session;
    private readonly ServiceNativeChild[] children;
    private readonly ServiceNativeAcknowledgment[] rejected;
    private readonly List<ReceivedServiceChild>[] received = [[], []];
    private readonly Guid[] identities = new Guid[2];

    public NativeServiceClient(ulong begin, ulong size, SignalWaitMode mode, int capacity = 100000)
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("The native service transport requires Linux x64.");
        ArgumentNullException.ThrowIfNull(mode);
        if (capacity is < 1 or > 100000) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (Marshal.SizeOf<ServiceNativeAcknowledgment>() != 88 || Marshal.SizeOf<ServiceNativeChild>() != 104
            || Marshal.SizeOf<ServiceNativeSnapshot>() != 104)
            throw new InvalidOperationException("The service ABI layout differs.");
        children = new ServiceNativeChild[capacity];
        rejected = new ServiceNativeAcknowledgment[capacity];
        Check(ServiceNativeMethods.Open(begin, size, mode.NativeValue, (ulong)capacity, out session), "open");
    }

    public void RegisterCurrentThread(int slot) => Check(ServiceNativeMethods.Register(session, slot), "register");
    public void RetireCurrentThread(int slot) => Check(ServiceNativeMethods.Retire(session, slot), "retire");

    private static (ulong First, ulong Second) Parts(Guid identity)
    {
        if (identity == Guid.Empty) throw new ArgumentException("The service identity is empty.");
        Span<byte> bytes = stackalloc byte[16];
        identity.TryWriteBytes(bytes);
        return (BinaryPrimitives.ReadUInt64LittleEndian(bytes), BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]));
    }

    public void Submit(int slot, Guid identity, ulong command, ulong budget)
    {
        var parts = Parts(identity);
        Check(ServiceNativeMethods.Submit(session, slot, parts.First, parts.Second, command, budget), "submit");
        identities[slot] = identity;
        received[slot].Clear();
    }

    public void Cancel(int slot, Guid identity)
    {
        var parts = Parts(identity);
        Check(ServiceNativeMethods.Cancel(session, slot, parts.First, parts.Second), "cancel");
    }

    public unsafe ServiceNativeObservation Read(int slot, Guid identity)
    {
        if (slot is < 0 or > 1 || identities[slot] != identity)
            throw new InvalidOperationException("The service identity does not match the submitted lease.");
        var parts = Parts(identity);
        ServiceNativeSnapshot header;
        fixed (ServiceNativeChild* childPointer = children)
        fixed (ServiceNativeAcknowledgment* rejectedPointer = rejected)
            Check(ServiceNativeMethods.Snapshot(session, slot, parts.First, parts.Second, out header,
                childPointer, (ulong)children.Length, rejectedPointer, (ulong)rejected.Length), "snapshot");
        int clockStatus = ServiceNativeMethods.Clock(session, out ulong receiptUpper);
        if (header.Children > (ulong)children.Length || header.Rejected > (ulong)rejected.Length
            || header.Children < (ulong)received[slot].Count || header.IdentityFirst != parts.First || header.IdentitySecond != parts.Second)
            throw new InvalidDataException("The service snapshot identity or journal count differs.");
        while (received[slot].Count < (int)header.Children)
            received[slot].Add(new(children[received[slot].Count], header.Captured, receiptUpper, clockStatus));
        return new(header, received[slot].ToArray(), rejected.AsSpan(0, (int)header.Rejected).ToArray());
    }

    public void Release(int slot, Guid identity)
    {
        var parts = Parts(identity);
        Check(ServiceNativeMethods.Release(session, slot, parts.First, parts.Second), "release");
        identities[slot] = Guid.Empty;
    }

    public void CloseAfterThreadJoin()
    {
        Check(ServiceNativeMethods.Close(session), "close");
        session.SetHandleAsInvalid();
    }

    private static void Check(int status, string operation)
    {
        if (status != 0) throw new InvalidOperationException($"Native service {operation} failed with error {status}.");
    }
}
