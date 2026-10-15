static class CpuCounterEvidenceChecks
{
    private sealed class Clock(params long[] times) : TimeProvider
    {
        private int index;
        public override long TimestampFrequency => 1000000000;
        public override long GetTimestamp() => times.Length == 0 ? ++index : times[index++];
    }

    public static void Run()
    {
        ReadsRetainIdentityEpochAndBrackets();
        FailedTransitionsPreserveNativeState();
        JournalCapacityPreventsUnrecordedEffects();
        ReadFailuresRetainHighWaterAndOriginalException();
        CounterRangesUseFourReadings();
        InvalidCounterEvidenceCannotProduceRange();
    }

    private static void ReadsRetainIdentityEpochAndBrackets()
    {
        ulong cpu = 0;
        var journal = new CpuCounterJournal(new Clock(), () => cpu);
        var initial = journal.ReadCounter();
        journal.Transition(true, () => { });
        cpu = 1200001;
        var active = journal.ReadCounter();
        journal.Transition(false, () => { });
        var stopped = journal.ReadCounter();
        var firstSnapshot = journal.Snapshot();
        journal.Transition(true, () => { });
        var resumed = journal.ReadCounter();
        Require(initial.Epoch == 0 && !initial.Enabled && initial.CpuNanoseconds == 0 && initial.Before == 1 && initial.After == 2,
            "The initial disabled counter must retain its bracket and zero epoch.");
        Require(active.Epoch == 1 && active.Enabled && active.CpuNanoseconds == 1200001 && active.Before == 5 && active.After == 6,
            "An active read must retain its current epoch and full counter value.");
        Require(stopped.Epoch == 1 && !stopped.Enabled && stopped.CpuNanoseconds == 1200001 && resumed.Epoch == 2 && resumed.Enabled,
            "Disable must preserve the epoch and re-enable must advance it.");
        Require(initial.SourceIdentity.Length > 0 && initial.SourceIdentity == active.SourceIdentity &&
            active.SourceIdentity == stopped.SourceIdentity && stopped.SourceIdentity == resumed.SourceIdentity,
            "All readings must identify the same source instance.");
        Require(firstSnapshot.Transitions.Count == 2 && journal.Snapshot().Transitions.Count == 3 &&
            firstSnapshot.Transitions[0] == new CpuCounterTransition(1, true, true, 3, 4, 1000000000, null) &&
            firstSnapshot.Transitions[1] == new CpuCounterTransition(1, false, true, 7, 8, 1000000000, null),
            "Transition snapshots must preserve native effect order without later mutation.");
    }

    private static void FailedTransitionsPreserveNativeState()
    {
        var journal = new CpuCounterJournal(new Clock(), () => 0);
        var failure = new IOException("Injected enable failure.");
        Require(ReferenceEquals(Failure(() => journal.Transition(true, () => throw failure)), failure),
            "A native transition failure must preserve the original exception.");
        var snapshot = journal.Snapshot();
        Require(!snapshot.Enabled && snapshot.Epoch == 0 && snapshot.Transitions.Count == 1 &&
            !snapshot.Transitions[0].Completed && snapshot.Transitions[0].Error?.Contains(failure.Message, StringComparison.Ordinal) == true,
            "A failed enable must remain disabled and retain its failure.");
        journal.Transition(true, () => { });
        Require(ReferenceEquals(Failure(() => journal.Transition(false, () => throw failure)), failure) && journal.Enabled,
            "A failed disable must retain the enabled state.");
        var regressed = new CpuCounterJournal(new Clock(20, 10), () => 0);
        Require(Failure(() => regressed.Transition(true, () => { })) is InvalidDataException && regressed.Enabled &&
            regressed.Epoch == 1 && regressed.Snapshot().Transitions.Single().Completed,
            "A post-operation clock failure must not erase a completed native effect.");
    }

    private static void JournalCapacityPreventsUnrecordedEffects()
    {
        var journal = new CpuCounterJournal(new Clock(), () => 0, 2);
        int effects = 0;
        Require(Failure(() => journal.Transition(false, () => effects++)) is InvalidOperationException && effects == 0,
            "A redundant transition must fail before its native operation.");
        journal.Transition(true, () => effects++);
        journal.Transition(false, () => effects++);
        Require(Failure(() => journal.Transition(true, () => effects++)) is InvalidOperationException && effects == 2 &&
            !journal.Enabled && journal.Snapshot().Overflowed && journal.Snapshot().Transitions.Count == 2,
            "A full journal must reject new effects while preserving all prior records.");
        journal.MarkUnrecordedCleanup();
        Require(journal.Snapshot().UnrecordedCleanup, "Best-effort cleanup must remain explicit in the journal.");
    }

    private static void ReadFailuresRetainHighWaterAndOriginalException()
    {
        var counts = new Queue<ulong>([100, 90, 95, 110]);
        var journal = new CpuCounterJournal(new Clock(), counts.Dequeue);
        Require(journal.ReadCounter().Error is null && journal.ReadCounter().Error is not null &&
            journal.ReadCounter().Error is not null && journal.ReadCounter().CpuNanoseconds == 110 &&
            journal.Snapshot().FailedReads == 2 && journal.Snapshot().FirstReadFailure is not null,
            "A failed read must not lower the counter high-water mark or erase historical failures.");
        var failure = new IOException("Injected counter read failure.");
        var failing = new CpuCounterJournal(new Clock(10), () => throw failure);
        Require(ReferenceEquals(Failure(() => failing.ReadCounter(propagateFailure: true)), failure) &&
            failing.LastReading is { Before: 10, After: 0, CpuNanoseconds: null, Error: not null } &&
            failing.Snapshot().FailedReads == 1,
            "Propagated read errors must preserve their partial bracket and original exception.");
        var regressed = new CpuCounterJournal(new Clock(20, 10), () => 7);
        Require(regressed.ReadCounter() is { Before: 20, After: 10, CpuNanoseconds: 7, Error: not null },
            "A regressing read clock must retain its raw counter and timestamps with an error.");
    }

    private static MeasurementWindowCapture Window()
    {
        var window = new MeasurementWindowCapture(new Clock(30, 35, 90, 95));
        MeasurementWindowCapture.Readings Read() => new(0, 0, 0, 1, new(0, 0, 0, null), new(0, 0, 0, null));
        window.Begin(Read, () => { });
        window.Finish(Read);
        return window;
    }

    private static CpuCounterReading[] Readings() =>
    [
        new("event-a", 0, false, 10, 20, 1000000000, 0, null),
        new("event-a", 1, true, 40, 50, 1000000000, 20, null),
        new("event-a", 3, true, 70, 80, 1000000000, 70, null),
        new("event-a", 3, true, 100, 110, 1000000000, 90, null)
    ];

    private static void CounterRangesUseFourReadings()
    {
        var readings = Readings();
        var result = Parse(readings);
        Require(result.Value is { Lower: 50, Upper: 90 } && result.Error is null,
            "The inner reads must determine the lower count delta and the outer reads the upper delta.");
        var zero = Parse(readings.Select(reading => reading with { CpuNanoseconds = 0 }).ToArray());
        Require(zero.Value is { Lower: 0, Upper: 0 }, "A disabled or idle event can have a zero count range.");
        var wide = Parse(readings.Select((reading, index) => reading with
            { CpuNanoseconds = index < 2 ? 0UL : ulong.MaxValue }).ToArray());
        Require(wide.Value is { Lower: ulong.MaxValue, Upper: ulong.MaxValue }, "Count ranges must preserve unsigned 64-bit precision.");
    }

    private static void InvalidCounterEvidenceCannotProduceRange()
    {
        var window = Window();
        Require(EnabledCpuCountRange.Parse(null, null, null, null, window.Start, window.End).Value is null,
            "Missing counters must not produce a range.");
        var valid = Readings();
        for (int index = 0; index < valid.Length; index++)
        {
            CpuCounterReading[] invalid =
            [
                valid[index] with { CpuNanoseconds = null }, valid[index] with { Error = "failed" },
                valid[index] with { SourceIdentity = "other-event" }, valid[index] with { SourceIdentity = "" },
                valid[index] with { Before = -1 }, valid[index] with { After = valid[index].Before - 1 },
                valid[index] with { TimestampFrequency = 0 }, valid[index] with { TimestampFrequency = 1000000 }
            ];
            foreach (var reading in invalid)
            {
                var changed = (CpuCounterReading[])valid.Clone();
                changed[index] = reading;
                var result = Parse(changed);
                Require(result.Value is null && result.Error is not null, $"Invalid counter reading at index {index} was accepted.");
            }
        }
        Require(Parse([valid[0], valid[1], valid[2] with { CpuNanoseconds = 19 }, valid[3]]).Value is null,
            "A counter regression must reject the range.");
        Require(Parse([valid[0], valid[1], valid[2] with { Epoch = 0 }, valid[3]]).Value is null,
            "An epoch regression must reject the range.");
        Require(Parse([valid[0], valid[1], valid[2] with { Before = 49 }, valid[3]]).Value is null,
            "Overlapping counter read brackets must reject the range.");
        Require(Parse([valid[0] with { After = 31 }, valid[1], valid[2], valid[3]]).Value is null &&
            Parse([valid[0], valid[1] with { Before = 34 }, valid[2], valid[3]]).Value is null &&
            Parse([valid[0], valid[1], valid[2] with { After = 91 }, valid[3]]).Value is null &&
            Parse([valid[0], valid[1], valid[2], valid[3] with { Before = 94 }]).Value is null,
            "Every counter bracket must lie on its declared side of the measurement endpoint.");
    }

    private static (EnabledCpuCountRange? Value, string? Error) Parse(CpuCounterReading[] readings)
    {
        var window = Window();
        return EnabledCpuCountRange.Parse(readings[0], readings[1], readings[2], readings[3], window.Start, window.End);
    }

    private static Exception? Failure(Action action)
    {
        try { action(); return null; }
        catch (Exception error) { return error; }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
