static class BoundReceiptReadinessChecks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Run()
    {
        SelectionPreservesSlotAndResult();
        FailurePreservesExceptionAndAttemptCount();
        InvalidModesReject();
    }

    private static void SelectionPreservesSlotAndResult()
    {
        foreach (string? mode in new string?[] { null, "0", "1" })
        {
            var readiness = BoundReceiptReadiness.Parse(mode);
            int direct = 0, controller = 0;
            foreach (uint slot in new uint[] { 0, 1 })
                foreach (bool ready in new[] { false, true })
                {
                    uint? observed = null;
                    bool result = readiness.Ready(slot,
                        value => { direct++; observed = value; return ready; },
                        value => { controller++; observed = value; return ready; });
                    Require(observed == slot && result == ready, "Readiness changed the slot or result.");
                }
            Require(readiness.DirectEnabled == (mode == "1")
                && direct == (mode == "1" ? 4 : 0) && controller == (mode == "1" ? 0 : 4)
                && readiness.Calls == direct, "Readiness selected the wrong path or count.");
        }
    }

    private static void FailurePreservesExceptionAndAttemptCount()
    {
        foreach (string mode in new[] { "0", "1" })
        {
            var readiness = BoundReceiptReadiness.Parse(mode);
            var original = new InvalidDataException("receipt query failed");
            Exception? observed = null;
            int direct = 0, controller = 0;
            try
            {
                readiness.Ready(1, _ => { direct++; throw original; }, _ => { controller++; throw original; });
            }
            catch (Exception error) { observed = error; }
            Require(ReferenceEquals(original, observed) && direct == (mode == "1" ? 1 : 0)
                && controller == (mode == "1" ? 0 : 1) && readiness.Calls == direct,
                "A failed query changed its exception, count, or selected path.");
        }
    }

    private static void InvalidModesReject()
    {
        foreach (string value in new[] { "", "true", "2", " 1", "1 " })
        {
            bool rejected = false;
            try { BoundReceiptReadiness.Parse(value); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected, "Readiness accepted an invalid mode.");
        }
    }
}
