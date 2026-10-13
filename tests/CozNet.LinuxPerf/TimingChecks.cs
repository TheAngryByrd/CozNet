using System.Diagnostics;
using CozNet;

static class TimingChecks
{
    private sealed class Clock : IThreadCpuClock
    {
        public long Value;
        public long ReadNanoseconds() => Value;
    }

    private sealed class ElapsedClock : TimeProvider
    {
        public long Value;
        public override long TimestampFrequency => 1000000000;
        public override long GetTimestamp() => Value;
    }

    public static void CadenceBoundaries()
    {
        var clock = new Clock { Value = 100 };
        var elapsed = new ElapsedClock();
        var cadence = new ProbeCadence(clock, 10, elapsed);
        if (cadence.IsDue()) throw new Exception("CadenceBoundaries: a new cadence was due immediately.");
        clock.Value = 109;
        elapsed.Value = 9;
        if (cadence.IsDue()) throw new Exception("CadenceBoundaries: a probe ran before its CPU interval.");
        if (cadence.CpuClockReads != 1) throw new Exception("CadenceBoundaries: the fast path read the CPU clock.");
        elapsed.Value = 10;
        if (cadence.IsDue() || cadence.CpuClockReads != 2)
            throw new Exception("CadenceBoundaries: elapsed time replaced CPU time.");
        clock.Value = 108;
        elapsed.Value = 11;
        RejectDecreasingClock(cadence);
        clock.Value = 110;
        elapsed.Value = 12;
        if (!cadence.IsDue() || cadence.IsDue())
            throw new Exception("CadenceBoundaries: the interval boundary did not produce exactly one check.");
        clock.Value = 119;
        elapsed.Value = 22;
        if (cadence.IsDue()) throw new Exception("CadenceBoundaries: the next interval began too early.");
        clock.Value = 120;
        elapsed.Value = 23;
        if (!cadence.IsDue()) throw new Exception("CadenceBoundaries: the next interval was missed.");
        clock.Value = 119;
        elapsed.Value = 33;
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
