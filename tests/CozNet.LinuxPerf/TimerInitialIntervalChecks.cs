static class TimerInitialIntervalChecks
{
    public static void Run()
    {
        if (TimerInitialInterval.Default.Nanoseconds != 1000000 || TimerInitialInterval.Parse("1ms").Nanoseconds != 1000000
            || TimerInitialInterval.Parse("10us").Nanoseconds != 10000)
            throw new InvalidOperationException("Initial timer interval parsing changed the supported values or default.");
        foreach (var text in new[] { "", "0", "10000", "1us", "11us", "1MS", " 10us", "1000001" })
        {
            bool rejected = false;
            try { TimerInitialInterval.Parse(text); } catch (ArgumentException) { rejected = true; }
            if (!rejected) throw new InvalidOperationException($"Unsupported initial timer interval '{text}' was accepted.");
        }
    }
}
