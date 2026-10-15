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
sealed record BoundSampleCleanup(bool SourceCreated, bool DisableSucceeded, bool CollectorJoined, bool SourceDisposed, BoundSampleCleanupError[] Errors);
sealed record BoundSampleResult(ClrProbeBinding Binding, ClrNativeRange Range, SampleBatch Batch,
    IReadOnlyList<CpuSample> Samples, ulong CpuNanoseconds, CpuCounterSourceSnapshot InitialCounters,
    CpuCounterSourceSnapshot FinalCounters, BoundSampleCleanup Cleanup);

sealed class BoundSampleCollection
{
    private sealed class Capture(ClrProbeLifetime lifetime, CpuSampleOwner owner, ClrNativeRange range) : ICpuSampleSink
    {
        private readonly object gate = new();
        private readonly List<CpuSample> samples = [];
        private bool accepting;
        private ulong lastTimestamp, lastCounter;
        public int Count { get { lock (gate) return samples.Count; } }
        public void Open() { lock (gate) accepting = true; }
        public void Close() { lock (gate) accepting = false; }
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
                lastTimestamp = accepted.Timestamp;
                lastCounter = accepted.CumulativeCpuNanoseconds!.Value;
            }
        }
    }

    private readonly ClrProbeLifetime lifetime;
    private readonly Capture capture;
    private ICpuEventSession? source;
    private LiveSampleCollector? collector;
    private bool started, enableAttempted, completed;
    private SampleBatch? finalBatch;
    private ulong cpuNanoseconds;
    private BoundCpuEpoch? epoch;
    private CpuCounterSourceSnapshot? finalCounters;
    public ClrBoundInstructionMap Map { get; }
    public BoundSampleCleanup? Cleanup { get; private set; }
    public CpuCounterSourceSnapshot? InitialCounters => epoch?.Initial;
    public CpuCounterSourceSnapshot? FinalCounters => finalCounters;
    public int LiveSamples => capture.Count;
    public SampleBatch? Latest => collector?.Latest;
    public IReadOnlyList<CpuSample> Diagnostics => Cleanup is null
        ? throw new InvalidOperationException("Close collection before reading sample diagnostics.") : capture.Snapshot();

    public IReadOnlyList<CpuSample> ReadProvisional(int offset)
    {
        if (collector is null || Cleanup is not null)
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
    {
        ArgumentNullException.ThrowIfNull(createSource);
        if (started || Cleanup is not null) throw new InvalidOperationException("A bound collection can start only once.");
        started = true;
        lifetime.Execute(createSource, factory =>
        {
            source = factory(Map, capture) ?? throw new InvalidDataException("The sample factory returned no source.");
            var counters = source.CounterEvidence ?? throw new InvalidDataException("The bound source has no counter epoch evidence.");
            _ = counters.ReadCounter();
            epoch = BoundCpuEpoch.ParseFresh(counters.DescribeCounters());
            var initial = source.Drain();
            if (initial.Samples != 0 || initial.DistinctInstructionPointers != 0 || initial.RepresentedCpuNanoseconds != 0
                || initial.LostRecords != 0 || initial.LostSamples != 0 || initial.Throttles != 0
                || initial.SelectedMethodSamples != 0 || initial.UnmappedSamples != 0 || initial.SelectedSymbols.Count != 0)
                throw new InvalidDataException("A bound sample epoch requires a fresh empty source.");
            lifetime.RequireCurrent();
            capture.Open();
            enableAttempted = true;
            source.Enable();
            collector = new LiveSampleCollector(source.Drain);
            return true;
        });
    }

    public BoundSampleCleanup CloseForCleanup()
    {
        if (Cleanup is not null) return Cleanup;
        var errors = new List<BoundSampleCleanupError>();
        void Retain(string stage, Exception error) => errors.Add(new(stage, error.GetType().FullName!, error.ToString()));
        bool disabled = !enableAttempted, joined = collector is null, disposed = source is null;
        if (enableAttempted)
        {
            try { cpuNanoseconds = source!.Disable(); disabled = true; }
            catch (Exception error) { Retain("disable", error); }
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
            var counters = epoch.Close(finalCounters, cpuNanoseconds);
            if (batch.LostRecords != 0 || batch.LostSamples != 0 || batch.Throttles != 0
                || batch.Samples != samples.Count
                || batch.SelectedMethodSamples != samples.Count(item => item.Method == OverheadWorkload.SelectedSymbol)
                || batch.UnmappedSamples != samples.Count(item => item.Method is null)
                || batch.DistinctInstructionPointers != samples.Select(item => item.InstructionPointer).Distinct().Count()
                || !batch.SelectedSymbols.SequenceEqual(samples.Where(item => item.Method is not null).Select(item => item.Method!).Distinct().Order())
                || samples.Any(item => item.CumulativeCpuNanoseconds > cpuNanoseconds)
                || batch.RepresentedCpuNanoseconds != samples.Aggregate(0UL, (sum, item) => checked(sum + item.PeriodNanoseconds)))
                throw new InvalidDataException("The bound sample epoch lost records or changed sample accounting.");
            return new BoundSampleResult(lifetime.Binding, Map.Range, batch, samples, cpuNanoseconds, epoch.Initial, counters, value);
        });
        completed = true;
        return result;
    }
}
