static class MeasurementWindowCaptureChecks
{
    private sealed class Clock(params long[] timestamps) : TimeProvider
    {
        public int Reads;
        public int FailOnRead = int.MaxValue;
        public long Frequency = 1000000;
        public bool FailFrequency;
        public readonly Exception Failure = new IOException("Injected measurement clock failure.");
        public override long TimestampFrequency => FailFrequency ? throw Failure : Frequency;
        public override long GetTimestamp()
        {
            if (++Reads == FailOnRead) throw Failure;
            return timestamps[Reads - 1];
        }
    }

    private static MeasurementWindowCapture.Readings Empty() => new(0, 0, 0, 1,
        new(0, 0, 0, null), new(0, 0, 0, null));

    public static void Run()
    {
        PreparationPreservesTheRealWindow();
        PreparationCannotRepeatOrFollowStart();
        FailedPreparationBlocksMeasurement();
        PreparationClockMustMatchAndPrecedeMeasurement();
        PreparationConstructorFailurePreventsReuse();
        FrozenEndExcludesShutdownChanges();
        ReleaseOccursInsideValidatedStartBracket();
        FailedFinishCannotCaptureShutdownState();
        InvalidReadingsPreventRelease();
        ClockRegressionCannotPublishBoundary();
        InvalidTransitionsDoNotInvokeCallbacks();
        CumulativeCountersCannotRegress();
        TimerSnapshotMustMatchWorkerAccounting();
        TimerInventoryCannotRegressOrChangeCapability();
    }

    private static void PreparationPreservesTheRealWindow()
    {
        var clock = new Clock(10, 20, 30, 40, 100, 110, 200, 210);
        var capture = new MeasurementWindowCapture(clock);
        int reads = 0, releases = 0;
        var initial = Empty();
        capture.Prepare(() => { reads++; return initial; });
        Require(reads == 2 && clock.Reads == 4 && capture.Status == MeasurementCaptureStatus.NotStarted
            && capture.Start is null && capture.End is null && capture.Preparation!.Status == MeasurementCaptureStatus.Complete
            && capture.Preparation.Start!.BeforeTimestamp == 10 && capture.Preparation.End!.AfterTimestamp == 40,
            "Preparation must retain its own complete window without starting measurement.");
        capture.Begin(() => initial with { Peer = new(7, 0, 0, null) }, () => releases++);
        capture.Finish(() => initial with { Selected = new(3, 0, 0, null), Peer = new(9, 0, 0, null) });
        Require(releases == 1 && capture.Status == MeasurementCaptureStatus.Complete && capture.Start!.BeforeTimestamp == 100
            && capture.Start.Accounting.Peer.Progress == 7 && capture.End!.AfterTimestamp == 210
            && capture.End.Accounting.Selected.Progress == 3 && capture.Preparation!.End!.Accounting == initial,
            "Preparation must not supply measured progress, release the workload, or change the final window.");
    }

    private static void PreparationCannotRepeatOrFollowStart()
    {
        var untouchedClock = new Clock(10, 20, 30, 40);
        var untouched = new MeasurementWindowCapture(untouchedClock);
        Require(Failure(() => untouched.Prepare(null!)) is ArgumentNullException
            && untouched.Preparation is null && !untouched.PreparationAttempted && untouchedClock.Reads == 0,
            "A missing preparation reader must reject without changing state or reading the clock.");
        foreach (bool prepareFirst in new[] { false, true })
        {
            var clock = new Clock(10, 20, 30, 40);
            var capture = new MeasurementWindowCapture(clock);
            if (prepareFirst) capture.Prepare(Empty);
            else capture.Begin(Empty, () => { });
            int reads = clock.Reads;
            bool invoked = false;
            Require(Failure(() => capture.Prepare(() => { invoked = true; return Empty(); })) is InvalidOperationException
                && !invoked && clock.Reads == reads,
                "Repeated or late preparation must reject before reading clocks or accounting.");
        }
    }

