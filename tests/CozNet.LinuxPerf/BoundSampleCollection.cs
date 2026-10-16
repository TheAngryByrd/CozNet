sealed class CpuSampleOwner
{
    public uint Process { get; }
    public uint Thread { get; }
    private CpuSampleOwner(uint process, uint thread) => (Process, Thread) = (process, thread);
    public static CpuSampleOwner Parse(uint process, uint thread)
    {
        if (process == 0 || thread == 0) throw new InvalidDataException("A sample owner requires nonzero process and thread identifiers.");
        return new(process, thread);
    }
}

sealed record BoundSampleCleanupError(string Stage, string Type, string Error);
sealed record BoundEpochSample(ulong EligibilityEpoch, CpuSample Sample);

interface IOwnerStoppedCpuEventSession : ICpuEventSession
{
    void DisableAndStopProducer();
}
sealed record BoundSampleCleanup(bool SourceCreated, bool DisableSucceeded, bool CollectorJoined, bool SourceDisposed, BoundSampleCleanupError[] Errors);
sealed record BoundSampleResult(ClrProbeBinding Binding, ClrNativeRange Range, SampleBatch Batch,
    IReadOnlyList<CpuSample> Samples, ulong CpuNanoseconds, CpuCounterSourceSnapshot InitialCounters,
    CpuCounterSourceSnapshot FinalCounters, BoundSampleCleanup Cleanup)
{
    public IReadOnlyList<BoundCpuEpochPrefix>? EpochPrefixes { get; init; }
}

