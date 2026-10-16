using CozNet;

sealed class MeasurementLedgerReading
{
    internal LedgerThread Thread { get; }
    public long Global { get; }
    public long Local { get; }
    public string Phase { get; }
    public Guid? ServiceLease { get; }
    public long AcceptedChildren { get; }
    public ulong LastSequence { get; }
    public long UnsettledWait { get; }

    private MeasurementLedgerReading(LedgerObservation ledger, Guid? lease, long children, ulong sequence, long wait)
    {
        Thread = ledger.Thread;
        Global = DelayTicksModule.value(ledger.Global);
        Local = DelayTicksModule.value(ledger.Local);
        Phase = ledger.Phase.IsDelaying ? "delaying" : "runnable";
        ServiceLease = lease;
        AcceptedChildren = children;
        LastSequence = sequence;
        UnsettledWait = wait;
    }

    public static MeasurementLedgerReading Capture(SignalThreadSnapshot snapshot,
        TimerServiceController.WorkerAccounting controller)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(controller);
        if (snapshot.Operation is SignalOperationSnapshot.Service service)
        {
            var lease = SignalServiceLeaseModule.identity(service.lease);
            if (!snapshot.Ledger.Phase.IsDelaying || controller.Pending is not { LedgerSettled: false } pending
                || pending.Lease != lease || pending.AcceptedChildren != service.progress.Count
                || pending.ConfirmedWait != service.progress.MeasuredNanoseconds)
                throw new InvalidDataException("The frozen service ledger does not match controller ownership.");
            return new(snapshot.Ledger, lease, service.progress.Count, service.progress.LastSequence,
                service.progress.MeasuredNanoseconds);
        }
        if (!snapshot.Operation.IsIdle || !snapshot.Ledger.Phase.IsRunnable
            || controller.Pending is { LedgerSettled: false })
            throw new InvalidDataException("The frozen timer ledger has unsupported or unsettled ownership.");
        return new(snapshot.Ledger, null, 0, 0, 0);
    }

    internal static MeasurementLedgerReading CaptureCooperative(SignalThreadSnapshot snapshot,
        Guid? lease, long children, long wait, bool settled)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (children < 0 || wait < 0 || lease == Guid.Empty ||
            (lease is null && (children != 0 || wait != 0 || settled)))
            throw new InvalidDataException("Cooperative measurement ownership is invalid.");
        if (snapshot.Operation is SignalOperationSnapshot.Service service)
        {
            if (!snapshot.Ledger.Phase.IsDelaying || settled || lease != SignalServiceLeaseModule.identity(service.lease)
                || children != service.progress.Count || wait != service.progress.MeasuredNanoseconds)
                throw new InvalidDataException("Cooperative measurement ownership differs from the accepted ledger prefix.");
            return new(snapshot.Ledger, lease, children, service.progress.LastSequence, wait);
        }
        if (!snapshot.Operation.IsIdle || !snapshot.Ledger.Phase.IsRunnable || (lease is not null && !settled))
            throw new InvalidDataException("Cooperative measurement has unsupported or unsettled ownership.");
        return new(snapshot.Ledger, null, 0, 0, 0);
    }

    internal bool Matches(long global, TimerServiceController.WorkerAccounting? controller) =>
        controller is not null && Global == global && (ServiceLease is { } lease
            ? controller.Pending is { LedgerSettled: false } pending && pending.Lease == lease
                && pending.AcceptedChildren == AcceptedChildren && pending.ConfirmedWait == UnsettledWait
            : controller.Pending is null or { LedgerSettled: true });
}
