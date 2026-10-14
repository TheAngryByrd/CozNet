static class TimerDebtLimit
{
    public const long Nanoseconds = 2_000_000;
    public static bool Allows(long selected, long peer) =>
        selected is >= 0 and <= Nanoseconds && peer is >= 0 and <= Nanoseconds;
}
