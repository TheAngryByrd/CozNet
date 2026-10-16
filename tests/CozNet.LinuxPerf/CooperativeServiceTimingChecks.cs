static class CooperativeServiceTimingChecks
{
    private static void Require(bool value, string message) => CooperativeManagedChecks.Require(value, message);

    public static void Run()
    {
        Require(CooperativeServiceTiming.Parse(null, false) is null && CooperativeServiceTiming.Parse("0", false) is null,
            "Disabled service timing created an observer.");
        foreach (var input in new[] { ("1", false), ("", true), ("true", true), (" 1", true), ("2", true) })
        {
            bool rejected = false;
            try { CooperativeServiceTiming.Parse(input.Item1, input.Item2); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected, "Invalid service timing settings were accepted.");
        }
        var timing = CooperativeServiceTiming.Parse("1", true)!;
        var empty = timing.All;
        timing.Record(CooperativeServiceActivity.Pending, 10, 30, false, true);
        timing.Record(CooperativeServiceActivity.Pending, 40, 45, true, false);
        timing.Record(CooperativeServiceActivity.ReceiptAndSubmit, 50, 90, true, true);
        Require(timing.All[2] == new CooperativeServiceTimingBucket(CooperativeServiceActivity.Pending, 2, 1, 25, 20)
            && timing.Measurement[2] == new CooperativeServiceTimingBucket(CooperativeServiceActivity.Pending, 1, 0, 20, 20)
            && timing.All[5] == new CooperativeServiceTimingBucket(CooperativeServiceActivity.ReceiptAndSubmit, 1, 1, 40, 40)
            && timing.Measurement[5] == timing.All[5] && empty.All(item => item.Calls == 0),
            "Service timing lost a phase, failure, measurement subset, maximum, or immutable snapshot.");
        var before = timing.All;
        foreach (var input in new[] { ((CooperativeServiceActivity)6, 1L, 2L), (CooperativeServiceActivity.Idle, -1L, 2L),
            (CooperativeServiceActivity.Idle, 2L, 1L) })
        {
            bool rejected = false;
            try { timing.Record(input.Item1, input.Item2, input.Item3, false, true); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected && before.SequenceEqual(timing.All), "Invalid timing changed retained service evidence.");
        }
        var foreign = Task.Run(() =>
        {
            try { timing.Record(CooperativeServiceActivity.Idle, 1, 2, false, true); }
            catch (InvalidOperationException) { return true; }
            return false;
        });
        Require(foreign.Wait(TimeSpan.FromSeconds(5)) && foreign.Result && before.SequenceEqual(timing.All),
            "A foreign caller changed service timing evidence.");
    }
}
