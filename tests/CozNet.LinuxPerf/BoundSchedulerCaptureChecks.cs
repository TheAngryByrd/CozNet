using System.Diagnostics;
using static CooperativeManagedChecks;

static class BoundSchedulerCaptureChecks
{
    private sealed class Session(int thread) : ISchedulerEventSession
    {
        public int Finishes, Disposals;
        public bool FailDisposal;
        public SchedulerCapture Finish()
        {
            Finishes++;
            return new(Environment.ProcessId, thread, 10, 11, 1000, 1001, 0, [], []);
        }
        public void Dispose()
        {
            Disposals++;
            if (FailDisposal) throw new IOException("Controlled scheduler disposal failure.");
        }
    }

    private sealed class Backend : IBoundSchedulerBackend
    {
        public string ClockDomain => "controlled-monotonic-nanoseconds";
        public readonly List<Session> Sessions = [];
        public Func<ulong> Clock = () => 100;
        public int ClockReads;
        public ISchedulerEventSession StartCurrentThread()
        {
            var session = new Session(Environment.CurrentManagedThreadId);
            Sessions.Add(session);
            return session;
        }
        public ulong ReadClockNanoseconds() { ClockReads++; return Clock(); }
    }

    public static void Run()
    {
        RegistrationUsesOwnerThreadsAndStableRoles();
        ClockFailuresStillStopCollectors();
        FailedDisposalBlocksRetirement();
        MissingBeginAndClockRegressionRemainFailures();
        ClockQueryHasManagedBounds();
    }

    private static void RegistrationUsesOwnerThreadsAndStableRoles()
    {
        var backend = new Backend();
        var capture = new BoundSchedulerCapture(backend);
        var first = new CooperativeInteropChecks.Worker();
        var second = new CooperativeInteropChecks.Worker();
        try
        {
            capture.Register(0);
            int selected = first.Run(() => { capture.Register(1); return Environment.CurrentManagedThreadId; });
            int peer = second.Run(() => { capture.Register(2); return Environment.CurrentManagedThreadId; });
            capture.Begin();
            bool blocked = false;
            try { capture.Retire(() => throw new Exception("Premature retirement.")); }
            catch (InvalidOperationException) { blocked = true; }
            var result = capture.Finish();
            bool retired = false;
            capture.Retire(() => retired = true);
            Require(blocked && retired && result.Stopped && result.Failures.Length == 0 &&
                result.ClockDomain == backend.ClockDomain && result.Captures.Select(item => item!.ThreadId)
                    .SequenceEqual(new[] { Environment.CurrentManagedThreadId, selected, peer }) &&
                result.Captures.Select(item => item!.ThreadId).Distinct().Count() == 3 &&
                backend.Sessions.All(item => item.Finishes == 1 && item.Disposals == 1),
                "Scheduler roles changed owner threads or retired before all collectors stopped.");
            Require(ReferenceEquals(capture.Finish(), result) && backend.ClockReads == 2 &&
                backend.Sessions.All(item => item.Finishes == 1 && item.Disposals == 1),
                "Repeated scheduler completion changed clocks, captures, or cleanup.");
        }
        finally
        {
            bool firstJoined = first.Join();
            bool secondJoined = second.Join();
            Require(firstJoined && secondJoined, "Controlled scheduler workers did not join.");
        }
    }

    private static void ClockFailuresStillStopCollectors()
    {
        var backend = new Backend { Clock = () => throw new IOException("Controlled clock failure.") };
        var capture = new BoundSchedulerCapture(backend);
        capture.Register(0);
        capture.Begin();
        var result = capture.Finish();
        bool retired = false;
        capture.Retire(() => retired = true);
        Require(result.Stopped && retired && result.Begin is { Nanoseconds: null, Failure: not null } &&
            result.End is { Nanoseconds: null, Failure: not null } && result.Failures.Length == 2 &&
            result.Failures.All(error => error.Contains("Controlled clock failure.")) && backend.Sessions[0].Disposals == 1,
            "Clock failure prevented cleanup or lost diagnostic evidence.");
    }

    private static void FailedDisposalBlocksRetirement()
    {
        var backend = new Backend();
        var capture = new BoundSchedulerCapture(backend);
        capture.Register(0);
        capture.Register(2);
        backend.Sessions[0].FailDisposal = true;
        capture.Begin();
        var result = capture.Finish();
        bool retired = false, rejected = false;
        try { capture.Retire(() => retired = true); }
        catch (InvalidOperationException) { rejected = true; }
        Require(!result.Stopped && rejected && !retired && result.Captures[0] is not null &&
            result.Captures[2] is not null && backend.Sessions.All(item => item.Disposals == 1) &&
            result.Failures.Any(error => error.Contains("Controlled scheduler disposal failure.")),
            "Ambiguous scheduler shutdown released a target or skipped another collector.");
    }

    private static void MissingBeginAndClockRegressionRemainFailures()
    {
        var missing = new BoundSchedulerCapture(new Backend()).Finish();
        Require(missing.Stopped && missing.Begin is null && missing.Failures.Length == 1,
            "Missing initial clock evidence was fabricated or accepted.");
        var backend = new Backend();
        var capture = new BoundSchedulerCapture(backend);
        capture.Begin();
        backend.Clock = () => 99;
        var result = capture.Finish();
        Require(result.Begin!.Nanoseconds == 100 && result.End.Nanoseconds == 99 && result.Failures.Length == 1,
            "Regressed scheduler clock evidence was accepted or replaced.");
    }

    private static void ClockQueryHasManagedBounds()
    {
        long during = 0;
        var clock = SchedulerClockObservation.Read(() => { during = Stopwatch.GetTimestamp(); return 100; });
        Require(clock.Failure is null && clock.Before <= during && during <= clock.After &&
            clock.Nanoseconds == 100 && clock.Frequency == Stopwatch.Frequency,
            "The managed interval does not contain the scheduler clock query.");
        var zero = SchedulerClockObservation.Read(() => 0);
        Require(zero.Nanoseconds == 0 && zero.Failure is not null,
            "A zero clock timestamp was accepted or lost its evidence.");
    }
}