    private static void FailedPreparationBlocksMeasurement()
    {
        foreach (int failRead in new[] { 1, 2 })
        {
            var clock = new Clock(10, 20, 30, 40);
            var capture = new MeasurementWindowCapture(clock);
            var expected = new IOException("Controlled preparation failure.");
            int reads = 0;
            Require(ReferenceEquals(Failure(() => capture.Prepare(() => ++reads == failRead ? throw expected : Empty())), expected),
                "Preparation must preserve the original accounting failure.");
            var preparation = capture.Preparation!;
            int clockReads = clock.Reads;
            bool invoked = false;
            Require(Failure(() => capture.Begin(() => { invoked = true; return Empty(); }, () => invoked = true)) is InvalidOperationException
                && Failure(() => capture.Prepare(Empty)) is InvalidOperationException
                && !invoked && clock.Reads == clockReads && ReferenceEquals(capture.Preparation, preparation)
                && preparation.Status == (failRead == 1 ? MeasurementCaptureStatus.StartFailed : MeasurementCaptureStatus.FinishFailed)
                && capture.Start is null && capture.End is null,
                "Failed preparation must remain visible and prevent measurement or a later preparation attempt.");
        }
    }

    private static void PreparationClockMustMatchAndPrecedeMeasurement()
    {
        foreach (long start in new[] { 39L, 40L })
        {
            var clock = new Clock(10, 20, 30, 40, start, 41);
            var capture = new MeasurementWindowCapture(clock);
            capture.Prepare(Empty);
            if (start == 39)
            {
                bool invoked = false;
                Require(Failure(() => capture.Begin(() => { invoked = true; return Empty(); }, () => invoked = true)) is InvalidDataException
                    && !invoked && capture.Start is null && capture.Status == MeasurementCaptureStatus.StartFailed,
                    "An overlapping start must reject before reading accounting or releasing work.");
            }
            else
            {
                capture.Begin(Empty, () => { });
                Require(capture.Status == MeasurementCaptureStatus.Active && capture.Start!.BeforeTimestamp == 40,
                    "Measurement must accept the exact preparation endpoint.");
            }
        }
        var changedClock = new Clock(10, 20, 30, 40);
        var changed = new MeasurementWindowCapture(changedClock);
        changedClock.Frequency++;
        Require(Failure(() => changed.Prepare(Empty)) is InvalidDataException
            && changedClock.Reads == 0 && changed.Preparation is not null
            && Failure(() => changed.Begin(Empty, () => { })) is InvalidOperationException,
            "A changed preparation frequency must prevent timestamp reads and measurement.");
    }

    private static void PreparationConstructorFailurePreventsReuse()
    {
        foreach (bool throws in new[] { false, true })
        {
            var clock = new Clock(10, 20, 30, 40);
            var capture = new MeasurementWindowCapture(clock);
            clock.Frequency = 0;
            clock.FailFrequency = throws;
            var error = Failure(() => capture.Prepare(Empty));
            Require((throws ? ReferenceEquals(error, clock.Failure) : error is ArgumentOutOfRangeException)
                && capture.PreparationAttempted && capture.Preparation is null && clock.Reads == 0,
                "Failed preparation construction must preserve its attempt and original exception.");
            clock.Frequency = 1000000;
            clock.FailFrequency = false;
            bool invoked = false;
            Require(Failure(() => capture.Begin(() => { invoked = true; return Empty(); }, () => invoked = true)) is InvalidOperationException
                && Failure(() => capture.Prepare(() => { invoked = true; return Empty(); })) is InvalidOperationException
                && !invoked && clock.Reads == 0 && capture.Start is null && capture.End is null,
                "Restoring the clock must not permit reuse after failed preparation construction.");
        }
    }

    private static void TimerSnapshotMustMatchWorkerAccounting()
    {
        var worker = new TimerServiceController.WorkerAccounting(0, null);
        var timer = new TimerServiceController.AccountingSnapshot(0, worker, worker);
        var prefix = new TimerServiceController.PendingAccounting(Guid.NewGuid(), 1, 7, 1, 10, 100, false, false, false, null);
        var valid = Empty() with { Timer = timer };
        MeasurementWindowCapture.Readings[] invalid =
        [
            valid with { Timer = timer with { ReleasedJournalCount = -1 } },
            valid with { Timer = timer with { Selected = null! } },
            valid with { Timer = timer with { Peer = null! } },
            valid with { Timer = timer with { Selected = worker with { ReleasedWait = 1 } } },
            valid with { Timer = timer with { Peer = worker with { ReleasedWait = 1 } } },
            valid with { Timer = timer with { Peer = worker with { Pending = prefix } } },
            valid with { Peer = new(0, 0, 9, new(1, 7)), Timer = timer with { Peer = worker with { Pending = prefix } } },
            valid with { Peer = new(0, 0, 10, new(2, 7)), Timer = timer with { Peer = worker with { Pending = prefix } } },
            valid with { Peer = new(0, 0, 10, new(1, 8)), Timer = timer with { Peer = worker with { Pending = prefix } } }
        ];
        foreach (var reading in invalid)
        {
            var capture = new MeasurementWindowCapture(new Clock(100, 110));
            bool released = false;
            Require(Failure(() => capture.Begin(() => reading, () => released = true)) is InvalidDataException
                && !released && capture.Start is null && capture.Status == MeasurementCaptureStatus.StartFailed,
                "Mismatched timer totals, identity, or journal inventory must fail before worker release.");
        }
    }

