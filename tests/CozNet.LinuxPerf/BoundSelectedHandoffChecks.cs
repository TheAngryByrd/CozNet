using static CooperativeManagedChecks;

static class BoundSelectedHandoffChecks
{
    private sealed class Backend : ISelectedHandoffBackend
    {
        public int Calls, Thread;
        public SelectedHandoffResult Result = SelectedHandoffResult.Attempted;
        public Exception? Failure;
        public SelectedHandoffResult TryHandoff()
        {
            Calls++;
            Thread = Environment.CurrentManagedThreadId;
            if (Failure is not null) throw Failure;
            return Result;
        }
    }

    public static void Run()
    {
        ModesParseBeforeUse();
        ThresholdAndPendingStateControlAttempts();
        EachAdmissionAttemptsAtMostOnceOnItsCaller();
        FailuresKeepOriginalExceptionAndAttempt();
        UnavailableAndUnknownResultsReject();
    }

    private static void ModesParseBeforeUse()
    {
        foreach (string? mode in new string?[] { null, "0", "1" })
        {
            var backend = new Backend();
            var policy = BoundSelectedHandoff.Parse(mode, backend);
            bool attempted = policy.TryHandoff(500000, true);
            Require(policy.Enabled == (mode == "1") && attempted == (mode == "1")
                && policy.Attempts == (mode == "1" ? 1 : 0) && backend.Calls == policy.Attempts,
                "Selected handoff activation changed its declared policy or called a disabled backend.");
        }
        foreach (string mode in new[] { "", "true", "2", " 1" })
        {
            var backend = new Backend();
            bool rejected = false;
            try { BoundSelectedHandoff.Parse(mode, backend); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected && backend.Calls == 0, "Invalid selected handoff mode reached the backend.");
        }
    }

    private static void ThresholdAndPendingStateControlAttempts()
    {
        foreach (long debt in new long[] { 0, 499999, 500000, 500001, 2000000 })
        foreach (bool pending in new[] { false, true })
        {
            var backend = new Backend();
            var policy = BoundSelectedHandoff.Parse("1", backend);
            bool expected = pending && debt >= 500000;
            Require(policy.TryHandoff(debt, pending) == expected
                && policy.Attempts == (expected ? 1 : 0) && backend.Calls == policy.Attempts,
                $"Handoff threshold failed for debt={debt}, pending={pending}.");
        }
        var unused = new Backend();
        var invalid = BoundSelectedHandoff.Parse("1", unused);
        bool rejected = false;
        try { invalid.TryHandoff(-1, true); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Require(rejected && invalid.Attempts == 0 && unused.Calls == 0, "Negative debt caused a handoff.");
    }

    private static void EachAdmissionAttemptsAtMostOnceOnItsCaller()
    {
        var backend = new Backend();
        var policy = BoundSelectedHandoff.Parse("1", backend);
        for (int admission = 1; admission <= 3; admission++)
            Require(policy.TryHandoff(500000, true) && policy.Attempts == admission && backend.Calls == admission
                && backend.Thread == Environment.CurrentManagedThreadId,
                "Pending service caused repeated handoffs within one admission or changed the executing thread.");
        Require(!policy.TryHandoff(500000, false) && policy.Attempts == 3 && backend.Calls == 3,
            "A completed request caused another handoff or reset its count.");
    }

    private static void FailuresKeepOriginalExceptionAndAttempt()
    {
        var original = new OperationCanceledException("Controlled handoff failure.");
        var backend = new Backend { Failure = original };
        var policy = BoundSelectedHandoff.Parse("1", backend);
        Exception? observed = null;
        try { policy.TryHandoff(500000, true); }
        catch (Exception error) { observed = error; }
        Require(ReferenceEquals(original, observed) && backend.Calls == 1 && policy.Attempts == 1,
            "A failed handoff changed its exception, retried, or lost its attempt.");
    }

    private static void UnavailableAndUnknownResultsReject()
    {
        foreach (var result in new[] { SelectedHandoffResult.Unavailable, (SelectedHandoffResult)99 })
        {
            var backend = new Backend { Result = result };
            var policy = BoundSelectedHandoff.Parse("1", backend);
            Exception? observed = null;
            try { policy.TryHandoff(500000, true); }
            catch (Exception error) { observed = error; }
            Require((result == SelectedHandoffResult.Unavailable ? observed is PlatformNotSupportedException
                    : observed is InvalidDataException) && policy.Attempts == 1 && backend.Calls == 1,
                "An unavailable or unknown backend result passed as a completed handoff.");
        }
    }
}
