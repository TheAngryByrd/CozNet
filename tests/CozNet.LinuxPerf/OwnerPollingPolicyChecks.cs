static class OwnerPollingPolicyChecks
{
    public static void Run()
    {
        var yielding = OwnerPollingPolicy.Parse("yield");
        var bounded = OwnerPollingPolicy.Parse("poll-64");
        if (yielding != OwnerPollingPolicy.Yielding || yielding.NativeValue != 0
            || yielding.IterationsBeforeYield != 1 || yielding.Name != "yield")
            throw new InvalidOperationException("The default polling policy must preserve the native yield contract.");
        if (bounded != OwnerPollingPolicy.Bounded || bounded.NativeValue != 1
            || bounded.IterationsBeforeYield != 64 || bounded.Name != "poll-64")
            throw new InvalidOperationException("The bounded polling policy must select 64 native iterations.");
        foreach (var invalid in new[] { "", "Yield", " poll-64", "poll-64 ", "0", "poll-0", "poll-65", "yield,poll-64" })
        {
            bool rejected = false;
            try { OwnerPollingPolicy.Parse(invalid); }
            catch (ArgumentException) { rejected = true; }
            if (!rejected)
                throw new InvalidOperationException($"The polling parser accepted invalid input: {invalid}.");
        }
    }
}
