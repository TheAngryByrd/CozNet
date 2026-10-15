using System.Buffers.Binary;
using System.Runtime.InteropServices;

sealed class ServicePathPreparation : IServiceInterventionBackend, ICpuEventSession, IInstructionMap
{
    private const int Offset = 4096, Capacity = 128;
    private readonly IntPtr mapping = Marshal.AllocHGlobal(Offset + Capacity);
    private readonly RingReader reader;
    private readonly ServiceNativeObservation?[] observations = new ServiceNativeObservation?[2];
    private ulong sequence;
    private bool disposed, enabled;
    public string Name => "synthetic-managed-initialization";
    public object Describe() => new { clock = "synthetic", liveDelivery = false };
    public ServicePathPreparation(ICpuSampleSink sink)
    {
        Marshal.Copy(new byte[Offset + Capacity], 0, mapping, Offset + Capacity);
        reader = new(mapping, Offset, Capacity, 42, this, OverheadWorkload.SelectedSymbol, sink);
        var record = new byte[48];
        BinaryPrimitives.WriteUInt32LittleEndian(record, 9);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(6), 48);
        BinaryPrimitives.WriteUInt64LittleEndian(record.AsSpan(8), 100);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(16), checked((uint)Environment.ProcessId));
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(20), 42);
        BinaryPrimitives.WriteUInt64LittleEndian(record.AsSpan(24), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(record.AsSpan(32), 80000);
        BinaryPrimitives.WriteUInt64LittleEndian(record.AsSpan(40), 80000);
        Marshal.Copy(record, 0, mapping + Offset, record.Length);
        Marshal.WriteInt64(mapping, 1024, record.Length);
    }
    public string? Resolve(ulong address) => address == 100 ? OverheadWorkload.SelectedSymbol : null;
    public bool IsEligibleAddress(ulong address) => address == 100;
    public bool IsValidOutcome(ulong outcome, long result) => outcome == 2 && result == 0;
    public void RegisterCurrentThread(int slot) => throw new NotSupportedException("Initialization has no native target threads.");
    public void RetireCurrentThread(int slot) => throw new NotSupportedException("Initialization has no native target threads.");
    public void RestoreAfterThreadJoin() { }
    public void Enable()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (enabled) throw new InvalidOperationException("Initialization sampling is already enabled.");
        enabled = true;
    }
    public ulong Disable()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!enabled) throw new InvalidOperationException("Initialization sampling is not enabled.");
        enabled = false;
        return 80000;
    }
    public SampleBatch Drain()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return reader.Drain();
    }
    public void Submit(int slot, Guid identity, ulong command, ulong budget)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (observations[slot] is not null) throw new InvalidOperationException("The initialization slot is occupied.");
        var parts = NativeServiceClient.Parts(identity);
        ulong wait = budget + (slot == 1 ? 30000UL : 0);
        ulong started = ++sequence * 1000000;
        var ack = new ServiceNativeAcknowledgment { Sequence = sequence, Outcome = 2, Ip = 100,
            Handler = wait, Wait = wait, Started = started, Queued = started + 1, HandlerStarted = started + 2,
            HandlerFinished = started + 2 + wait, Observed = started + 3 + wait };
        var child = new ReceivedServiceChild(new ServiceNativeChild { Ordinal = 1, Requested = budget,
            Acknowledgment = ack }, ack.Observed + 1, ack.Observed + 2, 0);
        observations[slot] = new(new ServiceNativeSnapshot { IdentityFirst = parts.First, IdentitySecond = parts.Second,
            Command = command, Budget = budget, Measured = wait, Phase = 2, Children = 1,
            Captured = child.FirstSnapshotCaptured + 1 }, [child], []);
    }
    public ServiceNativeObservation Read(int slot, Guid identity)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var observation = observations[slot] ?? throw new InvalidOperationException("The initialization slot is empty.");
        var parts = NativeServiceClient.Parts(identity);
        if (observation.Header.IdentityFirst != parts.First || observation.Header.IdentitySecond != parts.Second)
            throw new InvalidOperationException("The initialization identity differs.");
        return observation;
    }
    public void Release(int slot, Guid identity)
    {
        Read(slot, identity);
        observations[slot] = null;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Marshal.FreeHGlobal(mapping);
    }
}
