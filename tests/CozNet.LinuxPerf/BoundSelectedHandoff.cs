enum SelectedHandoffResult { Attempted, Unavailable }

interface ISelectedHandoffBackend
{
    SelectedHandoffResult TryHandoff();
}

sealed class ThreadYieldHandoffBackend : ISelectedHandoffBackend
{
    public SelectedHandoffResult TryHandoff()
    {
        try
        {
            Thread.Yield();
            return SelectedHandoffResult.Attempted;
        }
        catch (PlatformNotSupportedException) { return SelectedHandoffResult.Unavailable; }
    }
}

sealed class BoundSelectedHandoff
{
    public bool Enabled { get; }
    public long Attempts { get; private set; }
    private readonly ISelectedHandoffBackend backend;

    private BoundSelectedHandoff(bool enabled, ISelectedHandoffBackend backend)
        => (Enabled, this.backend) = (enabled, backend);

    public static BoundSelectedHandoff Parse(string? value, ISelectedHandoffBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        return value switch
        {
            null or "0" => new(false, backend),
            "1" => new(true, backend),
            _ => throw new ArgumentException("COZNET_SELECTED_HANDOFF must be 0 or 1.")
        };
    }

    public bool TryHandoff(long peerDebt, bool pending)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(peerDebt);
        if (!Enabled || !pending || peerDebt < 500000) return false;
        Attempts = checked(Attempts + 1);
        return backend.TryHandoff() switch
        {
            SelectedHandoffResult.Attempted => true,
            SelectedHandoffResult.Unavailable => throw new PlatformNotSupportedException("Selected scheduling handoff is unavailable."),
            _ => throw new InvalidDataException("The selected scheduling backend returned an unknown result.")
        };
    }
}
