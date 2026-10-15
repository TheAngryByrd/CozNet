sealed class CooperativeCleanupCancellation
{
    private readonly Func<uint, bool> hasPending;
    private readonly Func<uint, ulong> visibility;
    private readonly Action<uint> cancel;
    private readonly bool[] cancelled;
    private int started;

    public bool HasStarted => Volatile.Read(ref started) != 0;

    public CooperativeCleanupCancellation(uint targets, Func<uint, bool> hasPending,
        Func<uint, ulong> visibility, Action<uint> cancel)
    {
        if (targets is 0 or > 64) throw new ArgumentOutOfRangeException(nameof(targets));
        ArgumentNullException.ThrowIfNull(hasPending);
        ArgumentNullException.ThrowIfNull(visibility);
        ArgumentNullException.ThrowIfNull(cancel);
        this.hasPending = hasPending;
        this.visibility = visibility;
        this.cancel = cancel;
        cancelled = new bool[targets];
    }

    public void Begin()
    {
        Interlocked.Exchange(ref started, 1);
        List<Exception>? failures = null;
        for (uint slot = 0; slot < cancelled.Length; ++slot)
        {
            try
            {
                if (hasPending(slot) && visibility(slot) < 3)
                {
                    cancel(slot);
                    cancelled[slot] = true;
                }
            }
            catch (Exception error) { (failures ??= []).Add(error); }
        }
        if (failures is not null)
            throw new AggregateException("Cooperative cleanup could not cancel every pending slot.", failures);
    }

    public void RequireCancelled(uint slot)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, (uint)cancelled.Length);
        if (!HasStarted || !cancelled[slot])
            throw new InvalidOperationException("Cleanup polling requires controller cancellation.");
    }
}
