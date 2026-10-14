static class TimerWorkerLifetimeChecks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private static void Trace(List<string> actual, params string[] expected) =>
        Require(actual.SequenceEqual(expected), $"Lifecycle order differs: {string.Join(", ", actual)}.");

    public static void Run()
    {
        SuccessfulCleanupOrder();
        RegistrationFailureStillRetires();
        RegistrationAndRetirementFailuresRetainBoth();
        BodyFailureStillRetires();
        RetirementFailurePreventsReleaseDespiteJoin();
        FailedJoinPreservesResources();
        JoinExceptionPreservesResources();
        FailedReleasePreventsClose();
        FailedDisposalPreventsRelease();
    }

    private static void SuccessfulCleanupOrder()
    {
        var lifetime = new TimerWorkerLifetime();
        var calls = new List<string>();
        lifetime.Run(() => calls.Add("register"), () => calls.Add("ready"), () => calls.Add("body"),
            () => calls.Add("retire"), () => calls.Add("retain"));
        lifetime.StopAndJoin(() => calls.Add("stop"), () => { calls.Add("join"); return true; },
            () => calls.Add("dispose"), () => calls.Add("release"));
        lifetime.StopAndJoin(() => calls.Add("repeated-stop"), () => { calls.Add("repeated-join"); return true; },
            () => calls.Add("repeated-dispose"), () => calls.Add("repeated-release"));
        Trace(calls, "register", "ready", "body", "ready", "retire", "stop", "join", "dispose", "release");
        Require(lifetime.Retired && lifetime.Joined && lifetime.Released && lifetime.Error is null,
            "Successful cleanup must confirm retirement, join, and release without a worker error.");
    }

    private static void RegistrationFailureStillRetires()
    {
        var lifetime = new TimerWorkerLifetime();
        var calls = new List<string>();
        var expected = new InvalidOperationException("Injected registration error after native ownership.");
        bool active = false;
        lifetime.Run(() => { active = true; calls.Add("register"); throw expected; }, () => calls.Add("ready"),
            () => calls.Add("body"), () => { Require(active, "Retirement must observe partial registration."); active = false; calls.Add("retire"); },
            () => calls.Add("retain"));
        var failure = Throws<InvalidDataException>(() => lifetime.StopAndJoin(() => calls.Add("stop"),
            () => { calls.Add("join"); return true; }, () => calls.Add("dispose"), () => calls.Add("release")));
        Trace(calls, "register", "ready", "retire", "stop", "join", "dispose", "release");
        Require(!active && lifetime.Retired && lifetime.Joined && lifetime.Released
            && ReferenceEquals(lifetime.Error, expected) && ReferenceEquals(failure.InnerException, expected),
            "Partial registration must retire and release while preserving its original failure.");
    }

    private static void RegistrationAndRetirementFailuresRetainBoth()
    {
        var lifetime = new TimerWorkerLifetime();
        var calls = new List<string>();
        var registration = new InvalidOperationException("Injected registration failure.");
        var retirement = new InvalidOperationException("Injected retirement failure.");
        lifetime.Run(() => { calls.Add("register"); throw registration; }, () => calls.Add("ready"),
            () => calls.Add("body"), () => { calls.Add("retire"); throw retirement; }, () => calls.Add("retain"));
        var failure = Throws<InvalidDataException>(() => lifetime.StopAndJoin(() => calls.Add("stop"),
            () => { calls.Add("join"); return false; }, () => calls.Add("dispose"), () => calls.Add("release")));
        Trace(calls, "register", "ready", "retire", "retain", "stop", "join");
        Require(lifetime.Error is AggregateException aggregate && aggregate.InnerExceptions.Count == 2
            && ReferenceEquals(aggregate.InnerExceptions[0], registration) && ReferenceEquals(aggregate.InnerExceptions[1], retirement)
            && ReferenceEquals(failure.InnerException, lifetime.Error) && !lifetime.Retired && !lifetime.Joined && !lifetime.Released,
            "Failed registration and retirement must retain both errors and prohibit disposal and release.");
    }

    private static void BodyFailureStillRetires()
    {
        var lifetime = new TimerWorkerLifetime();
        var calls = new List<string>();
        var expected = new InvalidDataException("Injected checksum failure.");
        lifetime.Run(() => calls.Add("register"), () => calls.Add("ready"), () => { calls.Add("body"); throw expected; },
            () => calls.Add("retire"), () => calls.Add("retain"));
        var failure = Throws<InvalidDataException>(() => lifetime.StopAndJoin(() => calls.Add("stop"),
            () => { calls.Add("join"); return true; }, () => calls.Add("dispose"), () => calls.Add("release")));
        Trace(calls, "register", "ready", "body", "ready", "retire", "stop", "join", "dispose", "release");
        var repeated = Throws<InvalidDataException>(() => lifetime.StopAndJoin(() => calls.Add("repeated-stop"),
            () => { calls.Add("repeated-join"); return true; }, () => calls.Add("repeated-dispose"), () => calls.Add("repeated-release")));
        Trace(calls, "register", "ready", "body", "ready", "retire", "stop", "join", "dispose", "release");
        Require(lifetime.Retired && lifetime.Joined && lifetime.Released && ReferenceEquals(failure.InnerException, expected),
            "A body failure must survive successful cleanup.");
        Require(ReferenceEquals(repeated.InnerException, expected), "Repeated cleanup must preserve the original worker failure.");
    }

    private static void RetirementFailurePreventsReleaseDespiteJoin()
    {
        var lifetime = new TimerWorkerLifetime();
        var calls = new List<string>();
        var expected = new InvalidOperationException("Injected retirement failure.");
        lifetime.Run(() => calls.Add("register"), () => calls.Add("ready"), () => calls.Add("body"),
            () => { calls.Add("retire"); throw expected; }, () => calls.Add("retain"));
        var failure = Throws<InvalidDataException>(() => lifetime.StopAndJoin(() => calls.Add("stop"),
            () => { calls.Add("join"); return true; }, () => calls.Add("dispose"), () => calls.Add("release")));
        Trace(calls, "register", "ready", "body", "ready", "retire", "retain", "stop", "join");
        Require(!lifetime.Retired && lifetime.Joined && !lifetime.Released && ReferenceEquals(failure.InnerException, expected),
            "A join result alone must not authorize release after failed retirement.");
    }

    private static TimerWorkerLifetime Retired()
    {
        var lifetime = new TimerWorkerLifetime();
        lifetime.Run(() => { }, () => { }, () => { }, () => { },
            () => throw new InvalidOperationException("Successful retirement cannot retain the worker."));
        return lifetime;
    }

    private static void FailedJoinPreservesResources()
    {
        var lifetime = Retired();
        var calls = new List<string>();
        Throws<InvalidDataException>(() => lifetime.StopAndJoin(() => calls.Add("stop"),
            () => { calls.Add("join"); return false; }, () => calls.Add("dispose"), () => calls.Add("release")));
        Trace(calls, "stop", "join");
        Require(lifetime.Retired && !lifetime.Joined && !lifetime.Released, "Retirement cannot replace a successful join.");
    }

    private static void JoinExceptionPreservesResources()
    {
        var lifetime = Retired();
        var calls = new List<string>();
        var expected = new InvalidOperationException("Injected join error.");
        var actual = Throws<InvalidOperationException>(() => lifetime.StopAndJoin(() => calls.Add("stop"),
            () => { calls.Add("join"); throw expected; }, () => calls.Add("dispose"), () => calls.Add("release")));
        Trace(calls, "stop", "join");
        Require(ReferenceEquals(actual, expected) && !lifetime.Joined && !lifetime.Released,
            "A join error must preserve the failure and retain resources.");
    }

    private static void FailedReleasePreventsClose()
    {
        var lifetime = Retired();
        var calls = new List<string>();
        var expected = new InvalidOperationException("Injected release failure.");
        var actual = Throws<InvalidOperationException>(() => lifetime.StopAndJoin(() => calls.Add("stop"),
            () => { calls.Add("join"); return true; }, () => calls.Add("dispose"), () => { calls.Add("release"); throw expected; }));
        Trace(calls, "stop", "join", "dispose", "release");
        Require(ReferenceEquals(actual, expected) && lifetime.Retired && lifetime.Joined && !lifetime.Released,
            "Failed release must leave the session-close prerequisite false.");
    }

    private static void FailedDisposalPreventsRelease()
    {
        var lifetime = Retired();
        var calls = new List<string>();
        var expected = new InvalidOperationException("Injected disposal failure.");
        var actual = Throws<InvalidOperationException>(() => lifetime.StopAndJoin(() => calls.Add("stop"),
            () => { calls.Add("join"); return true; }, () => { calls.Add("dispose"); throw expected; }, () => calls.Add("release")));
        Trace(calls, "stop", "join", "dispose");
        Require(ReferenceEquals(actual, expected) && lifetime.Joined && !lifetime.Released,
            "A disposal failure must stop cleanup before release.");
    }
}
