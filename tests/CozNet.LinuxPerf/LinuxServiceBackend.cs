using System.Runtime.InteropServices;

sealed class LinuxServiceBackend : IServiceInterventionBackend, IServiceSubmissionDiagnostics, IServiceOwnerSchedulerBackend
{
    private readonly NativeServiceClient client;
    private readonly ulong start, size;
    private readonly SignalWaitMode waitMode;
    private int? ownerThreadId;
    private readonly OwnerPollingPolicy polling;
    public string Name => "linux-x64-native-service";
    public ServiceSubmissionObservation? LastSubmission => client.LastSubmission;

    public LinuxServiceBackend(SignalWaitMode waitMode, bool captureSubmission = false, OwnerPollingPolicy? polling = null)
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("The native service backend requires Linux x64.");
        this.waitMode = waitMode;
        this.polling = polling ?? OwnerPollingPolicy.Yielding;
        (start, size) = PerfMapSnapshot.Read($"/tmp/perf-{Environment.ProcessId}.map").UniqueRange(OverheadWorkload.SelectedSymbol);
        client = new(start, size, waitMode, captureSubmission: captureSubmission, polling: this.polling);
    }
    public object Describe() => new { nativeStart = start, nativeSize = size, symbol = OverheadWorkload.SelectedSymbol,
        timingClock = "linux-clock-monotonic", timingUnit = "nanoseconds", waitMode = waitMode.Name,
        selectedChildrenPerCommand = 1, peerBudgetBoundNanoseconds = 2000000, nativeOwnerThreadId = ownerThreadId,
        ownerPolling = polling.Name, ownerIterationsBeforeYield = polling.IterationsBeforeYield };
    public bool IsEligibleAddress(ulong address) => client.IsEligibleAddress(address);
    public ISchedulerEventSession CaptureOwnerScheduling()
    {
        ownerThreadId = client.OwnerThreadId();
        return new LinuxSchedulerEventSession(ownerThreadId);
    }
    public bool IsValidOutcome(ulong outcome, long result) => client.IsValidOutcome(outcome, result);
    public void RegisterCurrentThread(int slot) => client.RegisterCurrentThread(slot);
    public void RetireCurrentThread(int slot) => client.RetireCurrentThread(slot);
    public void Submit(int slot, Guid identity, ulong command, ulong budget) => client.Submit(slot, identity, command, budget);
    public ServiceNativeObservation Read(int slot, Guid identity) => client.Read(slot, identity);
    public void Release(int slot, Guid identity) => client.Release(slot, identity);
    public void RestoreAfterThreadJoin() => client.CloseAfterThreadJoin();
}
