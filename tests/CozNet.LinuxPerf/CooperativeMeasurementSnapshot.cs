using CozNet;

sealed class CooperativeMeasurementSnapshot
{
    internal sealed class Ownership
    {
        public Guid Lease { get; }
        public ulong Command { get; }
        public ulong Generation { get; }
        public long Budget { get; }
        public long AcceptedChildren { get; }
        public long ConfirmedWait { get; }
        public bool LedgerSettled { get; }

        private Ownership(Guid lease, ulong command, ulong generation, long budget, long children, long wait, bool settled)
            => (Lease, Command, Generation, Budget, AcceptedChildren, ConfirmedWait, LedgerSettled)
                = (lease, command, generation, budget, children, wait, settled);

        public static Ownership Parse(SignalServiceLease lease, ulong command, ulong generation,
            long children, long wait, bool settled)
        {
            ArgumentNullException.ThrowIfNull(lease);
            if (command == 0 || generation > uint.MaxValue || children < 0 || wait < 0
                || (generation == 0 && (children != 0 || wait != 0 || settled)))
                throw new InvalidDataException("Cooperative boundary ownership has invalid identity or prefix counters.");
            return new(SignalServiceLeaseModule.identity(lease), command, generation,
                SignalServiceLeaseModule.nanoseconds(lease), children, wait, settled);
        }
    }

    public Ownership? Selected { get; }
    public Ownership? Peer { get; }
    public long ReleasedJournalCount { get; }
    private readonly MeasurementWindowCapture.Worker selected;
    private readonly MeasurementWindowCapture.Worker peer;

    private CooperativeMeasurementSnapshot(Ownership? selectedOwnership, Ownership? peerOwnership, long journals,
        MeasurementWindowCapture.Worker selected, MeasurementWindowCapture.Worker peer)
        => (Selected, Peer, ReleasedJournalCount, this.selected, this.peer)
            = (selectedOwnership, peerOwnership, journals, selected, peer);

    public static MeasurementWindowCapture.Readings Capture(CooperativeDebtSnapshot ledger,
        long selectedProgress, long peerProgress, long selectedReleased, long peerReleased, long journals,
        Ownership? selectedOwnership, Ownership? peerOwnership, long credit, ulong sampleSequence, ulong epoch)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentOutOfRangeException.ThrowIfNegative(journals);
        MeasurementWindowCapture.Worker Worker(SignalThreadSnapshot snapshot, long progress, long released, Ownership? ownership)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(progress);
            ArgumentOutOfRangeException.ThrowIfNegative(released);
            var reading = MeasurementLedgerReading.CaptureCooperative(snapshot, ownership?.Lease,
                ownership?.AcceptedChildren ?? 0, ownership?.ConfirmedWait ?? 0, ownership?.LedgerSettled ?? false);
            return new(progress, released, ownership?.ConfirmedWait ?? 0,
                ownership is null ? null : new(ownership.Command, ownership.Generation), reading);
        }
        var selected = Worker(ledger.SelectedState, selectedProgress, selectedReleased, selectedOwnership);
        var peer = Worker(ledger.PeerState, peerProgress, peerReleased, peerOwnership);
        if (credit < 0 || epoch == 0
            || selected.Ledger!.Local != checked(credit + selectedReleased + (selectedOwnership is { LedgerSettled: true } ? selectedOwnership.ConfirmedWait : 0))
            || peer.Ledger!.Local != checked(peerReleased + (peerOwnership is { LedgerSettled: true } ? peerOwnership.ConfirmedWait : 0)))
            throw new InvalidDataException("Cooperative frozen local credit differs from released and settled payment.");
        var ownership = new CooperativeMeasurementSnapshot(selectedOwnership, peerOwnership, journals, selected, peer);
        return new(ledger.Global, credit, sampleSequence, epoch, selected, peer, Cooperative: ownership);
    }

    internal bool Matches(MeasurementWindowCapture.Readings readings) => readings.Timer is null
        && readings.Selected == selected && readings.Peer == peer
        && selected.Ledger!.Global == readings.Global && peer.Ledger!.Global == readings.Global
        && !selected.Ledger.Thread.Equals(peer.Ledger.Thread);
}
