using System.Collections.Concurrent;
using static CooperativeManagedChecks;

static class PausableBoundCollectionChecks
{
    private sealed class Source(ICpuSampleSink sink) : IOwnerStoppedCpuEventSession, ICpuCounterEvidenceSource
    {
        private readonly int owner = Environment.CurrentManagedThreadId;
        private readonly ConcurrentQueue<CpuSample> queued = new();
        private readonly List<CpuSample> delivered = [];
        private CpuCounterJournal? journal;
        private CpuCounterJournal Journal => journal ??= new(TimeProvider.System, () =>
            (FailReadAfterResume && Enables > 1) || (FailReadAfterDisable && cpu > 0)
                ? throw new IOException("Controlled counter failure.") : RegressCounterAfterResume && Enables > 1 ? 0UL : cpu);
        private ulong cpu;
        private int enables, drains;
        private int enabledDrains;
        public int Enables => Volatile.Read(ref enables);
        public int Drains => Volatile.Read(ref drains);
        public int Disposes;
        public int Stops;
        public int FailEnableAt;
        public bool FailDisable, LateLoss, WrongBatch, FailReadAfterResume, FailReadAfterDisable;
        public bool SkipFirstEnabledDrain;
        public bool RegressCounterAfterResume;
        public Action? BeforeDrain, AfterDisable, BeforeEnable;
        public ICpuCounterEvidenceSource CounterEvidence => this;
        private void Owner() => Require(Environment.CurrentManagedThreadId == owner, "A source control operation left its owner thread.");
        public void Enable()
        {
            Owner();
            Journal.Transition(true, () =>
            {
                BeforeEnable?.Invoke();
                if (Enables + 1 == FailEnableAt) throw new IOException("Controlled resume failure.");
                Interlocked.Increment(ref enables);
            });
        }
        public ulong Disable() { DisableAndStopProducer(); return Journal.ReadCounter().CpuNanoseconds!.Value; }
        public void DisableAndStopProducer()
        {
            Owner();
            Stops++;
            if (!Journal.Enabled)
            {
                Journal.MarkUnrecordedCleanup();
                if (FailDisable) throw new IOException("Controlled disable failure.");
                return;
            }
            Journal.Transition(false, () =>
            {
                if (FailDisable) throw new IOException("Controlled disable failure.");
            });
            cpu += 1000;
            queued.Enqueue(CpuSample.Create(1, 2, cpu, 1000, 100, OverheadWorkload.SelectedSymbol, cpu));
            AfterDisable?.Invoke();
        }
        public SampleBatch Drain()
        {
            Interlocked.Increment(ref drains);
            bool skip = Enables != 0 && Interlocked.Increment(ref enabledDrains) == 1 && SkipFirstEnabledDrain;
            if (Enables != 0) BeforeDrain?.Invoke();
            while (!skip && queued.TryDequeue(out var sample))
            {
                sink.Publish(sample);
                delivered.Add(sample);
            }
            return new(delivered.Count + (WrongBatch && Enables > 0 ? 1 : 0), delivered.Count == 0 ? 0 : 1, (ulong)delivered.Count * 1000,
                LateLoss && Enables > 1 && Volatile.Read(ref cpu) >= 2000 ? 1 : 0, 0, 0, delivered.Count, 0,
                delivered.Count == 0 ? Array.Empty<string>() : new[] { OverheadWorkload.SelectedSymbol });
        }
        public CpuCounterReading ReadCounter() { Owner(); return Journal.ReadCounter(); }
        public CpuCounterSourceSnapshot DescribeCounters() { Owner(); return Journal.Snapshot(); }
        public void Dispose() { Owner(); Disposes++; if (Journal.Enabled) Journal.MarkUnrecordedCleanup(); }
    }

    private static BoundSampleCollection Create(Func<ClrProbeBinding, bool>? current = null) => new(
        new ClrProbeLifetime(ClrProbeBinding.Parse(1, 10, 20, 0x06000001, 2), current ?? (_ => true)),
        CpuSampleOwner.Parse(1, 2), _ => ClrNativeRange.Parse(100, 10));

