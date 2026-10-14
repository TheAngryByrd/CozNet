static class SignalWaitModeChecks
{
    public static void Run()
    {
        if (SignalWaitMode.Parse("sleep").NativeValue != 0 || SignalWaitMode.Parse("spin").NativeValue != 1)
            throw new InvalidOperationException("Wait modes must match the native installation contract.");
        foreach (var invalid in new[] { "", "Spin", " spin", "0", "sleep,spin" })
        {
            bool rejected = false;
            try { SignalWaitMode.Parse(invalid); }
            catch (ArgumentException) { rejected = true; }
            if (!rejected) throw new InvalidOperationException($"The wait parser accepted invalid input: {invalid}.");
        }
    }
}
