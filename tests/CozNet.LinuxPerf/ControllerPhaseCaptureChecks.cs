static class ControllerPhaseCaptureChecks
{
    private sealed class Clock : IThreadCpuClock
    {
        public long Value;
        public int Reads;
        public long ReadNanoseconds() { Reads++; return Value; }
    }
    public static void Run()
    {
        FailedPhaseRetainsCpuInterval();
        CaptureLimitStopsClockReads();
        RegressedCpuCannotPublishInterval();
        PrefixStopsAfterCapacity();
        RecentPhasesRemainOrderedAcrossWraps();
        DiscardedDrainsDoNotEvictButFailuresDo();
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
    private static void PrefixStopsAfterCapacity()
    {
        var clock = new Clock();
        var capture = new ControllerPhaseCapture(clock);
        for (int index = 0; index < 300; index++)
        {
            capture.Begin("check", "prefix", index);
            capture.End();
        }
        Require(capture.Entries.Select(entry => entry.Iteration).SequenceEqual(Enumerable.Range(0, 128))
            && capture.Overwritten == 0 && clock.Reads == 256,
            "Default capture must stop both recording and clock reads at 128 entries.");
    }
    private static void RecentPhasesRemainOrderedAcrossWraps()
    {
        var capture = new ControllerPhaseCapture(new Clock(), retainRecent: true);
        for (int index = 0; index < 385; index++)
        {
            capture.Begin("check", "recent", index);
            capture.End();
            int count = Math.Min(index + 1, 128);
            Require(capture.Entries.Select(entry => entry.Iteration).SequenceEqual(Enumerable.Range(index + 1 - count, count))
                && capture.Overwritten == Math.Max(0, index + 1 - 128),
                $"Rolling phase order or eviction count differs after iteration {index}.");
        }
    }
    private static void DiscardedDrainsDoNotEvictButFailuresDo()
    {
        var capture = new ControllerPhaseCapture(new Clock(), retainRecent: true);
        for (int index = 0; index < 128; index++)
        {
            capture.Begin("check", "retained", index);
            capture.End();
        }
        capture.Begin("check", "empty-drain", 128);
        capture.End(retain: false);
        Require(capture.Entries.Count == 128 && capture.Overwritten == 0 && capture.Entries[^1].Iteration == 127,
            "An empty drain must not evict a retained service phase.");
        capture.Begin("check", "failed-drain", 129);
        capture.End(completed: false, retain: false);
        Require(capture.Entries.Count == 128 && capture.Overwritten == 1 && capture.Entries[0].Iteration == 1
            && capture.Entries[^1].Iteration == 129 && !capture.Entries[^1].Completed,
            "A failed phase must remain available even when its successful form would be discarded.");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
