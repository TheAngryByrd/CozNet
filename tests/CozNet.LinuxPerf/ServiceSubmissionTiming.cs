using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
struct ServiceNativeSubmissionTiming
{
    public ulong Entered, ApiLocked, OwnerEntered, OwnerLocked, OwnerFinished, Returned;
}

sealed record ServiceSubmissionObservation(int Slot, Guid Identity, ulong Command, ulong Budget,
    long ManagedEntered, long ManagedReturned, int? Status, ServiceNativeSubmissionTiming Native);

interface IServiceSubmissionDiagnostics
{
    ServiceSubmissionObservation? LastSubmission { get; }
}

sealed class ServiceSubmissionTiming
{
    public ulong ApiAcquisitionNanoseconds { get; }
    public ulong OwnerAcquisitionNanoseconds { get; }
    public ulong OwnerSubmissionNanoseconds { get; }
    public ulong NativeNanoseconds { get; }
    public long ManagedTicks { get; }

    private ServiceSubmissionTiming(ServiceSubmissionObservation value)
    {
        var native = value.Native;
        ApiAcquisitionNanoseconds = native.ApiLocked - native.Entered;
        OwnerAcquisitionNanoseconds = native.OwnerLocked - native.OwnerEntered;
        OwnerSubmissionNanoseconds = native.OwnerFinished - native.OwnerLocked;
        NativeNanoseconds = native.Returned - native.Entered;
        ManagedTicks = checked(value.ManagedReturned - value.ManagedEntered);
    }

    public static ServiceSubmissionTiming Parse(ServiceSubmissionObservation value, int slot, Guid identity, ulong command, ulong budget)
    {
        if (value.Slot != slot || value.Identity != identity || value.Command != command || value.Budget != budget)
            throw new InvalidDataException("The submission timing identity does not match the command.");
        var native = value.Native;
        if (value.Status != 0 || value.ManagedEntered < 0 || value.ManagedReturned < value.ManagedEntered
            || native.Entered == 0 || native.ApiLocked < native.Entered || native.OwnerEntered < native.ApiLocked
            || native.OwnerLocked < native.OwnerEntered || native.OwnerFinished < native.OwnerLocked || native.Returned < native.OwnerFinished)
            throw new InvalidDataException("The accepted submission timing is incomplete or unordered.");
        return new(value);
    }
}
