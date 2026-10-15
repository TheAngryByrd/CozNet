enum MeasurementCaptureStatus { NotStarted, Active, Complete, StartFailed, FinishFailed }

sealed class MeasurementWindowCapture
{
    internal sealed record PendingService(ulong Command, ulong Generation);
    internal sealed record Worker(long Progress, long ReleasedWait, long ActiveWait, PendingService? Pending);
    internal sealed record Readings(long Global, long Credit, ulong AcceptedSampleSequence, ulong Epoch, Worker Selected, Worker Peer,
        TimerServiceController.AccountingSnapshot? Timer = null);

    internal sealed class Boundary
    {
        public long BeforeTimestamp { get; }
        public long AfterTimestamp { get; }
        public long TimestampFrequency { get; }
        public Readings Accounting { get; }

        private Boundary(long before, long after, long frequency, Readings accounting)
        {
            BeforeTimestamp = before;
            AfterTimestamp = after;
            TimestampFrequency = frequency;
            Accounting = accounting;
        }

        internal static Boundary Capture(TimeProvider clock, long frequency, Func<Readings> read, Action? release = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frequency);
            long before = clock.GetTimestamp();
            var values = read();
            if (values is null || values.Global < 0 || values.Credit < 0 || !Valid(values.Selected) || !Valid(values.Peer)
                || !ValidTimer(values))
                throw new InvalidDataException("Measurement readings contain invalid counters or pending ownership.");
            release?.Invoke();
            long after = clock.GetTimestamp();
            if (after < before) throw new InvalidDataException("The measurement boundary clock regressed.");
            return new(before, after, frequency, values);
        }

        private static bool Valid(Worker? worker) => worker is not null && worker.Progress >= 0
            && worker.ReleasedWait >= 0 && worker.ActiveWait >= 0
            && (worker.Pending is null ? worker.ActiveWait == 0 : worker.Pending.Command != 0);
    }

    private static bool ValidTimer(Readings readings)
    {
        if (readings.Timer is not { } timer) return true;
        bool Matches(Worker worker, TimerServiceController.WorkerAccounting? state) => state is not null
            && state.ReleasedWait == worker.ReleasedWait
            && worker.ActiveWait == (state.Pending?.ConfirmedWait ?? 0)
            && worker.Pending == (state.Pending is { } pending ? new PendingService(pending.Command, pending.Generation) : null);
        return timer.ReleasedJournalCount >= 0 && Matches(readings.Selected, timer.Selected) && Matches(readings.Peer, timer.Peer);
    }

    private readonly TimeProvider clock;
    private readonly long frequency;
    public MeasurementCaptureStatus Status { get; private set; }
    public Boundary? Start { get; private set; }
    public Boundary? End { get; private set; }

    public MeasurementWindowCapture(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        this.clock = clock;
        frequency = clock.TimestampFrequency;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frequency);
    }

    public void Begin(Func<Readings> read, Action release)
    {
        if (Status != MeasurementCaptureStatus.NotStarted)
            throw new InvalidOperationException("The measurement start was already attempted.");
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(release);
        Status = MeasurementCaptureStatus.StartFailed;
        Start = Boundary.Capture(clock, frequency, read, release);
        Status = MeasurementCaptureStatus.Active;
    }

    public void Finish(Func<Readings> read)
    {
        if (Status is MeasurementCaptureStatus.Complete or MeasurementCaptureStatus.FinishFailed) return;
        if (Status != MeasurementCaptureStatus.Active)
            throw new InvalidOperationException("The measurement has no completed start boundary.");
        ArgumentNullException.ThrowIfNull(read);
        Status = MeasurementCaptureStatus.FinishFailed;
        var end = Boundary.Capture(clock, frequency, read);
        if (end.BeforeTimestamp < Start!.AfterTimestamp)
            throw new InvalidDataException("The measurement end precedes its start boundary.");
        var before = Start.Accounting;
        var after = end.Accounting;
        if (after.Global < before.Global || after.Credit < before.Credit
            || after.AcceptedSampleSequence < before.AcceptedSampleSequence || after.Epoch < before.Epoch
            || after.Selected.Progress < before.Selected.Progress || after.Peer.Progress < before.Peer.Progress
            || after.Selected.ReleasedWait < before.Selected.ReleasedWait || after.Peer.ReleasedWait < before.Peer.ReleasedWait)
            throw new InvalidDataException("Cumulative measurement counters regressed.");
        if ((before.Timer is null) != (after.Timer is null)
            || (before.Timer is { } timer && after.Timer!.ReleasedJournalCount < timer.ReleasedJournalCount))
            throw new InvalidDataException("The frozen timer journal inventory changed capability or regressed.");
        End = end;
        Status = MeasurementCaptureStatus.Complete;
    }
}