sealed class BoundSampleCollection
{
    private sealed class Capture(ClrProbeLifetime lifetime, CpuSampleOwner owner, ClrNativeRange range) : ICpuSampleSink
    {
        private readonly object gate = new();
        private readonly List<CpuSample> samples = [];
        private readonly List<BoundEpochSample> epochs = [];
        private ulong eligibilityEpoch;
        public bool TrackEpochs { get; set; }
        private bool accepting;
        private ulong lastTimestamp, lastCounter;
        public int Count { get { lock (gate) return samples.Count; } }
        public void Open() { lock (gate) { eligibilityEpoch = checked(eligibilityEpoch + 1); accepting = true; } }
        public void Close() { lock (gate) accepting = false; }
        public IReadOnlyList<BoundEpochSample> EpochSnapshot(int offset)
        {
            lock (gate)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(offset);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, epochs.Count);
                return Array.AsReadOnly(epochs.GetRange(offset, epochs.Count - offset).ToArray());
            }
        }
        public IReadOnlyList<CpuSample> Snapshot(int offset = 0)
        {
            lock (gate)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(offset);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, samples.Count);
                return Array.AsReadOnly(samples.GetRange(offset, samples.Count - offset).ToArray());
            }
        }
        public void Publish(CpuSample sample)
        {
            lock (gate)
            {
                var accepted = lifetime.Execute((Self: this, Sample: sample, Owner: owner, Range: range), static value =>
                {
                    var item = value.Sample;
                    var self = value.Self;
                    if (!self.accepting) throw new InvalidDataException("The bound sample epoch is not accepting records.");
                    if (item.ProcessId != value.Owner.Process || item.ThreadId != value.Owner.Thread)
                        throw new InvalidDataException("A CPU sample belongs to a foreign owner.");
                    if (item.Timestamp == 0 || item.Timestamp < self.lastTimestamp || item.CumulativeCpuNanoseconds is not ulong counter
                        || counter < self.lastCounter)
                        throw new InvalidDataException("The bound sample timestamp or cumulative CPU counter is missing or regressed.");
                    string? expected = value.Range.Contains(item.InstructionPointer) ? OverheadWorkload.SelectedSymbol : null;
                    if (item.Method != expected) throw new InvalidDataException("A CPU sample does not match the bound native range.");
                    if (self.samples.Count == 10000) throw new InvalidDataException("The bound sample epoch exceeded its record limit.");
                    return item;
                });
                samples.Add(accepted);
                if (TrackEpochs) epochs.Add(new(eligibilityEpoch, accepted));
                lastTimestamp = accepted.Timestamp;
                lastCounter = accepted.CumulativeCpuNanoseconds!.Value;
            }
        }
    }

    private readonly ClrProbeLifetime lifetime;
    private readonly Capture capture;
    private ICpuEventSession? source;
    private LiveSampleCollector? collector;
    private IPausableSampleCollector? pausableCollector;
    private BoundCpuSession? session;
    private readonly List<BoundCpuEpochStart> epochStarts = [];
    private readonly int owner = Environment.CurrentManagedThreadId;
    private bool pausable, excluding, drainingOnOwner, sourceDisabled = true;
    private bool started, enableAttempted, completed;
    private SampleBatch? finalBatch;
    private readonly SampleBatchSnapshotCache batchSnapshots = new();
    private ulong cpuNanoseconds;
    private BoundCpuEpoch? epoch;
    private CpuCounterSourceSnapshot? finalCounters;
    public ClrBoundInstructionMap Map { get; }
    public BoundSampleCleanup? Cleanup { get; private set; }
    public CpuCounterSourceSnapshot? InitialCounters => epoch?.Initial;
    public CpuCounterSourceSnapshot? FinalCounters => finalCounters;
    public int LiveSamples => capture.Count;
    public SampleBatch? Latest => pausable ? Volatile.Read(ref finalBatch) : collector?.Latest;
    public IReadOnlyList<CpuSample> Diagnostics => Cleanup is null
        ? throw new InvalidOperationException("Close collection before reading sample diagnostics.") : capture.Snapshot();
    public IReadOnlyList<BoundEpochSample> EpochDiagnostics => Cleanup is null
        ? throw new InvalidOperationException("Close collection before reading epoch diagnostics.") : capture.EpochSnapshot(0);

    public IReadOnlyList<CpuSample> ReadProvisional(int offset)
    {
        if ((collector is null && pausableCollector is null) || Cleanup is not null)
            throw new InvalidOperationException("Provisional reads require an active collection.");
        return lifetime.Execute((Capture: capture, Offset: offset), static value => value.Capture.Snapshot(value.Offset));
    }

    public BoundSampleCollection(ClrProbeLifetime lifetime, CpuSampleOwner owner)
        : this(lifetime, owner, null) { }
    internal BoundSampleCollection(ClrProbeLifetime lifetime, CpuSampleOwner owner, Func<ClrProbeBinding, ClrNativeRange>? readRange)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(owner);
        this.lifetime = lifetime;
        Map = readRange is null ? ClrBoundInstructionMap.Capture(lifetime, OverheadWorkload.SelectedSymbol)
            : ClrBoundInstructionMap.Capture(lifetime, OverheadWorkload.SelectedSymbol, readRange);
        capture = new(lifetime, owner, Map.Range);
    }

    public void Start(Func<IInstructionMap, ICpuSampleSink, ICpuEventSession> createSource)
        => StartCore(createSource, false);

    public void StartPausable(Func<IInstructionMap, ICpuSampleSink, IOwnerStoppedCpuEventSession> createSource)
    {
        ArgumentNullException.ThrowIfNull(createSource);
        RequireOwner();
        StartCore((map, sink) => createSource(map, sink), true);
    }

    public void StartOwnerPausable(Func<IInstructionMap, ICpuSampleSink, IOwnerStoppedCpuEventSession> createSource)
    {
        ArgumentNullException.ThrowIfNull(createSource);
        RequireOwner();
        StartCore((map, sink) => createSource(map, sink), true, true);
    }

    public void DrainOnOwner()
    {
        RequireOwner();
        if (pausableCollector is not OwnerSampleCollector ownerCollector || Cleanup is not null || excluding || drainingOnOwner)
            throw new InvalidOperationException("Owner draining requires an active owner collection outside excluded execution.");
        ownerCollector.Drain();
    }

    private void StartCore(Func<IInstructionMap, ICpuSampleSink, ICpuEventSession> createSource, bool allowPause, bool ownerDriven = false)
    {
        ArgumentNullException.ThrowIfNull(createSource);
        if (started || Cleanup is not null) throw new InvalidOperationException("A bound collection can start only once.");
        started = true;
        pausable = allowPause;
        capture.TrackEpochs = allowPause;
        lifetime.Execute(createSource, factory =>
        {
            PrepareValidation();
            source = factory(Map, capture) ?? throw new InvalidDataException("The sample factory returned no source.");
            var counters = source.CounterEvidence ?? throw new InvalidDataException("The bound source has no counter epoch evidence.");
            _ = counters.ReadCounter();
            epoch = BoundCpuEpoch.ParseFresh(counters.DescribeCounters());
            if (pausable) session = BoundCpuSession.ParseFresh(epoch.Initial);
            var initial = source.Drain();
            if (initial.Samples != 0 || initial.DistinctInstructionPointers != 0 || initial.RepresentedCpuNanoseconds != 0
                || initial.LostRecords != 0 || initial.LostSamples != 0 || initial.Throttles != 0
                || initial.SelectedMethodSamples != 0 || initial.UnmappedSamples != 0 || initial.SelectedSymbols.Count != 0)
                throw new InvalidDataException("A bound sample epoch requires a fresh empty source.");
            lifetime.RequireCurrent();
            capture.Open();
            enableAttempted = true;
            sourceDisabled = false;
            source.Enable();
            if (pausable)
            {
                _ = counters.ReadCounter();
                var enabled = BoundCpuEpochStart.Parse(session!, counters.DescribeCounters());
                session = enabled.Session;
                epochStarts.Add(enabled.Start);
                Action drain = () =>
                {
                    if (ownerDriven) drainingOnOwner = true;
                    try
                    {
                        lifetime.Execute((Self: this, Source: source), static value =>
                        {
                            var batch = value.Source.Drain();
                            Volatile.Write(ref value.Self.finalBatch, value.Self.batchSnapshots.Capture(batch));
                            if (batch.LostRecords != 0 || batch.LostSamples != 0 || batch.Throttles != 0)
                                throw new InvalidDataException("The pausable source reported cumulative loss or throttling.");
                            return true;
                        });
                    }
                    finally { if (ownerDriven) drainingOnOwner = false; }
                };
                pausableCollector = ownerDriven ? new OwnerSampleCollector(drain) : new PausableSampleCollector(drain);
            }
            else collector = new LiveSampleCollector(source.Drain);
            return true;
        });
    }

    public IReadOnlyList<BoundEpochSample> ReadEpochProvisional(int offset)
    {
        if (pausableCollector is null || Cleanup is not null)
            throw new InvalidOperationException("Epoch reads require an active pausable collection.");
        return lifetime.Execute((Capture: capture, Offset: offset), static value => value.Capture.EpochSnapshot(value.Offset));
    }

    private void RequireOwner()
    {
        if (Environment.CurrentManagedThreadId != owner)
            throw new InvalidOperationException("Pausable collection control requires its owning thread.");
    }

    private void DisablePausable(BoundPauseTiming? timing = null)
    {
        ((IOwnerStoppedCpuEventSession)source!).DisableAndStopProducer();
        sourceDisabled = true;
        if (timing is not null) timing.ProducerStopped = System.Diagnostics.Stopwatch.GetTimestamp();
        ReadDisabledCounters();
    }

    private void ReadDisabledCounters()
    {
        var counters = source!.CounterEvidence ?? throw new InvalidDataException("The pausable source lost counter evidence.");
        var reading = counters.ReadCounter();
        if (reading.Error is not null) throw new InvalidDataException("The disabled source CPU counter read failed.");
        cpuNanoseconds = reading.CpuNanoseconds ?? throw new InvalidDataException("The disabled source has no CPU counter.");
        if (session!.Current.Enabled) session = session.Disable(counters.DescribeCounters(), cpuNanoseconds);
    }

    public void ExecuteExcluded(Action<IReadOnlyList<BoundEpochSample>> action, BoundPauseTiming? timing = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        RequireOwner();
        if (pausableCollector is null || Cleanup is not null || excluding || drainingOnOwner)
            throw new InvalidOperationException("Excluded execution requires an active pausable collection without an excluded callback.");
        if (timing is not null) timing.Started = System.Diagnostics.Stopwatch.GetTimestamp();
        lifetime.Execute(action, callback =>
        {
            excluding = true;
            try
            {
                DisablePausable(timing);
                if (timing is not null) timing.DisabledCountersAccepted = System.Diagnostics.Stopwatch.GetTimestamp();
                var pause = pausableCollector.RequestPause().GetAwaiter().GetResult();
                if (timing is not null) timing.DrainAcknowledged = System.Diagnostics.Stopwatch.GetTimestamp();
                capture.Close();
                lifetime.RequireCurrent();
                ValidateBatch(finalBatch ?? throw new InvalidDataException("The paused source has no final drain evidence."), capture.Snapshot(), cpuNanoseconds);
                if (timing is not null) timing.BatchValidated = System.Diagnostics.Stopwatch.GetTimestamp();
                var snapshot = capture.EpochSnapshot(0);
                if (timing is not null) timing.SnapshotReady = System.Diagnostics.Stopwatch.GetTimestamp();
                callback(snapshot);
                if (timing is not null) timing.CallbackReturned = System.Diagnostics.Stopwatch.GetTimestamp();
                lifetime.RequireCurrent();
                capture.Open();
                sourceDisabled = false;
                source!.Enable();
                if (timing is not null) timing.SourceEnabled = System.Diagnostics.Stopwatch.GetTimestamp();
                var counters = source.CounterEvidence ?? throw new InvalidDataException("The pausable source lost counter evidence.");
                _ = counters.ReadCounter();
                var enabled = BoundCpuEpochStart.Parse(session!, counters.DescribeCounters());
                session = enabled.Session;
                epochStarts.Add(enabled.Start);
                if (timing is not null) timing.EnabledCountersAccepted = System.Diagnostics.Stopwatch.GetTimestamp();
                pausableCollector.Resume(pause);
                if (timing is not null) timing.CollectorResumed = System.Diagnostics.Stopwatch.GetTimestamp();
                return true;
            }
            finally { excluding = false; }
        });
        if (timing is not null) timing.Completed = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    public BoundSampleCleanup CloseForCleanup()
    {
        if (pausable)
        {
            RequireOwner();
            if (excluding || drainingOnOwner) throw new InvalidOperationException("Collection cannot close inside an excluded callback or owner drain.");
        }
        if (Cleanup is not null) return Cleanup;
        var errors = new List<BoundSampleCleanupError>();
        void Retain(string stage, Exception error) => errors.Add(new(stage, error.GetType().FullName!, error.ToString()));
        bool disabled = !enableAttempted || sourceDisabled, joined = collector is null && pausableCollector is null, disposed = source is null;
        if (enableAttempted && !sourceDisabled)
        {
            try
            {
                if (pausable)
                {
                    ((IOwnerStoppedCpuEventSession)source!).DisableAndStopProducer();
                    sourceDisabled = true;
                }
                else { cpuNanoseconds = source!.Disable(); sourceDisabled = true; }
                disabled = true;
            }
            catch (Exception error) { Retain("disable", error); }
            if (pausable && disabled)
            {
                try { ReadDisabledCounters(); }
                catch (Exception error) { Retain("counters", error); }
            }
        }
        if (pausableCollector is not null)
        {
            try { pausableCollector.Complete(); }
            catch (Exception error) { Retain("collector", error); }
            finally { joined = true; }
        }
        if (collector is not null)
        {
            try { finalBatch = collector.Complete(); }
            catch (Exception error) { Retain("collector", error); }
            finally { joined = true; }
        }
        capture.Close();
        if (source is not null)
        {
            ICpuCounterEvidenceSource? counters = null;
            try { counters = source.CounterEvidence; }
            catch (Exception error) { Retain("counters", error); }
            try { source.Dispose(); disposed = true; }
            catch (Exception error) { Retain("dispose", error); }
            try { finalCounters = counters?.DescribeCounters(); }
            catch (Exception error) { Retain("counters", error); }
        }
        Cleanup = new(source is not null, disabled, joined, disposed, errors.ToArray());
        return Cleanup;
    }

    public BoundSampleResult Complete()
    {
        if (!started || completed) throw new InvalidOperationException("A bound collection requires one start and one completion.");
        var cleanup = CloseForCleanup();
        var result = lifetime.Execute(cleanup, value =>
        {
            if (value.Errors.Length != 0 || !value.DisableSucceeded || !value.CollectorJoined || !value.SourceDisposed || finalBatch is null)
                throw new InvalidDataException("The bound sample source did not close cleanly.");
            var samples = capture.Snapshot();
            var batch = finalBatch with { SelectedSymbols = Array.AsReadOnly(finalBatch.SelectedSymbols.ToArray()) };
            if (epoch is null || finalCounters is null) throw new InvalidDataException("The bound source lost its counter epoch evidence.");
            var counters = pausable ? session!.Close(finalCounters, cpuNanoseconds) : epoch.Close(finalCounters, cpuNanoseconds);
            ValidateBatch(batch, samples, cpuNanoseconds);
            return new BoundSampleResult(lifetime.Binding, Map.Range, batch, samples, cpuNanoseconds, epoch.Initial, counters, value)
            {
                EpochPrefixes = pausable ? BoundCpuEpochPrefixes.Parse(epochStarts, capture.EpochSnapshot(0), session!, counters) : null
            };
        });
        completed = true;
        return result;
    }

    private static void PrepareValidation()
    {
        var samples = Array.AsReadOnly(new[]
        {
            CpuSample.Create(1, 1, 1, 1, 1, OverheadWorkload.SelectedSymbol, 1),
            CpuSample.Create(1, 1, 2, 1, 1, OverheadWorkload.SelectedSymbol, 2),
            CpuSample.Create(1, 1, 3, 1, 2, null, 3)
        });
        var batch = new SampleBatch(3, 2, 3, 0, 0, 0, 2, 1, new[] { OverheadWorkload.SelectedSymbol });
        ValidateBatch(batch, samples, 3);
    }

    private static void ValidateBatch(SampleBatch batch, IReadOnlyList<CpuSample> samples, ulong cpuNanoseconds)
    {
        if (batch.LostRecords != 0 || batch.LostSamples != 0 || batch.Throttles != 0
            || batch.Samples != samples.Count
            || batch.SelectedMethodSamples != samples.Count(item => item.Method == OverheadWorkload.SelectedSymbol)
            || batch.UnmappedSamples != samples.Count(item => item.Method is null)
            || batch.DistinctInstructionPointers != samples.Select(item => item.InstructionPointer).Distinct().Count()
            || !batch.SelectedSymbols.SequenceEqual(samples.Where(item => item.Method is not null).Select(item => item.Method!).Distinct().Order())
            || samples.Any(item => item.CumulativeCpuNanoseconds > cpuNanoseconds)
            || batch.RepresentedCpuNanoseconds != samples.Aggregate(0UL, (sum, item) => checked(sum + item.PeriodNanoseconds)))
            throw new InvalidDataException("The bound sample epoch lost records or changed sample accounting.");
    }
}
