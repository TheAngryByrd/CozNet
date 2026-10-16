static class CooperativeComponentCpuChecks
{
    private sealed class Clock(params long[] readings) : IThreadCpuClock
    {
        private int index;
        public long ReadNanoseconds() => readings[index++];
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    public static void Run()
    {
        Require(CooperativeComponentCpu.Parse(null, false, null) is null
            && CooperativeComponentCpu.Parse("0", false, null) is null, "Disabled capture requested a clock.");
        Reject<ArgumentException>(() => CooperativeComponentCpu.Parse("bad", true, null));
        Reject<ArgumentException>(() => CooperativeComponentCpu.Parse("1", false, null));
        Reject<PlatformNotSupportedException>(() => CooperativeComponentCpu.Parse("1", true, null));
        Reject<InvalidOperationException>(() => CooperativeComponentCpu.Parse("1", true, () => new Clock(-1)));
        TotalsAndSnapshots();
        ServiceActivities();
        FailuresAndOwnership();
    }

    private static void TotalsAndSnapshots()
    {
        var capture = CooperativeComponentCpu.Parse("1", true, () => new Clock(5, 10, 30, 40, 47, 60, 60))!;
        capture.Begin(true);
        capture.End(CooperativeCpuActivity.Drain, false);
        var snapshot = capture.All;
        capture.Begin(false);
        capture.End(CooperativeCpuActivity.Drain, true);
        capture.Begin(true);
        capture.EndService(CooperativeServiceActivity.Pending, false);
        Require(snapshot[0] == new CooperativeCpuBucket(CooperativeCpuActivity.Drain, 1, 0, 20, 20), "A snapshot changed after later observations.");
        Require(capture.All[0] == new CooperativeCpuBucket(CooperativeCpuActivity.Drain, 2, 1, 27, 20), "Drain totals or maximum changed.");
        Require(capture.Measurement[0] == snapshot[0], "Cleanup CPU entered measurement totals.");
        Require(capture.All[3] == new CooperativeCpuBucket(CooperativeCpuActivity.Pending, 1, 0, 0, 0)
            && capture.Measurement[3] == capture.All[3], "Zero CPU progress or service attribution changed.");
        Require(capture.Failure is null && !capture.Active, "Valid completion retained failure or an open region.");
    }

    private static void ServiceActivities()
    {
        var capture = CooperativeComponentCpu.Parse("1", true, () => new Clock(0, 1, 2, 3, 5, 6, 9, 10, 14, 15, 20, 21, 27))!;
        (CooperativeServiceActivity Service, CooperativeCpuActivity Bucket)[] cases =
        [
            (CooperativeServiceActivity.Skipped, CooperativeCpuActivity.Skipped),
            (CooperativeServiceActivity.Idle, CooperativeCpuActivity.Idle),
            (CooperativeServiceActivity.Pending, CooperativeCpuActivity.Pending),
            (CooperativeServiceActivity.Receipt, CooperativeCpuActivity.Receipt),
            (CooperativeServiceActivity.Submit, CooperativeCpuActivity.Submit),
            (CooperativeServiceActivity.ReceiptAndSubmit, CooperativeCpuActivity.ReceiptAndSubmit)
        ];
        for (int index = 0; index < cases.Length; ++index)
        {
            capture.Begin(true);
            capture.EndService(cases[index].Service, false);
            Require(capture.All[(int)cases[index].Bucket] == new CooperativeCpuBucket(cases[index].Bucket, 1, 0, index + 1, index + 1),
                "Service activity entered the wrong bucket or changed its CPU duration.");
        }
        Require(capture.All[0].Calls == 0, "Service activity entered the drain bucket.");
    }

    private static void FailuresAndOwnership()
    {
        var capture = CooperativeComponentCpu.Parse("1", true, () => new Clock(1, 2, 1))!;
        Task.Run(() => Reject<InvalidOperationException>(() => capture.Begin(true))).GetAwaiter().GetResult();
        Require(capture.Failure is null, "A foreign thread changed capture state.");
        capture.Begin(true);
        Reject<InvalidOperationException>(() => capture.End(CooperativeCpuActivity.Drain, false));
        Require(capture.Failure is not null && capture.Active && capture.All[0].Calls == 0, "Clock regression did not retain incomplete evidence.");
        Reject<InvalidOperationException>(() => capture.Begin(true));
        var nested = CooperativeComponentCpu.Parse("1", true, () => new Clock(0, 1))!;
        nested.Begin(false);
        Reject<InvalidOperationException>(() => nested.Begin(false));
        Require(nested.Failure is not null, "Nested regions did not fail capture.");
        var missing = CooperativeComponentCpu.Parse("1", true, () => new Clock(0))!;
        Reject<InvalidOperationException>(() => missing.End(CooperativeCpuActivity.Idle, false));
        Require(missing.Failure is not null, "Missing start did not fail capture.");
    }
}
