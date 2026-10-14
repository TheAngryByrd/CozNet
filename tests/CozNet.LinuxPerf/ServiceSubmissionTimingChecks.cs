using System.Runtime.InteropServices;

static class ServiceSubmissionTimingChecks
{
    public static void Run()
    {
        var identity = Guid.NewGuid();
        var value = new ServiceSubmissionObservation(1, identity, 2, 50000, 10, 30, 0,
            new() { Entered = ulong.MaxValue - 9, ApiLocked = ulong.MaxValue - 8,
                OwnerEntered = ulong.MaxValue - 7, OwnerLocked = ulong.MaxValue - 5,
                OwnerFinished = ulong.MaxValue - 2, Returned = ulong.MaxValue });
        var parsed = ServiceSubmissionTiming.Parse(value, 1, identity, 2, 50000);
        if (Marshal.SizeOf<ServiceNativeSubmissionTiming>() != 48 || parsed.NativeNanoseconds != 9
            || parsed.ApiAcquisitionNanoseconds != 1 || parsed.OwnerAcquisitionNanoseconds != 2
            || parsed.OwnerSubmissionNanoseconds != 3 || parsed.ManagedTicks != 20)
            throw new InvalidOperationException("Submission timing must preserve layout and exact intervals at large timestamps.");
        foreach (var invalid in new[] { value with { Status = null }, value with { Status = 22 },
            value with { Slot = 0 }, value with { Identity = Guid.Empty }, value with { Command = 3 },
            value with { Budget = 1 }, value with { ManagedEntered = -1 }, value with { ManagedReturned = 9 } })
            Reject(invalid, identity);
        for (int field = 0; field < 6; field++)
        {
            var native = value.Native;
            switch (field)
            {
                case 0: native.Entered = 0; break;
                case 1: native.ApiLocked = native.Entered - 1; break;
                case 2: native.OwnerEntered = native.ApiLocked - 1; break;
                case 3: native.OwnerLocked = native.OwnerEntered - 1; break;
                case 4: native.OwnerFinished = native.OwnerLocked - 1; break;
                case 5: native.Returned = native.OwnerFinished - 1; break;
            }
            Reject(value with { Native = native }, identity);
        }
    }

    private static void Reject(ServiceSubmissionObservation value, Guid identity)
    {
        try { ServiceSubmissionTiming.Parse(value, 1, identity, 2, 50000); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid submission timing must not produce parsed intervals.");
    }
}
