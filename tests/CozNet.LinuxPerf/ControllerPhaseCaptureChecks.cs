static class ControllerPhaseCaptureChecks
{
    private sealed class Clock : IThreadCpuClock
    {
        public long Value;
        public int Reads;
        public long ReadNanoseconds() { Reads++; return Value; }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    public static void Run()
    {
        FailedPhaseRetainsCpuInterval();
        CaptureLimitStopsClockReads();
        RegressedCpuCannotPublishInterval();
    }
    private static void FailedPhaseRetainsCpuInterval()
    {
        var clock = new Clock { Value = 100 };
        var capture = new ControllerPhaseCapture(clock);
        capture.Begin("warmup", "prepare-peer", 2);
        clock.Value = 750;
        capture.End(false);
        capture.End();
        var entry = capture.Entries.Single();
        Require(!entry.Completed && entry.Stage == "warmup" && entry.Phase == "prepare-peer" && entry.Iteration == 2
            && entry.After.CpuNanoseconds - entry.Before.CpuNanoseconds == 650 && clock.Reads == 2
            && entry.Before.AfterTimestamp <= entry.After.BeforeTimestamp,
            "A failed phase must retain its bounded CPU interval once, without a second end reading.");
    }
    private static void CaptureLimitStopsClockReads()
    {
        var clock = new Clock();
        var capture = new ControllerPhaseCapture(clock);
        for (int index = 0; index < 130; index++)
        {
            capture.Begin("warmup", "drain", index);
            clock.Value += 10;
            capture.End();
        }
        Require(capture.Entries.Count == 128 && clock.Reads == 256 && capture.Entries[^1].Iteration == 127
            && capture.Entries.All(entry => entry.Completed && entry.After.CpuNanoseconds - entry.Before.CpuNanoseconds == 10),
            "The capture must retain exactly the bounded prefix and stop reading the clock after capacity.");
    }
    private static void RegressedCpuCannotPublishInterval()
    {
        var clock = new Clock { Value = 100 };
        var capture = new ControllerPhaseCapture(clock);
        capture.Begin("warmup", "drain", 1);
        clock.Value = 99;
        bool rejected = false;
        try { capture.End(); } catch (InvalidDataException) { rejected = true; }
        Require(rejected && capture.Entries.Count == 0, "A regressed CPU clock must not publish a valid phase interval.");
    }
}