    private static void TimerInventoryCannotRegressOrChangeCapability()
    {
        var worker = new TimerServiceController.WorkerAccounting(0, null);
        var timer = new TimerServiceController.AccountingSnapshot(2, worker, worker);
        foreach (var pair in new[]
        {
            (Empty() with { Timer = timer }, Empty() with { Timer = timer with { ReleasedJournalCount = 1 } }),
            (Empty() with { Timer = timer }, Empty()),
            (Empty(), Empty() with { Timer = timer })
        })
        {
            var capture = new MeasurementWindowCapture(new Clock(100, 110, 200, 210));
            capture.Begin(() => pair.Item1, () => { });
            Require(Failure(() => capture.Finish(() => pair.Item2)) is InvalidDataException
                && capture.End is null && capture.Status == MeasurementCaptureStatus.FinishFailed,
                "The end boundary must not lose timer journals or change the captured timer capability.");
        }
    }

    private static void FrozenEndExcludesShutdownChanges()
    {
        var clock = new Clock(100, 110, 1000, 1010);
        var capture = new MeasurementWindowCapture(clock);
        var readings = Empty();
        capture.Begin(() => readings, () => { });
        readings = new(200, 100, 9, 3, new(11, 40, 10, new(1, 7)), new(17, 70, 20, new(2, 0)));
        capture.Finish(() => readings);
        readings = new(900, 800, 19, 5, new(101, 100, 0, null), new(107, 900, 0, null));
        capture.Finish(() => throw new InvalidOperationException("Shutdown must not replace the end boundary."));
        var end = capture.End!;
        Require(capture.Status == MeasurementCaptureStatus.Complete && capture.Start!.Accounting == Empty()
            && end.BeforeTimestamp == 1000 && end.AfterTimestamp == 1010 && end.TimestampFrequency == 1000000
            && end.Accounting == new MeasurementWindowCapture.Readings(200, 100, 9, 3,
                new(11, 40, 10, new(1, 7)), new(17, 70, 20, new(2, 0))) && clock.Reads == 4,
            "The frozen end must retain both workers, pending generations, sample identity, accounting, and its original clock bracket.");
    }

    private static void ReleaseOccursInsideValidatedStartBracket()
    {
        var clock = new Clock(100, 110);
        var capture = new MeasurementWindowCapture(clock);
        bool read = false, released = false;
        capture.Begin(() => { Require(clock.Reads == 1, "The opening clock must precede the initial read."); read = true; return Empty(); },
            () => { Require(read && clock.Reads == 1 && capture.Start is null,
                "Release must follow the initial read and precede the closing clock and published boundary."); released = true; });
        Require(released && capture.Status == MeasurementCaptureStatus.Active && capture.Start!.BeforeTimestamp == 100
            && capture.Start.AfterTimestamp == 110, "The start boundary must bracket worker release.");
    }

    private static void FailedFinishCannotCaptureShutdownState()
    {
        foreach (bool failRead in new[] { false, true })
        {
            var clock = new Clock(100, 110, 1000, 1010) { FailOnRead = failRead ? int.MaxValue : 4 };
            var capture = new MeasurementWindowCapture(clock);
            capture.Begin(Empty, () => { });
            var readFailure = new IOException("Injected measurement read failure.");
            var error = Failure(() => capture.Finish(() => failRead ? throw readFailure : Empty()));
            int reads = clock.Reads;
            capture.Finish(() => throw new InvalidOperationException("A failed capture must not retry during shutdown."));
            Require(ReferenceEquals(error, failRead ? readFailure : clock.Failure)
                && capture.Status == MeasurementCaptureStatus.FinishFailed && capture.End is null
                && capture.Start is not null && clock.Reads == reads,
                "Failed end capture must preserve its exception, start boundary, and failed state without a later capture.");
        }
    }