    private static T Fails<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new InvalidOperationException($"Expected {typeof(T).Name} was not thrown.");
    }

    public static void Run()
    {
        OwnerDrainRejectsCleanupReentry();
        OwnerDrainRejectsBackgroundCollection();
        OwnerDrainsPreserveExclusionAndEpochs();
        OwnerDrainFailureRetainsLossAndStopsCollection();
        FinalDrainPreservesEpochAndCallbackBoundary();
        CallbackFailureDoesNotResume();
        InvalidationDoesNotResume();
        DisableFailurePreventsCallback();
        ResumeFailureRejectsCollection();
        InitialEnableFailureStillStopsSource();
        CounterFailurePreservesDisableEvidence();
        ResumeCounterFailureRejectsCollection();
        LateLossRejectsEarlierEpochs();
        InconsistentPausePreventsCallback();
        WrongThreadControlIsRejected();
        EpochReadsPreserveOwnershipAndRejectInvalidOffsets();
    }

    private static void OwnerDrainRejectsCleanupReentry()
    {
        var collection = Create();
        Source? source = null;
        collection.StartOwnerPausable((_, sink) => source = new Source(sink));
        source!.BeforeDrain = () =>
        {
            Fails<InvalidOperationException>(() => collection.CloseForCleanup());
            Fails<InvalidOperationException>(() => collection.Complete());
            Fails<InvalidOperationException>(() => collection.ExecuteExcluded(_ => throw new InvalidDataException("Recursive exclusion ran.")));
            Fails<InvalidOperationException>(collection.DrainOnOwner);
            Require(source.Disposes == 0 && collection.Cleanup is null, "Cleanup disposed a source with an active owner drain.");
        };
        collection.DrainOnOwner();
        var result = collection.Complete();
        Require(result.Cleanup.CollectorJoined && result.Cleanup.SourceDisposed && result.Cleanup.Errors.Length == 0,
            "Rejected cleanup reentry prevented final ownership release.");
    }

    private static void OwnerDrainRejectsBackgroundCollection()
    {
        var collection = Create();
        collection.StartPausable((_, sink) => new Source(sink));
        try { Fails<InvalidOperationException>(collection.DrainOnOwner); }
        finally { collection.CloseForCleanup(); }
        Require(collection.Cleanup!.Errors.Length == 0, "The rejected owner drain disrupted background cleanup.");
    }

    private static void OwnerDrainsPreserveExclusionAndEpochs()
    {
        var collection = Create();
        Source? source = null;
        int owner = Environment.CurrentManagedThreadId;
        collection.StartOwnerPausable((_, sink) => source = new(sink)
        {
            BeforeDrain = () => Require(Environment.CurrentManagedThreadId == owner, "A drain left the source owner.")
        });
        Require(source!.Drains == 1, "Owner collection started a background drain.");
        Task.Run(() => Fails<InvalidOperationException>(collection.DrainOnOwner)).GetAwaiter().GetResult();
        collection.DrainOnOwner();
        collection.ExecuteExcluded(samples =>
        {
            Require(samples.Count == 1 && samples[0].EligibilityEpoch == 1, "The final owner drain lost the old epoch.");
            int before = source.Drains;
            Fails<InvalidOperationException>(collection.DrainOnOwner);
            Fails<InvalidOperationException>(() => collection.CloseForCleanup());
            Require(source.Drains == before && source.Enables == 1, "Excluded execution admitted a drain or resume.");
        });
        Require(source.Enables == 2, "Owner collection did not resume its source.");
        collection.DrainOnOwner();
        collection.ExecuteExcluded(samples => Require(samples.Count == 2 && samples[1].EligibilityEpoch == 2,
            "The next final drain used the wrong eligibility epoch."));
        var result = collection.Complete();
        Require(result.Cleanup.CollectorJoined && result.Cleanup.SourceDisposed && result.Cleanup.Errors.Length == 0,
            "Owner collection did not release source ownership cleanly.");
        Require(collection.EpochDiagnostics.Select(item => item.EligibilityEpoch).SequenceEqual(new ulong[] { 1, 2, 3 }),
            "Owner cleanup lost cumulative sample epochs.");
        Fails<InvalidOperationException>(collection.DrainOnOwner);
    }

    private static void OwnerDrainFailureRetainsLossAndStopsCollection()
    {
        var collection = Create();
        Source? source = null;
        collection.StartOwnerPausable((_, sink) => source = new(sink));
        collection.ExecuteExcluded(_ => { });
        collection.ExecuteExcluded(_ => { });
        source!.LateLoss = true;
        var original = Fails<InvalidDataException>(collection.DrainOnOwner);
        Require(collection.Latest!.LostRecords == 1, "Owner draining discarded cumulative loss evidence.");
        int drains = source.Drains;
        Require(ReferenceEquals(original, Fails<InvalidDataException>(collection.DrainOnOwner)),
            "Owner collection replaced its first drain failure.");
        var cleanup = collection.CloseForCleanup();
        Require(source.Drains == drains && cleanup.CollectorJoined && cleanup.SourceDisposed
            && cleanup.Errors.Any(error => error.Stage == "collector"),
            "Failed owner collection retried its drain or lost cleanup evidence.");
    }

    private static void FinalDrainPreservesEpochAndCallbackBoundary()
    {
        using var inDrain = new ManualResetEventSlim();
        using var releaseDrain = new ManualResetEventSlim();
        var collection = Create();
        Source? source = null;
        bool callbackReturned = false;
        int firstDrain = 0;
        collection.StartPausable((_, sink) => source = new(sink)
        {
            SkipFirstEnabledDrain = true,
            BeforeDrain = () =>
            {
                if (Interlocked.Increment(ref firstDrain) != 1) return;
                inDrain.Set();
                Require(releaseDrain.Wait(TimeSpan.FromSeconds(5)), "The queued drain was not released by source disable.");
            },
            AfterDisable = releaseDrain.Set,
            BeforeEnable = () =>
            {
                if (source!.Enables == 0)
                    Require(collection.LiveSamples == 0 && source.DescribeCounters().LastReading is { CpuNanoseconds: 0 },
                        "Validation preparation published samples or advanced the event counter before enable.");
                else Require(callbackReturned, "The source resumed before callback return.");
            }
        });
        try
        {
            Require(inDrain.Wait(TimeSpan.FromSeconds(5)), "The collector did not enter its controlled drain.");
            IReadOnlyList<BoundEpochSample>? frozen = null;
            var timing = new BoundPauseTiming();
            collection.ExecuteExcluded(records =>
            {
                frozen = records;
                Require(collection.ReadEpochProvisional(0).SequenceEqual(records)
                    && collection.ReadEpochProvisional(records.Count).Count == 0, "Live epoch reads changed the acknowledged prefix.");
                Require(records.Count == 1 && records[0].EligibilityEpoch == 1 && records[0].Sample.CumulativeCpuNanoseconds == 1000,
                    "The final drain lost the old record or changed its epoch.");
                Require(source!.Enables == 1, "The source was enabled during excluded execution.");
                int drains = source.Drains;
                Fails<InvalidOperationException>(() => collection.ExecuteExcluded(_ => { }));
                Fails<InvalidOperationException>(() => collection.CloseForCleanup());
                Require(source.Drains == drains, "Collection drained inside an excluded callback.");
                callbackReturned = true;
            }, timing);
            long?[] boundaries = [timing.Started, timing.ProducerStopped, timing.DisabledCountersAccepted,
                timing.DrainAcknowledged, timing.BatchValidated, timing.SnapshotReady, timing.CallbackReturned,
                timing.SourceEnabled, timing.EnabledCountersAccepted, timing.CollectorResumed, timing.Completed];
            Require(boundaries.All(value => value is > 0) && boundaries.Zip(boundaries.Skip(1)).All(pair => pair.First <= pair.Second),
                "Successful pause timing lost or reordered a completed boundary.");
            var result = collection.Complete();
            var records = collection.EpochDiagnostics;
            Require(result.Samples.Count == 2 && result.CpuNanoseconds == 2000 && result.FinalCounters.Epoch == 2
                && records.Select(item => item.EligibilityEpoch).SequenceEqual(new ulong[] { 1, 2 })
                && frozen!.Count == 1 && frozen[0].EligibilityEpoch == 1 && source!.Disposes == 1,
                "Pause/resume changed final counters, immutable epoch ownership, or cleanup.");
        }
        finally { releaseDrain.Set(); collection.CloseForCleanup(); }
    }

    private static void CallbackFailureDoesNotResume()
    {
        var collection = Create();
        Source? source = null;
        collection.StartPausable((_, sink) => source = new(sink));
        var failure = new IOException("Controlled callback failure.");
        var timing = new BoundPauseTiming();
        var observed = Fails<IOException>(() => collection.ExecuteExcluded(_ => throw failure, timing));
        Require(timing.SnapshotReady is > 0 && timing.CallbackReturned is null && timing.SourceEnabled is null
            && timing.CollectorResumed is null && timing.Completed is null,
            "Failed callback timing claimed a completed callback or resume.");
        int drains = source!.Drains;
        var cleanup = collection.CloseForCleanup();
        Require(ReferenceEquals(observed, failure) && source.Enables == 1 && source.Drains == drains
            && cleanup.DisableSucceeded && cleanup.CollectorJoined && cleanup.SourceDisposed,
            "A failed callback resumed collection, drained closed admission, or lost cleanup.");
        Fails<InvalidDataException>(() => collection.Complete());
    }

    private static void InvalidationDoesNotResume()
    {
        bool current = true;
        var collection = Create(_ => Volatile.Read(ref current));
        Source? source = null;
        collection.StartPausable((_, sink) => source = new(sink));
        bool invoked = false;
        Fails<InvalidDataException>(() => collection.ExecuteExcluded(_ => { invoked = true; Volatile.Write(ref current, false); }));
        collection.CloseForCleanup();
        Require(invoked && source!.Enables == 1 && source.Disposes == 1, "Invalidation did not run, permitted resume, or lost source cleanup.");
        Fails<InvalidDataException>(() => collection.Complete());
    }

    private static void DisableFailurePreventsCallback()
    {
        var collection = Create();
        Source? source = null;
        collection.StartPausable((_, sink) => source = new(sink) { FailDisable = true });
        bool invoked = false;
        var timing = new BoundPauseTiming();
        Fails<IOException>(() => collection.ExecuteExcluded(_ => invoked = true, timing));
        Require(timing.Started is > 0 && timing.ProducerStopped is null && timing.DrainAcknowledged is null
            && timing.CallbackReturned is null && timing.Completed is null,
            "Failed disable timing claimed producer stop or later work.");
        var cleanup = collection.CloseForCleanup();
        Require(!invoked && source!.Enables == 1 && !cleanup.DisableSucceeded && cleanup.SourceDisposed
            && cleanup.Errors.Any(item => item.Stage == "disable"), "A failed disable permitted payment or disappeared from cleanup evidence.");
        Fails<InvalidDataException>(() => collection.Complete());
    }

    private static void ResumeFailureRejectsCollection()
    {
        var collection = Create();
        Source? source = null;
        collection.StartPausable((_, sink) => source = new(sink) { FailEnableAt = 2 });
        bool invoked = false;
        var timing = new BoundPauseTiming();
        Fails<IOException>(() => collection.ExecuteExcluded(_ => invoked = true, timing));
        Require(timing.CallbackReturned is > 0 && timing.SourceEnabled is null && timing.EnabledCountersAccepted is null
            && timing.CollectorResumed is null && timing.Completed is null,
            "Failed resume timing claimed enabled or completed collection.");
        var cleanup = collection.CloseForCleanup();
        Require(invoked && source!.Enables == 1 && source.Disposes == 1 && source.Stops == 2 && cleanup.DisableSucceeded
            && !cleanup.Errors.Any(item => item.Stage == "disable"), "Resume failure lost the completed callback or reported a false disable failure.");
        Fails<InvalidDataException>(() => collection.Complete());
    }

    private static void InitialEnableFailureStillStopsSource()
    {
        var collection = Create();
        Source? source = null;
        Fails<IOException>(() => collection.StartPausable((_, sink) => source = new(sink) { FailEnableAt = 1 }));
        var cleanup = collection.CloseForCleanup();
        Require(cleanup.DisableSucceeded && cleanup.SourceDisposed && source!.Enables == 0 && source.Stops == 1
            && !cleanup.Errors.Any(item => item.Stage == "disable"), "Initial enable failure produced false disable evidence.");
        Fails<InvalidDataException>(() => collection.Complete());
    }

    private static void CounterFailurePreservesDisableEvidence()
    {
        foreach (bool duringPause in new[] { false, true })
        {
            var collection = Create();
            collection.StartPausable((_, sink) => new Source(sink) { FailReadAfterDisable = true });
            bool invoked = false;
            if (duringPause) Fails<InvalidDataException>(() => collection.ExecuteExcluded(_ => invoked = true));
            var cleanup = collection.CloseForCleanup();
            Require(!invoked && cleanup.DisableSucceeded && cleanup.CollectorJoined && cleanup.SourceDisposed
                && !cleanup.Errors.Any(item => item.Stage == "disable") && collection.FinalCounters!.FailedReads > 0,
                "A counter failure changed physical disable evidence or reached excluded execution.");
            if (!duringPause) Require(cleanup.Errors.Any(item => item.Stage == "counters"), "Cleanup omitted its counter failure.");
            Fails<InvalidDataException>(() => collection.Complete());
        }
    }

    private static void ResumeCounterFailureRejectsCollection()
    {
        foreach (bool regress in new[] { false, true })
        {
            var collection = Create();
            Source? source = null;
            collection.StartPausable((_, sink) => source = new(sink) { FailReadAfterResume = !regress, RegressCounterAfterResume = regress });
            bool invoked = false;
            Fails<InvalidDataException>(() => collection.ExecuteExcluded(_ => invoked = true));
            var cleanup = collection.CloseForCleanup();
            Require(invoked && source!.Enables == 2 && cleanup.DisableSucceeded && cleanup.SourceDisposed
                && !cleanup.Errors.Any(item => item.Stage == "disable") && cleanup.Errors.Any(item => item.Stage == "counters")
                && collection.FinalCounters!.FailedReads > 0,
                "A failed post-enable counter check lost cleanup or source failure evidence.");
            if (regress) Require(collection.FinalCounters!.LastReading is { CpuNanoseconds: 0, Error: not null },
                "The regression fixture did not retain a counter value with its error.");
            Fails<InvalidDataException>(() => collection.Complete());
        }
    }

    private static void LateLossRejectsEarlierEpochs()
    {
        var collection = Create();
        Source? source = null;
        collection.StartPausable((_, sink) => source = new(sink) { LateLoss = true });
        bool paid = false;
        collection.ExecuteExcluded(records => { Require(records.Count == 1, "The first epoch did not drain."); paid = true; });
        Fails<InvalidDataException>(() => collection.Complete());
        Require(paid && collection.Latest!.LostRecords == 1 && source!.Disposes == 1
            && collection.EpochDiagnostics[0].EligibilityEpoch == 1,
            "Late loss did not retain the earlier physical effect, sample ownership, and cumulative evidence.");
    }

    private static void WrongThreadControlIsRejected()
    {
        var collection = Create();
        Source? source = null;
        collection.StartPausable((_, sink) => source = new(sink));
        Task.Run(() =>
        {
            Fails<InvalidOperationException>(() => collection.ExecuteExcluded(_ => { }));
            Fails<InvalidOperationException>(() => collection.CloseForCleanup());
        }).GetAwaiter().GetResult();
        var result = collection.Complete();
        Require(result.FinalCounters.Epoch == 1 && source!.Disposes == 1, "Foreign control changed source ownership.");
    }

    private static void InconsistentPausePreventsCallback()
    {
        var collection = Create();
        Source? source = null;
        collection.StartPausable((_, sink) => source = new(sink) { WrongBatch = true });
        bool invoked = false;
        Fails<InvalidDataException>(() => collection.ExecuteExcluded(_ => invoked = true));
        collection.CloseForCleanup();
        Require(!invoked && source!.Enables == 1 && source.Disposes == 1, "Inconsistent pause accounting reached the excluded callback.");
        Fails<InvalidDataException>(() => collection.Complete());
    }

    private static void EpochReadsPreserveOwnershipAndRejectInvalidOffsets()
    {
        foreach (int offset in new[] { -1, 1 })
        {
            var collection = Create();
            Fails<InvalidOperationException>(() => collection.ReadEpochProvisional(0));
            collection.StartPausable((_, sink) => new Source(sink));
            try
            {
                Require(collection.ReadEpochProvisional(0).Count == 0, "A fresh collection exposed epoch records.");
                Fails<ArgumentOutOfRangeException>(() => collection.ReadEpochProvisional(offset));
            }
            finally { collection.CloseForCleanup(); }
            Fails<InvalidOperationException>(() => collection.ReadEpochProvisional(0));
            Fails<InvalidDataException>(() => collection.Complete());
        }
    }
}
