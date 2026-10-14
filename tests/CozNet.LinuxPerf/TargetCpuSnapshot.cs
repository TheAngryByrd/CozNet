internal sealed class TargetCpuSnapshot
{
    public ulong BeforeNanoseconds { get; }
    public ulong CpuNanoseconds { get; }
    public ulong AfterNanoseconds { get; }
    public ulong ResolutionNanoseconds { get; }
    public ulong Registration { get; }

    private TargetCpuSnapshot(ulong before, ulong cpu, ulong after, ulong resolution, ulong registration) =>
        (BeforeNanoseconds, CpuNanoseconds, AfterNanoseconds, ResolutionNanoseconds, Registration) =
        (before, cpu, after, resolution, registration);

    public static TargetCpuSnapshot Parse(ulong before, ulong cpu, ulong after, ulong resolution, ulong registration)
    {
        if (before == 0 || after < before || resolution == 0 || registration == 0)
            throw new InvalidDataException("The target CPU clock returned an invalid observation.");
        return new(before, cpu, after, resolution, registration);
    }
}

interface ITargetCpuObservationBackend
{
    TargetCpuSnapshot ReadTargetCpu(int slot);
}
