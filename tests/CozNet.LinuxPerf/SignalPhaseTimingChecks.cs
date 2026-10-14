static class SignalPhaseTimingChecks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Run()
    {
        SequentialPhases();
        HandlerCanFinishBeforeQueueReturns();
        EqualBoundariesAreValid();
        LargeClockValuesKeepSmallDurations();
        RejectMalformedTimings();
    }

    private static void SequentialPhases()
    {
        var timing = SignalPhaseTiming.Parse(10, 12, 20, 25, 30, 5);
        Require(timing.BeforeHandlerNanoseconds == 10 && timing.MeasuredHandlerNanoseconds == 5
            && timing.AfterHandlerNanoseconds == 5 && timing.QueueCallNanoseconds == 2,
            "Sequential native phases must preserve independent elapsed durations.");
    }

    private static void HandlerCanFinishBeforeQueueReturns()
    {
        var timing = SignalPhaseTiming.Parse(10, 30, 12, 15, 40, 3);
        Require(timing.BeforeHandlerNanoseconds == 2 && timing.MeasuredHandlerNanoseconds == 3
            && timing.AfterHandlerNanoseconds == 25 && timing.QueueCallNanoseconds == 20,
            "Signal handling can overlap the controller's queue syscall.");
    }

    private static void EqualBoundariesAreValid()
    {
        var timing = SignalPhaseTiming.Parse(10, 10, 10, 10, 10, 0);
        Require(timing.BeforeHandlerNanoseconds == 0 && timing.MeasuredHandlerNanoseconds == 0
            && timing.AfterHandlerNanoseconds == 0 && timing.QueueCallNanoseconds == 0,
            "Equal clock observations must retain zero elapsed durations.");
    }

    private static void LargeClockValuesKeepSmallDurations()
    {
        var timing = SignalPhaseTiming.Parse(ulong.MaxValue - 10, ulong.MaxValue - 9, ulong.MaxValue - 8,
            ulong.MaxValue - 5, ulong.MaxValue, 3);
        Require(timing.BeforeHandlerNanoseconds == 2 && timing.MeasuredHandlerNanoseconds == 3
            && timing.AfterHandlerNanoseconds == 5 && timing.QueueCallNanoseconds == 1,
            "Large absolute timestamps must not overflow elapsed durations.");
    }

    private static void RejectMalformedTimings()
    {
        (ulong Request, ulong Queue, ulong Start, ulong End, ulong Observed, ulong Measured)[] cases =
        [
            (0, 12, 20, 25, 30, 5), (10, 9, 20, 25, 30, 5), (10, 12, 9, 25, 30, 16),
            (10, 12, 20, 19, 30, 0), (10, 12, 20, 25, 24, 5), (10, 31, 20, 25, 30, 5),
            (10, 12, 20, 25, 30, 4)
        ];
        foreach (var value in cases)
        {
            bool rejected = false;
            try { SignalPhaseTiming.Parse(value.Request, value.Queue, value.Start, value.End, value.Observed, value.Measured); }
            catch (InvalidDataException) { rejected = true; }
            Require(rejected, $"Malformed native phase timing was accepted: {value}.");
        }
    }
}
