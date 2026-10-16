static class SchedulerCaptureGroupChecks
{
    private sealed class Session : ISchedulerEventSession
    {
        public SchedulerCapture Capture = new(1, 2, 10, 11, 20, 21, 0, [], []);
        public Exception? FinishError;
        public Exception? DisposeError;
        public Action? DuringDispose;
        public int Finishes, Disposals;
        public SchedulerCapture Finish()
        {
            Finishes++;
            if (FinishError is not null) throw FinishError;
            return Capture;
        }
        public void Dispose()
        {
            Disposals++;
            DuringDispose?.Invoke();
            if (DisposeError is not null) throw DisposeError;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("The scheduler gate accepted an invalid transition.");
    }

    public static void Run()
    {
        DisabledCaptureAllowsRetirement();
        FinishPrecedesRetirement();
        FailedDisposalRetainsTarget();
        FinishFailurePreservesEvidenceAndDisposes();
        LossRemainsFailureAfterCleanup();
        RegistrationCannotReplaceOrReopen();
        PartialRegistrationStillStopsExistingCollectors();
    }

    private static void DisabledCaptureAllowsRetirement()
    {
        var group = new SchedulerCaptureGroup(null);
        int retired = 0;
        group.Retire(() => retired++, TimeSpan.Zero);
        var result = group.Finish();
        Require(retired == 1 && result.Stopped && result.Errors.Length == 0 && result.Captures.All(capture => capture is null),
            "Disabled capture must not block retirement or create evidence.");
    }

    private static void FinishPrecedesRetirement()
    {
        var sessions = new[] { new Session(), new Session(), new Session() };
        int next = 0, retired = 0;
        var group = new SchedulerCaptureGroup(() => sessions[next++]);
        int blockedDuringDisposal = 0;
        foreach (var session in sessions)
            session.DuringDispose = () =>
            {
                Reject(() => group.Retire(() => retired++, TimeSpan.Zero));
                blockedDuringDisposal++;
            };
        for (int index = 0; index < 3; index++) group.Register(index);
        Reject(() => group.Retire(() => retired++, TimeSpan.Zero));
        Require(retired == 0, "An active collector must block native retirement.");
        var result = group.Finish();
        group.Retire(() => retired++, TimeSpan.Zero);
        Require(retired == 1 && blockedDuringDisposal == 3 && result.Stopped && result.Errors.Length == 0
            && result.Captures.SequenceEqual(sessions.Select(session => session.Capture))
            && sessions.All(session => session.Finishes == 1 && session.Disposals == 1),
            "Every collector must finish and dispose before retirement, with its capture retained in slot order.");
        Require(ReferenceEquals(group.Finish(), result) && sessions.All(session => session.Finishes == 1 && session.Disposals == 1),
            "Repeated shutdown must preserve evidence without repeating collector operations.");
    }

    private static void FailedDisposalRetainsTarget()
    {
        var failure = new IOException("Injected collector disposal failure.");
        var failed = new Session { DisposeError = failure };
        var other = new Session();
        int next = 0;
        var group = new SchedulerCaptureGroup(() => next++ == 0 ? failed : other);
        group.Register(0);
        group.Register(2);
        var result = group.Finish();
        var lifetime = new TimerWorkerLifetime();
        bool retired = false, retained = false;
        lifetime.Run(() => { }, () => { }, () => { },
            () => group.Retire(() => retired = true, TimeSpan.Zero), () => retained = true);
        Require(!result.Stopped && result.Errors.Any(error => ReferenceEquals(error, failure))
            && failed.Disposals == 1 && other.Disposals == 1 && other.Finishes == 1
            && !retired && retained && !lifetime.Retired && !lifetime.Released,
            "A failed disposal must retain the real worker lifetime while other collectors still stop.");
        Require(ReferenceEquals(group.Finish(), result) && failed.Disposals == 1,
            "An ambiguous disposal must not be retried or forgotten.");
    }

    private static void FinishFailurePreservesEvidenceAndDisposes()
    {
        var failure = new IOException("Injected capture failure.");
        var session = new Session { FinishError = failure };
        var group = new SchedulerCaptureGroup(() => session);
        group.Register(1);
        var result = group.Finish();
        bool retired = false;
        group.Retire(() => retired = true, TimeSpan.Zero);
        Require(result.Stopped && retired && result.Errors.Any(error => ReferenceEquals(error, failure))
            && result.Captures[1] is null && session.Disposals == 1,
            "Successful disposal permits retirement but must preserve the failed capture without fabricated evidence.");
    }

    private static void LossRemainsFailureAfterCleanup()
    {
        foreach (var capture in new[]
        {
            new Session().Capture with { FinalLost = null },
            new Session().Capture with { FinalLost = 3 },
            new Session().Capture with { Records = [new(SchedulerRecordKind.Loss, 0, 15, 1)] },
            new Session().Capture with { Failures = ["Injected capture error."] }
        })
        {
            var session = new Session { Capture = capture };
            var group = new SchedulerCaptureGroup(() => session);
            group.Register(2);
            var result = group.Finish();
            Require(result.Stopped && result.Errors.Length > 0 && ReferenceEquals(result.Captures[2], capture)
                && session.Disposals == 1,
                "Missing loss counts, nonzero loss, loss records, and capture errors must survive successful cleanup.");
        }
    }

    private static void RegistrationCannotReplaceOrReopen()
    {
        var session = new Session();
        int calls = 0;
        var group = new SchedulerCaptureGroup(() => { calls++; return session; });
        group.Register(0);
        Reject(() => group.Register(0));
        group.Finish();
        Reject(() => group.Register(1));
        Require(calls == 1 && session.Disposals == 1, "Duplicate and late registration must not create or replace a collector.");
    }

    private static void PartialRegistrationStillStopsExistingCollectors()
    {
        var session = new Session();
        int calls = 0;
        var group = new SchedulerCaptureGroup(() => ++calls == 1 ? session : throw new InvalidOperationException("Injected startup failure."));
        group.Register(0);
        Reject(() => group.Register(1));
        var result = group.Finish();
        Require(result.Stopped && session.Disposals == 1 && result.Captures[0] == session.Capture && result.Captures[1] is null,
            "A later registration failure must not prevent cleanup of existing collectors.");
    }
}
