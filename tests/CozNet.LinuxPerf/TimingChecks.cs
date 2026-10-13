using System.Diagnostics;
using CozNet;

static class TimingChecks
{
    private sealed class Clock : IThreadCpuClock
    {
        public long Value;
        public long ReadNanoseconds() => Value;
    }

    public static void CadenceBoundaries()
    {
        var clock = new Clock { Value = 100 };
        var cadence = new ProbeCadence(clock, 10);
        if (cadence.IsDue()) throw new Exception("CadenceBoundaries: a new cadence was due immediately.");
        clock.Value = 109;
        if (cadence.IsDue()) throw new Exception("CadenceBoundaries: a probe ran before its CPU interval.");
        clock.Value = 108;
        RejectDecreasingClock(cadence);
        clock.Value = 110;
        if (!cadence.IsDue() || cadence.IsDue())
            throw new Exception("CadenceBoundaries: the interval boundary did not produce exactly one check.");
        clock.Value = 119;
        if (cadence.IsDue()) throw new Exception("CadenceBoundaries: the next interval began too early.");
        clock.Value = 120;
        if (!cadence.IsDue()) throw new Exception("CadenceBoundaries: the next interval was missed.");
        clock.Value = 119;
        RejectDecreasingClock(cadence);
    }

    private static void RejectDecreasingClock(ProbeCadence cadence)
    {
        try { cadence.IsDue(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("CadenceBoundaries: a decreasing CPU clock was accepted.");
    }

    public static void WaitDurationAndCancellation(IWaitBackend backend)
    {
        var duration = DelayTicksModule.tryCreate(Stopwatch.Frequency / 1000).ResultValue;
        long start = Stopwatch.GetTimestamp();
        backend.Wait(duration, CancellationToken.None);
        if (Stopwatch.GetTimestamp() - start < DelayTicksModule.value(duration))
            throw new Exception("WaitDurationAndCancellation: the backend returned before the requested duration.");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try { backend.Wait(duration, cancellation.Token); }
        catch (OperationCanceledException error) when (error.CancellationToken == cancellation.Token) { return; }
        throw new Exception("WaitDurationAndCancellation: the backend ignored cancellation.");
    }
}
