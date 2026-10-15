using static CooperativeManagedChecks;

static class BoundSampleCollectionChecks
{
    private static readonly SampleBatch Empty = new(0, 0, 0, 0, 0, 0, 0, 0, Array.Empty<string>());
    private static CpuSample Sample(uint process = 1, uint thread = 2, ulong time = 10, ulong cpu = 1000000,
        ulong address = 100, string? method = OverheadWorkload.SelectedSymbol) => CpuSample.Create(process, thread, time, 1000000, address, method, cpu);
    private sealed class Source(ICpuSampleSink sink, CpuSample[] samples) : ICpuEventSession, ICpuCounterEvidenceSource
    {
        private int enabled, delivered, disabled;
        private ulong cpu;
        public int Enables, Disposes;
        public bool DirtyInitial, DisableFails, DisposeFails, DeliverAfterDisable;
        public Action? BeforeDelivery, AfterDelivery;
        public Func<SampleBatch, SampleBatch>? ChangeBatch;
        public ICpuCounterEvidenceSource CounterEvidence => this;
        private CpuCounterJournal? activeJournal;
        private CpuCounterJournal Counters => activeJournal ??= new(TimeProvider.System, () => cpu);
        public void Enable()
        {
            Counters.Transition(true, () => { Enables++; Volatile.Write(ref enabled, 1); });
        }
        public ulong Disable()
        {
            Counters.Transition(false, () => { if (DisableFails) throw new IOException("Controlled disable failure."); });
            cpu = 100000000000;
            Volatile.Write(ref disabled, 1);
            return Counters.ReadCounter().CpuNanoseconds!.Value;
        }
        public SampleBatch Drain()
        {
            if (Volatile.Read(ref enabled) == 0) return DirtyInitial ? Empty with { Samples = 1 } : Empty;
            if (DeliverAfterDisable && Volatile.Read(ref disabled) == 0) return Empty;
            if (Interlocked.Exchange(ref delivered, 1) == 0)
            {
                BeforeDelivery?.Invoke();
                foreach (var sample in samples) sink.Publish(sample);
                AfterDelivery?.Invoke();
            }
            var batch = new SampleBatch(samples.Length, samples.Select(item => item.InstructionPointer).Distinct().Count(),
                (ulong)samples.Length * 1000000, 0, 0, 0, samples.Count(item => item.Method == OverheadWorkload.SelectedSymbol),
                samples.Count(item => item.Method is null), Array.AsReadOnly(samples.Where(item => item.Method is not null).Select(item => item.Method!).Distinct().ToArray()));
            return ChangeBatch?.Invoke(batch) ?? batch;
        }
        public CpuCounterReading ReadCounter() => Counters.ReadCounter();
        public CpuCounterSourceSnapshot DescribeCounters() => Counters.Snapshot();
        public void Dispose()
        {
            Disposes++;
            if (DisposeFails) throw new IOException("Controlled dispose failure.");
            if (Counters.Enabled) Counters.MarkUnrecordedCleanup();
        }
    }
    private static BoundSampleCollection Create(Func<ClrProbeBinding, bool>? current = null) => new(
        new ClrProbeLifetime(ClrProbeBinding.Parse(1, 10, 20, 0x06000001, 2), current ?? (_ => true)),
        CpuSampleOwner.Parse(1, 2), _ => ClrNativeRange.Parse(100, 10));
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new InvalidOperationException("The invalid bound sample epoch was accepted.");
    }
    public static void Run()
    {
        FreshEpochAndFinalDrain();
        ProvisionalReadsPreservePrefixes();
        ProvisionalReadsRejectInvalidOffsetsAndLifetime();
        RejectsForeignAndInvalidSamples();
        RejectsLossAndChangedAccounting();
        InvalidationPreservesCleanup();
        CleanupFailuresRejectCompletion();
        CounterEpochRejectsReuse();
    }
    private static void FreshEpochAndFinalDrain()
    {
        Reject(() => CpuSampleOwner.Parse(0, 2));
        Reject(() => CpuSampleOwner.Parse(1, 0));
        var collection = Create();
        Source? source = null;
        var symbols = new List<string> { OverheadWorkload.SelectedSymbol };
        collection.Start((map, sink) =>
        {
            Require(map.Resolve(100) == OverheadWorkload.SelectedSymbol && map.Resolve(200) is null, "The source did not receive the admitted map.");
            return source = new(sink, [Sample(), Sample(time: 20, cpu: 2000000, address: 200, method: null)])
            { DeliverAfterDisable = true, ChangeBatch = batch => batch with { SelectedSymbols = symbols } };
        });
        var result = collection.Complete();
        symbols.Clear();
        Require(result.Samples.Count == 2 && result.Batch.SelectedMethodSamples == 1 && result.Batch.UnmappedSamples == 1
            && result.Batch.SelectedSymbols.SequenceEqual(new[] { OverheadWorkload.SelectedSymbol })
            && result.FinalCounters.Epoch == 1 && !result.FinalCounters.Enabled && source!.Enables == 1 && source.Disposes == 1,
            "Completion lost a final-drain sample, owner epoch, or source release.");
        Require(ReferenceEquals(result.Cleanup, collection.CloseForCleanup()) && source!.Disposes == 1, "Repeated cleanup released the source twice.");
        bool restartRejected = false;
        try { collection.Start((_, sink) => new Source(sink, [])); } catch (InvalidOperationException) { restartRejected = true; }
        Require(restartRejected, "The collection reused its source epoch.");
        var dirty = Create();
        Source? prior = null;
        Reject(() => dirty.Start((_, sink) => prior = new(sink, []) { DirtyInitial = true }));
        dirty.CloseForCleanup();
        Require(prior!.Enables == 0 && prior.Disposes == 1, "The collection enabled a nonempty source or lost its cleanup ownership.");
    }
    private static void ProvisionalReadsPreservePrefixes()
    {
        using var release = new ManualResetEventSlim();
        using var delivered = new ManualResetEventSlim();
        var collection = Create();
        bool beforeRejected = false;
        try { collection.ReadProvisional(0); } catch (InvalidOperationException) { beforeRejected = true; }
        Require(beforeRejected, "An unstarted collection exposed provisional records.");
        collection.Start((_, sink) => new Source(sink, [Sample(), Sample(time: 20, cpu: 2000000)])
        {
            BeforeDelivery = () => Require(release.Wait(TimeSpan.FromSeconds(5)), "Sample delivery was not released."),
            AfterDelivery = delivered.Set
        });
        try
        {
            var empty = collection.ReadProvisional(0);
            Require(empty.Count == 0, "A paused fixture already exposed records.");
            release.Set();
            Require(delivered.Wait(TimeSpan.FromSeconds(5)), "The source did not deliver provisional samples.");
            var prefix = collection.ReadProvisional(0);
            var suffix = collection.ReadProvisional(1);
            Require(empty.Count == 0 && prefix.Count == 2 && suffix.Count == 1 && suffix[0] == prefix[1]
                && collection.ReadProvisional(2).Count == 0 && collection.ReadProvisional(0).SequenceEqual(prefix),
                "Provisional offsets skipped, consumed, or changed retained samples.");
            var completed = collection.Complete();
            Require(completed.Samples.SequenceEqual(prefix), "Completion changed the provisional sample prefix.");
            bool closedRejected = false;
            try { collection.ReadProvisional(0); } catch (InvalidOperationException) { closedRejected = true; }
            Require(closedRejected, "A closed collection exposed a live provisional read.");
        }
        finally { release.Set(); collection.CloseForCleanup(); }
    }

    private static void ProvisionalReadsRejectInvalidOffsetsAndLifetime()
    {
        foreach (int offset in new[] { -1, 1 })
        {
            var collection = Create();
            collection.Start((_, sink) => new Source(sink, []));
            try
            {
                bool rejected = false;
                try { collection.ReadProvisional(offset); } catch (ArgumentOutOfRangeException) { rejected = true; }
                Require(rejected, "A provisional read accepted an invalid offset.");
                Reject(() => collection.Complete());
            }
            finally { collection.CloseForCleanup(); }
        }
        bool armed = false;
        int checks = 0;
        using var delivered = new ManualResetEventSlim();
        var invalidated = Create(_ => !armed || Interlocked.Increment(ref checks) < 2);
        invalidated.Start((_, sink) => new Source(sink, [Sample()]) { AfterDelivery = delivered.Set });
        Require(delivered.Wait(TimeSpan.FromSeconds(5)), "The source did not deliver a sample before invalidation.");
        armed = true;
        Reject(() => invalidated.ReadProvisional(0));
        armed = false;
        Reject(() => invalidated.ReadProvisional(0));
        invalidated.CloseForCleanup();
        Require(invalidated.Diagnostics.Count == 1 && invalidated.Cleanup!.SourceDisposed,
            "A failed provisional post-check lost diagnostic or cleanup access.");
    }

    private static void RejectsForeignAndInvalidSamples()
    {
        foreach (var samples in new[] {
            new[] { Sample(process: 9) }, new[] { Sample(thread: 9) }, new[] { Sample(time: 0) },
            new[] { Sample(method: "foreign") }, new[] { Sample(address: 200) },
            new[] { CpuSample.Create(1, 2, 10, 1000000, 100, OverheadWorkload.SelectedSymbol) },
            new[] { Sample(), Sample(time: 9, cpu: 2000000) }, new[] { Sample(), Sample(time: 20, cpu: 1) } })
        {
            var collection = Create();
            Source? source = null;
            collection.Start((_, sink) => source = new(sink, samples));
            Reject(() => collection.Complete());
            Require(collection.Cleanup is { CollectorJoined: true, SourceDisposed: true } && source!.Disposes == 1,
                "Sample rejection lost the collector or source cleanup.");
        }
    }
    private static void RejectsLossAndChangedAccounting()
    {
        foreach (Func<SampleBatch, SampleBatch> change in new Func<SampleBatch, SampleBatch>[] {
            batch => batch with { LostRecords = 1 }, batch => batch with { LostSamples = 1 }, batch => batch with { Throttles = 1 },
            batch => batch with { Samples = 2 }, batch => batch with { SelectedMethodSamples = 0 },
            batch => batch with { UnmappedSamples = 1 }, batch => batch with { RepresentedCpuNanoseconds = 1 },
            batch => batch with { DistinctInstructionPointers = 0 }, batch => batch with { SelectedSymbols = Array.Empty<string>() } })
        {
            var collection = Create();
            collection.Start((_, sink) => new Source(sink, [Sample()]) { ChangeBatch = change });
            Reject(() => collection.Complete());
            Require(collection.Diagnostics.Count == 1 && collection.Cleanup!.Errors.Length == 0, "Accounting rejection erased diagnostic samples or failed cleanup.");
        }
    }
    private static void InvalidationPreservesCleanup()
    {
        bool current = true;
        var before = Create(_ => current);
        Source? source = null;
        Reject(() => before.Start((_, sink) => { source = new(sink, []); current = false; return source; }));
        before.CloseForCleanup();
        Require(source!.Enables == 0 && source.Disposes == 1, "Invalidation during source creation escaped admission or cleanup.");
        int armedChecks = 0;
        bool armed = false;
        using var delivery = new ManualResetEventSlim();
        var during = Create(_ => !armed || ++armedChecks < 2);
        during.Start((_, sink) => new Source(sink, [Sample()]) { BeforeDelivery = () =>
        {
            if (!delivery.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Controlled sample delivery was not released.");
            armed = true;
        } });
        delivery.Set();
        Reject(() => during.Complete());
        Require(during.Diagnostics.Count == 0 && during.Cleanup!.SourceDisposed, "A sample escaped its failed post-acceptance lifetime check.");
        current = true;
        var after = Create(_ => current);
        after.Start((_, sink) => new Source(sink, [Sample()]));
        after.CloseForCleanup();
        current = false;
        Reject(() => after.Complete());
        Require(after.Diagnostics.Count == 1, "Final lifetime rejection erased the provisional sample evidence.");
    }
    private static void CleanupFailuresRejectCompletion()
    {
        foreach (bool disable in new[] { true, false })
        {
            var collection = Create();
            Source? source = null;
            collection.Start((_, sink) => source = new(sink, [Sample()]) { DisableFails = disable, DisposeFails = !disable });
            Reject(() => collection.Complete());
            Require(collection.Cleanup!.CollectorJoined && source!.Disposes == 1
                && collection.Cleanup.Errors.Any(error => error.Stage == (disable ? "disable" : "dispose")),
                "A failed cleanup stage prevented the other stages or escaped the result.");
        }
    }
    private static void CounterEpochRejectsReuse()
    {
        ulong cpu = 0;
        var journal = new CpuCounterJournal(TimeProvider.System, () => cpu);
        journal.ReadCounter();
        var fresh = journal.Snapshot();
        var initialTransitions = new List<CpuCounterTransition>();
        var epoch = BoundCpuEpoch.ParseFresh(fresh with { Transitions = initialTransitions });
        initialTransitions.Add(new(1, true, true, 0, 0, 1, null));
        Require(epoch.Initial.Transitions.Count == 0, "Caller mutation changed the admitted source epoch.");
        foreach (var changed in new[] { fresh with { Epoch = 1 }, fresh with { Enabled = true }, fresh with { Units = "other" },
            fresh with { FailedReads = 1 }, fresh with { Overflowed = true }, fresh with { UnrecordedCleanup = true },
            fresh with { LastReading = fresh.LastReading!.Value with { CpuNanoseconds = 1 } } })
            Reject(() => BoundCpuEpoch.ParseFresh(changed));
        journal.Transition(true, () => { });
        journal.Transition(false, () => { });
        cpu = 100;
        journal.ReadCounter();
        var final = journal.Snapshot();
        var finalTransitions = final.Transitions.ToList();
        var accepted = epoch.Close(final with { Transitions = finalTransitions }, 100);
        finalTransitions.Clear();
        Require(accepted.SourceIdentity == fresh.SourceIdentity && accepted.Transitions.Count == 2
            && accepted.LastReading!.Value.CpuNanoseconds == 100, "Caller mutation changed the closed source epoch.");
        Reject(() => BoundCpuEpoch.ParseFresh(final));
        Reject(() => epoch.Close(final with { SourceIdentity = "foreign" }, 100));
        Reject(() => epoch.Close(final, 99));
        Reject(() => epoch.Close(final with { Transitions = final.Transitions.Take(1).ToArray() }, 100));
        var invalid = final.Transitions.ToArray();
        invalid[1] = invalid[1] with { Completed = false };
        Reject(() => epoch.Close(final with { Transitions = invalid }, 100));
    }
}
