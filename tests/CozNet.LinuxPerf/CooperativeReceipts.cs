using System.Runtime.InteropServices;

sealed record CooperativeSite
{
    public ulong Worker { get; }
    public ulong CodeVersion { get; }
    public ulong Site { get; }
    private CooperativeSite(ulong worker, ulong version, ulong site) => (Worker, CodeVersion, Site) = (worker, version, site);
    public static CooperativeSite Parse(ulong worker, ulong version, ulong site) =>
        worker != 0 && version != 0 && site != 0 ? new(worker, version, site)
            : throw new InvalidDataException("A cooperative site requires nonzero worker, code-version, and site identities.");
}

sealed record CooperativeLeaseIdentity
{
    public ulong Generation { get; }
    public ulong Command { get; }
    public ulong Budget { get; }
    private CooperativeLeaseIdentity(ulong generation, ulong command, ulong budget) => (Generation, Command, Budget) = (generation, command, budget);
    public static CooperativeLeaseIdentity Parse(ulong generation, ulong command, ulong budget) =>
        generation is > 0 and <= uint.MaxValue && command != 0 && budget is > 0 and <= 2000000
            ? new(generation, command, budget) : throw new InvalidDataException("The cooperative lease identity or budget is invalid.");
}

[StructLayout(LayoutKind.Sequential)]
record struct CooperativeNativeChild
{
    public ulong Ordinal, Requested, Armed, Started, Finished, Waited, Worker, CodeVersion, Site, WaitStatus, Valid;
}
[StructLayout(LayoutKind.Sequential)]
record struct CooperativeNativeSnapshot
{
    public ulong Generation, Command, Budget, Children, Measured, Visibility, Stop, Error;
    public long TransportError;
    public ulong MayHaveDelivery, Captured;
}
readonly record struct CooperativeClockRead(int Status, ulong Nanoseconds);
sealed record CooperativeRawCapture(CooperativeNativeSnapshot Header, IReadOnlyList<CooperativeNativeChild> Children, CooperativeClockRead Clock, int Status, CooperativeCpuCapture? Cpu = null);
sealed record CooperativeReceivedChild(CooperativeNativeChild Raw, ulong FirstCaptured, ulong FirstManagedReceiptUpper, ServiceReceiptTiming Timing);
sealed record CooperativeObservation(CooperativeRawCapture Raw, IReadOnlyList<CooperativeReceivedChild> Received);

sealed class CooperativeReceipts(CooperativeLeaseIdentity identity)
{
    private readonly CooperativeLeaseIdentity identity = identity ?? throw new ArgumentNullException(nameof(identity));
    private IReadOnlyList<CooperativeReceivedChild> received = Array.Empty<CooperativeReceivedChild>();
    private CooperativeNativeSnapshot? previous, terminal;
    private ulong previousUpper;
    public CooperativeRawCapture? LastRaw { get; private set; }
    public CooperativeRawCapture? FirstFailure { get; private set; }

    public CooperativeObservation Observe(CooperativeNativeSnapshot header, ReadOnlySpan<CooperativeNativeChild> children,
        CooperativeClockRead clock, int status = 0, CooperativeCpuCapture? cpu = null)
    {
        LastRaw = new(header, Array.AsReadOnly(children.ToArray()), clock, status, cpu);
        if (FirstFailure is not null) throw new InvalidDataException("A failed cooperative receipt cannot be repaired by a later read.");
        try
        {
            Require(status == 0 && clock.Status == 0 && header.Captured != 0 && clock.Nanoseconds >= header.Captured,
                "The cooperative snapshot or receipt clock failed.");
            Require(header.Generation == identity.Generation && header.Command == identity.Command && header.Budget == identity.Budget
                && header.Children == (ulong)children.Length && children.Length <= 100000 && children.Length >= received.Count,
                "The cooperative receipt identity or child count is invalid.");
            Require(header.Visibility <= 3 && header.Stop <= 5 && header.Error <= 6 && header.MayHaveDelivery <= 1,
                "The cooperative snapshot has an invalid discriminator.");
            Require(header.Visibility == 0 ? header.Stop == 0 && header.Error == 0 && header.TransportError == 0 && header.MayHaveDelivery == 1 : header.Stop != 0,
                "The cooperative visibility contradicts its terminal state.");
            Require(header.Visibility < 2 || header.MayHaveDelivery == 0, "A quiescent cooperative journal reports possible delivery.");
            if (previous is { } prior)
                Require(header.Captured >= prior.Captured && header.Visibility >= prior.Visibility && clock.Nanoseconds >= previousUpper,
                    "Cooperative receipt time or visibility moved backwards.");
            if (terminal is { } sealedHeader)
                Require(header.Children == sealedHeader.Children && header.Measured == sealedHeader.Measured && header.Stop == sealedHeader.Stop
                    && header.Error == sealedHeader.Error && header.TransportError == sealedHeader.TransportError
                    && (header.Visibility != 1 || header.MayHaveDelivery == sealedHeader.MayHaveDelivery), "A terminal cooperative journal changed.");
            var next = new List<CooperativeReceivedChild>(received);
            for (int index = 0; index < children.Length; ++index)
            {
                var child = children[index];
                Require(child.Ordinal == (ulong)index + 1 && child.WaitStatus <= 2 && child.Valid <= 1,
                    "The cooperative child has an invalid ordinal or discriminator.");
                _ = CooperativeSite.Parse(child.Worker, child.CodeVersion, child.Site);
                if (index < received.Count)
                    Require(received[index].Raw == child, "A published cooperative child changed.");
                else
                    next.Add(new(child, header.Captured, clock.Nanoseconds,
                        ServiceReceiptTiming.Parse(child.Armed, child.Finished, header.Captured, clock.Nanoseconds, clock.Status)));
            }
            if (next.Count != received.Count) received = Array.AsReadOnly(next.ToArray());
            previous = header;
            previousUpper = clock.Nanoseconds;
            if (header.Visibility != 0) terminal ??= header;
            return new(LastRaw, received);
        }
        catch (InvalidDataException) { FirstFailure = LastRaw; throw; }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
