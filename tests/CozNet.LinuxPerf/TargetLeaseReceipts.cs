readonly record struct TargetLeaseRawChild(ulong Ordinal, ulong Requested, ulong Armed, ulong Started,
    ulong Finished, ulong Waited, ulong InstructionPointer, int WaitStatus, bool Valid);

readonly record struct TargetLeaseRawHeader(ulong Generation, ulong Command, ulong Budget, ulong Children,
    ulong Measured, int Visibility, int Stop, int Error, int TransportError, bool MayHaveDelivery, ulong Captured);

sealed record TargetLeaseRawCapture(TargetLeaseRawHeader Header, IReadOnlyList<TargetLeaseRawChild> Children,
    ulong ManagedReceiptUpper, int ClockStatus);

sealed record TargetLeaseReceivedChild(TargetLeaseRawChild Raw, ulong FirstCaptured, ulong FirstManagedReceiptUpper,
    ServiceReceiptTiming Timing);

sealed class TargetLeaseReceipts
{
    private readonly ulong generation, command, budget;
    private readonly List<TargetLeaseReceivedChild> received = [];
    private IReadOnlyList<TargetLeaseReceivedChild> published = Array.Empty<TargetLeaseReceivedChild>();
    private TargetLeaseRawHeader? previous;
    private TargetLeaseRawHeader? terminal;
    private ulong previousReceiptUpper;
    public TargetLeaseRawCapture? LastRaw { get; private set; }
    public TargetLeaseRawCapture? FirstFailure { get; private set; }

    private TargetLeaseReceipts(ulong generation, ulong command, ulong budget)
        => (this.generation, this.command, this.budget) = (generation, command, budget);

    public static TargetLeaseReceipts Create(ulong generation, ulong command, ulong budget)
    {
        if (generation == 0 || command == 0 || budget is 0 or > 2000000)
            throw new ArgumentException("A target lease needs a nonzero identity and a budget from 1 through 2000000 ns.");
        return new(generation, command, budget);
    }

    public IReadOnlyList<TargetLeaseReceivedChild> Observe(TargetLeaseRawHeader header,
        ReadOnlySpan<TargetLeaseRawChild> children, ulong receiptUpper, int clockStatus)
    {
        LastRaw = new(header, Array.AsReadOnly(children.ToArray()), receiptUpper, clockStatus);
        if (FirstFailure is not null)
            throw new InvalidDataException("A failed target lease receipt cannot be repaired by a later read.");
        try
        {
            if (header.Generation != generation || header.Command != command || header.Budget != budget
                || header.Children != (ulong)children.Length || children.Length > 100000 || children.Length < received.Count
                || header.Captured == 0 || clockStatus != 0 || receiptUpper < header.Captured
                || header.Visibility is < 0 or > 3 || header.Stop is < 0 or > 5 || header.Error is < 0 or > 6)
                throw new InvalidDataException("The target lease snapshot identity, count, visibility, or clock is invalid.");
            if (previous is { } earlier && (header.Captured < earlier.Captured || header.Visibility < earlier.Visibility
                || receiptUpper < previousReceiptUpper))
                throw new InvalidDataException("Target lease capture time and visibility cannot move backwards.");
            if ((header.Visibility == 0 && (header.Stop != 0 || header.Error != 0 || header.TransportError != 0 || !header.MayHaveDelivery))
                || (header.Visibility != 0 && header.Stop == 0) || (header.Visibility >= 2 && header.MayHaveDelivery))
                throw new InvalidDataException("The target lease visibility contradicts its terminal state.");
            if (terminal is { } sealedHeader && (header.Children != sealedHeader.Children || header.Measured != sealedHeader.Measured
                || header.Stop != sealedHeader.Stop || header.Error != sealedHeader.Error || header.TransportError != sealedHeader.TransportError
                || (header.Visibility == 1 && header.MayHaveDelivery != sealedHeader.MayHaveDelivery)))
                throw new InvalidDataException("A terminal target lease journal cannot change.");
            for (int index = 0; index < received.Count; index++)
                if (received[index].Raw != children[index])
                    throw new InvalidDataException("A published target lease child cannot change.");

            var additions = new List<TargetLeaseReceivedChild>();
            for (int index = received.Count; index < children.Length; index++)
            {
                var child = children[index];
                if (child.Ordinal != (ulong)index + 1)
                    throw new InvalidDataException("Target lease child ordinals must form a contiguous prefix.");
                var timing = ServiceReceiptTiming.Parse(child.Armed, child.Finished, header.Captured, receiptUpper, clockStatus);
                additions.Add(new(child, header.Captured, receiptUpper, timing));
            }
            received.AddRange(additions);
            if (additions.Count != 0) published = Array.AsReadOnly(received.ToArray());
            previous = header;
            previousReceiptUpper = receiptUpper;
            if (header.Visibility != 0) terminal ??= header;
            return published;
        }
        catch (InvalidDataException)
        {
            FirstFailure = LastRaw;
            throw;
        }
    }
}