    private static void InvalidReadingsPreventRelease()
    {
        var valid = Empty();
        MeasurementWindowCapture.Readings[] invalid =
        [
            valid with { Global = -1 }, valid with { Credit = -1 },
            valid with { Selected = valid.Selected with { Progress = -1 } },
            valid with { Peer = valid.Peer with { Progress = -1 } },
            valid with { Selected = valid.Selected with { ReleasedWait = -1 } },
            valid with { Peer = valid.Peer with { ReleasedWait = -1 } },
            valid with { Selected = valid.Selected with { ActiveWait = -1 } },
            valid with { Peer = valid.Peer with { ActiveWait = -1 } },
            valid with { Selected = valid.Selected with { ActiveWait = 1 } },
            valid with { Peer = valid.Peer with { Pending = new(0, 1) } },
            valid with { Selected = null! }, valid with { Peer = null! }, null!
        ];
        foreach (var readings in invalid)
        {
            var capture = new MeasurementWindowCapture(new Clock(100, 110));
            bool released = false;
            Require(Failure(() => capture.Begin(() => readings, () => released = true)) is InvalidDataException
                && !released && capture.Start is null && capture.Status == MeasurementCaptureStatus.StartFailed,
                "Invalid measurement readings must fail before worker release and cannot publish a start boundary.");
        }
    }

    private static void ClockRegressionCannotPublishBoundary()
    {
        var start = new MeasurementWindowCapture(new Clock(100, 99));
        Require(Failure(() => start.Begin(Empty, () => { })) is InvalidDataException
            && start.Start is null && start.Status == MeasurementCaptureStatus.StartFailed,
            "A backward start clock must not publish a boundary.");
        foreach (var clock in new[] { new Clock(100, 110, 200, 199), new Clock(100, 110, 109, 120) })
        {
            var capture = new MeasurementWindowCapture(clock);
            capture.Begin(Empty, () => { });
            Require(Failure(() => capture.Finish(Empty)) is InvalidDataException
                && capture.End is null && capture.Status == MeasurementCaptureStatus.FinishFailed,
                "A backward end clock or an end before the start bracket must fail without a published end boundary.");
        }
        Require(Failure(() => new MeasurementWindowCapture(new Clock { Frequency = 0 })) is ArgumentOutOfRangeException,
            "An unusable timestamp frequency must fail before capture.");
    }

    private static void InvalidTransitionsDoNotInvokeCallbacks()
    {
        var capture = new MeasurementWindowCapture(new Clock(100, 110));
        bool invoked = false;
        Require(Failure(() => capture.Finish(() => { invoked = true; return Empty(); })) is InvalidOperationException
            && !invoked && capture.Status == MeasurementCaptureStatus.NotStarted,
            "Finishing before start must not read a measurement or change its state.");
        var releaseFailure = new IOException("Injected start release failure.");
        Require(ReferenceEquals(Failure(() => capture.Begin(Empty, () => throw releaseFailure)), releaseFailure)
            && capture.Start is null && capture.Status == MeasurementCaptureStatus.StartFailed,
            "Failed release must prevent a completed start boundary.");
        Require(Failure(() => capture.Begin(() => { invoked = true; return Empty(); }, () => { })) is InvalidOperationException
            && !invoked, "A failed start must not be retried with a later boundary.");
    }

    private static Exception Failure(Action action)
    {
        try { action(); } catch (Exception error) { return error; }
        throw new InvalidOperationException("Expected a measurement capture failure.");
    }

    private static void CumulativeCountersCannotRegress()
    {
        var initial = new MeasurementWindowCapture.Readings(200, 100, 9, 3,
            new(11, 40, 0, null), new(17, 70, 0, null));
        MeasurementWindowCapture.Readings[] regressed =
        [
            initial with { Global = 199 }, initial with { Credit = 99 },
            initial with { AcceptedSampleSequence = 8 }, initial with { Epoch = 2 },
            initial with { Selected = initial.Selected with { Progress = 10 } },
            initial with { Peer = initial.Peer with { Progress = 16 } },
            initial with { Selected = initial.Selected with { ReleasedWait = 39 } },
            initial with { Peer = initial.Peer with { ReleasedWait = 69 } }
        ];
        foreach (var end in regressed)
        {
            var capture = new MeasurementWindowCapture(new Clock(100, 110, 200, 210));
            capture.Begin(() => initial, () => { });
            Require(Failure(() => capture.Finish(() => end)) is InvalidDataException
                && capture.End is null && capture.Status == MeasurementCaptureStatus.FinishFailed,
                "A decreasing cumulative counter must not produce a completed measurement window.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
