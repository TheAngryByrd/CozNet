using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Runtime.CompilerServices;

if (args is ["--verify-pausable-managed"])
{
    PausableCollectorChecks.Run();
    BoundCpuSessionChecks.Run();
    PausableBoundCollectionChecks.Run();
    return 0;
}

if (args is ["--verify-cooperative-managed"])
{
    CooperativeProgressCaptureChecks.Run();
    CooperativeCleanupCancellationChecks.Run();
    CooperativeReceiptIntervalChecks.Run();
    CooperativeDebtChecks.Run();
    CooperativeFailureCaptureChecks.Run();
    CooperativeManagedChecks.Run();
    return 0;
}
if (args is ["--verify-bound-cooperative-managed"])
{
    SchedulerCaptureGroupChecks.Run();
    BoundSchedulerCaptureChecks.Run();
    BoundSamplePublicationChecks.Run();
    BoundReceiptReadinessChecks.Run();
    CooperativeDispatchTimingChecks.Run();
    BoundSelectedCadenceChecks.Run();
    CooperativeProgressCaptureChecks.Run();
    CooperativeCleanupCancellationChecks.Run();
    CooperativeReceiptIntervalChecks.Run();
    CooperativeDebtChecks.Run();
    CooperativeFailureCaptureChecks.Run();
    CooperativeManagedChecks.Run();
    ClrProbeLifetimeChecks.Run();
    ClrBoundInstructionMapChecks.Run();
    return 0;
}
if (args is ["--verify-bound-map-managed"])
{
    ClrBoundInstructionMapChecks.Run();
    return 0;
}
if (args is ["--verify-bound-samples-managed"])
{
    ClrBoundInstructionMapChecks.Run();
    BoundSampleCollectionChecks.Run();
    return 0;
}
if (args is ["--verify-bound-samples", var boundSamplesPath, var boundSamplesMode])
    return BoundSamplesWorkload.Run(boundSamplesPath, boundSamplesMode);
if (args is ["--sampled-bound-selected-cadence", var cadencePath, var cadencePercent, var cadenceLibrary, var cadenceMode, var cadenceBatches])
{
    var cadencePlan = BoundSelectedCadencePlan.Parse(cadencePercent, cadenceMode, cadenceBatches);
    return BoundSampledPaymentWorkload.Run(cadencePath, cadencePlan.Sampling, cadenceLibrary,
        () => CpuSampleOwner.Parse((uint)Environment.ProcessId, checked((uint)Native.GetTid(186))),
        (map, sink) => new LinuxCpuEventSession(sink, map),
        selectedPaymentSource: (map, sink) => new LinuxCpuEventSession(sink, map), cadencePlan: cadencePlan, schedulerBackend: LinuxBoundSchedulerBackend.FromEnvironment());
}
if (args is ["--invalidate-sampled-bound-selected-cadence", var cadenceInvalidPath, var cadenceInvalidLibrary, var cadenceInvalidMode, var cadenceInvalidBatches])
{
    var cadencePlan = BoundSelectedCadencePlan.Parse("50", cadenceInvalidMode, cadenceInvalidBatches);
    return BoundSampledPaymentWorkload.Run(cadenceInvalidPath, cadencePlan.Sampling, cadenceInvalidLibrary,
        () => CpuSampleOwner.Parse((uint)Environment.ProcessId, checked((uint)Native.GetTid(186))),
        (map, sink) => new LinuxCpuEventSession(sink, map),
        selectedPaymentSource: (map, sink) => new LinuxCpuEventSession(sink, map),
        invalidateAfterSelectedPayment: true, cadencePlan: cadencePlan, schedulerBackend: LinuxBoundSchedulerBackend.FromEnvironment());
}
if (args is ["--sampled-bound-selected-cooperative", var selectedBoundPath, var selectedBoundPercent, var selectedBoundLibrary])
    return BoundSampledPaymentWorkload.Run(selectedBoundPath, SampledSignalPlan.Parse(selectedBoundPercent), selectedBoundLibrary,
        () => CpuSampleOwner.Parse((uint)Environment.ProcessId, checked((uint)Native.GetTid(186))),
        (map, sink) => new LinuxCpuEventSession(sink, map),
        selectedPaymentSource: (map, sink) => new LinuxCpuEventSession(sink, map), schedulerBackend: LinuxBoundSchedulerBackend.FromEnvironment());
if (args is ["--invalidate-sampled-bound-selected-cooperative", var selectedInvalidatedPath, var selectedInvalidatedLibrary])
    return BoundSampledPaymentWorkload.Run(selectedInvalidatedPath, SampledSignalPlan.Parse("50"), selectedInvalidatedLibrary,
        () => CpuSampleOwner.Parse((uint)Environment.ProcessId, checked((uint)Native.GetTid(186))),
        (map, sink) => new LinuxCpuEventSession(sink, map),
        selectedPaymentSource: (map, sink) => new LinuxCpuEventSession(sink, map), invalidateAfterSelectedPayment: true, schedulerBackend: LinuxBoundSchedulerBackend.FromEnvironment());
if (args is ["--sampled-bound-cooperative", var sampledBoundPath, var sampledBoundPercent, var sampledBoundLibrary])
    return BoundSampledPaymentWorkload.Run(sampledBoundPath, SampledSignalPlan.Parse(sampledBoundPercent), sampledBoundLibrary,
        () => CpuSampleOwner.Parse((uint)Environment.ProcessId, checked((uint)Native.GetTid(186))),
        (map, sink) => new LinuxCpuEventSession(sink, map), schedulerBackend: LinuxBoundSchedulerBackend.FromEnvironment());
if (args is ["--invalidate-sampled-bound-cooperative", var invalidatedBoundPath, var invalidatedBoundLibrary])
    return BoundSampledPaymentWorkload.Run(invalidatedBoundPath, SampledSignalPlan.Parse("50"), invalidatedBoundLibrary,
        () => CpuSampleOwner.Parse((uint)Environment.ProcessId, checked((uint)Native.GetTid(186))),
        (map, sink) => new LinuxCpuEventSession(sink, map), invalidateAfterPayment: true, schedulerBackend: LinuxBoundSchedulerBackend.FromEnvironment());
if (args is ["--verify-bound-cooperative", var boundPath, var boundMode, var boundLibrary])
    return BoundCooperativeChecks.Run(boundPath, boundMode, boundLibrary);
if (args is ["--verify-cooperative-interop", var cooperativePath, var cooperativeMode, var cooperativeLibrary])
    return CooperativeInteropChecks.Run(cooperativePath, cooperativeMode, cooperativeLibrary);
if (args is ["--verify-probe-identity", var identityPath, var identityMode])
    return ProbeIdentityChecks.Run(identityPath, identityMode);

if (args is ["--verify-cpu-counters"])
{
    PerfAttributeChecks.TaskClockUsesMonotonicAbi();
    RingReaderChecks.Run();
    CpuCounterEvidenceChecks.Run();
    return 0;
}

if (args is ["--verify-cpu-event-clock"])
{
    PerfAttributeChecks.TaskClockUsesMonotonicAbi();
    SampleClockEvidenceChecks.Run();
    return 0;
}

if (args is ["--calibrate-physical-interleaved", var interleavedCalibrationPath])
    return InterleavedCalibration.Run(interleavedCalibrationPath, () => new LinuxThreadCpuClock());

if (args is ["--verify-interleaved-calibration"])
{
    InterleavedCalibrationChecks.Run();
    return 0;
}

if (args is ["--calibrate-physical", var calibrationPath])
    return PhysicalCalibration.Run(calibrationPath, () => new LinuxThreadCpuClock());

if (args is ["--verify-physical-calibration"])
{
    PhysicalCalibrationChecks.Run();
    return 0;
}

if (args is ["--timer-physical", var timerPhysicalPath, var timerPhysicalMode])
    return MatchedPhysicalWorkload.Run(timerPhysicalPath, PhysicalWorkMode.Parse(timerPhysicalMode));

if (args is ["--verify-matched-workers"])
{
    MatchedCpuWorkersChecks.Run();
    return 0;
}

if (args is ["--sampled-timer", var sampledTimerPath, var sampledTimerPercent])
    return SampledTimerWorkload.Run(sampledTimerPath, SampledSignalPlan.Parse(sampledTimerPercent).Percent,
        sink => new LinuxCpuEventSession(sink));

if (args is ["--sampled-timer-phases", var sampledTimerPhasesPath, var sampledTimerPhasesPercent])
    return SampledTimerWorkload.Run(sampledTimerPhasesPath, SampledSignalPlan.Parse(sampledTimerPhasesPercent).Percent,
        sink => new LinuxCpuEventSession(sink), new LinuxThreadCpuClock());

if (args is ["--sampled-timer-initialized", var sampledTimerInitializedPath, var sampledTimerInitializedPercent])
    return SampledTimerWorkload.Run(sampledTimerInitializedPath, SampledSignalPlan.Parse(sampledTimerInitializedPercent).Percent,
        sink => new LinuxCpuEventSession(sink), new LinuxThreadCpuClock(), initializeManagedPaths: true);

if (args is ["--sampled-timer-prepared", var sampledTimerPreparedPath, var sampledTimerPreparedPercent])
    return SampledTimerWorkload.Run(sampledTimerPreparedPath, SampledSignalPlan.Parse(sampledTimerPreparedPercent).Percent,
        sink => new LinuxCpuEventSession(sink), new LinuxThreadCpuClock(), initializeManagedPaths: true, initializeNativeTransport: true);

if (args is ["--sampled-timer-prepared", var sampledTimerFastPath, var sampledTimerFastPercent, var timerInitialInterval])
    return SampledTimerWorkload.Run(sampledTimerFastPath, SampledSignalPlan.Parse(sampledTimerFastPercent).Percent,
        sink => new LinuxCpuEventSession(sink), new LinuxThreadCpuClock(), initializeManagedPaths: true,
        initializeNativeTransport: true, initialInterval: TimerInitialInterval.Parse(timerInitialInterval));

if (args is ["--sampled-timer-capacity", var sampledTimerCapacityPath, var sampledTimerCapacityPercent, var capacityInitialInterval])
    return SampledTimerWorkload.Run(sampledTimerCapacityPath, SampledSignalPlan.Parse(sampledTimerCapacityPercent).Percent,
        sink => new LinuxCpuEventSession(sink), initializeManagedPaths: true,
        initializeNativeTransport: true, initialInterval: TimerInitialInterval.Parse(capacityInitialInterval));

if (args is ["--sampled-timer-capacity", var sampledTimerGcPath, var sampledTimerGcPercent, var gcInitialInterval, var sampledTimerGcMode])
    return SampledTimerWorkload.Run(sampledTimerGcPath, SampledSignalPlan.Parse(sampledTimerGcPercent).Percent,
        sink => new LinuxCpuEventSession(sink), initializeManagedPaths: true,
        initializeNativeTransport: true, initialInterval: TimerInitialInterval.Parse(gcInitialInterval),
        forceGc: TimerGcReach.ParseMode(sampledTimerGcMode));

if (args is ["--sampled-timer-scheduler", var timerSchedulerPath, var timerSchedulerPercent, var schedulerInterval, var timerSchedulerGc])
    return SampledTimerWorkload.Run(timerSchedulerPath, SampledSignalPlan.Parse(timerSchedulerPercent).Percent,
        sink => new LinuxCpuEventSession(sink), initializeManagedPaths: true, initializeNativeTransport: true,
        initialInterval: TimerInitialInterval.Parse(schedulerInterval), forceGc: TimerGcReach.ParseMode(timerSchedulerGc),
        schedulerFactory: () => new LinuxSchedulerEventSession());

if (args is ["--verify-measurement-boundaries"])
{
    MeasurementWindowCaptureChecks.Run();
    return 0;
}

if (args is ["--verify-timer-layout"])
{
    RingReaderChecks.Run();
    CpuCounterEvidenceChecks.Run();
    PerfAttributeChecks.TaskClockUsesMonotonicAbi();
    SampleClockEvidenceChecks.Run();
    InterleavedCalibrationChecks.Run();
    PhysicalCalibrationChecks.Run();
    MatchedCpuWorkersChecks.Run();
    SchedulerCaptureGroupChecks.Run();
    TimerSessionCleanupChecks.Run();
    MeasurementWindowCaptureChecks.Run();
    NativeTimerClient.VerifyLayout();
    TimerInitialIntervalChecks.Run();
    TargetLeaseReceiptsChecks.Run();
    TimerAccountingAdapterChecks.Run();
    TimerServiceControllerChecks.Run();
    ControllerPhaseCaptureChecks.Run();
    TimerGcReachChecks.Run();
    TimerWorkloadPlanChecks.Run();
    TimerWorkerLifetimeChecks.Run();
    return 0;
}

if (args is ["--verify-timer-interop", var timerPath, var timerMode])
    return TimerInteropChecks.Run(timerPath, timerMode);

if (args is ["--verify-timer-managed", var timerManagedPath])
    return TimerManagedWorkload.Run(timerManagedPath);

if (args is ["--verify-timer-managed", var timerGcPath, var timerGcMode])
    return TimerManagedWorkload.Run(timerGcPath, TimerGcReach.ParseMode(timerGcMode));

if (args is ["--verify-timer-transition", var timerTransitionPath, var timerScenario, var timerTransitionGc])
    return TimerManagedWorkload.Run(timerTransitionPath, TimerWorkloadPlan.ParseTransition(timerScenario, timerTransitionGc),
        () => new LinuxSignalTransitionOperations());

if (args is ["--verify-timer-transition", var fastTransitionPath, var fastScenario, var fastTransitionGc, var transitionInterval])
    return TimerManagedWorkload.Run(fastTransitionPath, TimerWorkloadPlan.ParseTransition(fastScenario, fastTransitionGc),
        () => new LinuxSignalTransitionOperations(), TimerInitialInterval.Parse(transitionInterval));

if (args is ["--service-accounting-cost", var serviceCostPath])
    return ServiceAccountingCosts.Run(serviceCostPath);

if (args is ["--service-snapshot-cost", var snapshotCostPath])
    return ServiceInteropChecks.Run(snapshotCostPath, false, true);

if (args is ["--verify-selected-service-interop", var selectedServicePath, var selectedReceiptMode])
{
    bool delayed = selectedReceiptMode switch
    {
        "receipt" => false,
        "late" => true,
        _ => throw new ArgumentException("Select receipt or late for selected service interop.")
    };
    return ServiceInteropChecks.Run(selectedServicePath, delayed, selectedService: true);
}

if (args is ["--verify-service-accounting"])
{
    OwnerPollingPolicyChecks.Run();
    TargetLeaseReceiptsChecks.Run();
    ServiceAccountingAdapterChecks.Run();
    ServiceSelectedAdapterChecks.Run();
    NativeServiceControllerChecks.Run();
    ControllerPhaseCaptureChecks.Run();
    SignalTargetLifetimeChecks.UnresolvedDeliveryKeepsTargetAlive();
    return 0;
}

if (args is ["--verify-service-initialization", var initializationPath, var initializationPercent])
    return SampledSignalAbortChecks.Run(initializationPath, "cold", true, initializationPercent);

if (args is ["--verify-service-interop", var servicePath, var receiptMode])
{
    bool delayed = receiptMode switch
    {
        "receipt" => false,
        "late" => true,
        _ => throw new ArgumentException("Select receipt or late for service interop.")
    };
    return ServiceInteropChecks.Run(servicePath, delayed);
}

if (args is ["--verify-service-receipt"])
{
    ServiceReceiptTimingChecks.Run();
    return 0;
}

if (args is ["--verify-scheduler-cost-startup", var schedulerCostCheckPath])
{
    SchedulerCostWorkloadChecks.FailedStartsRetainEvidence(schedulerCostCheckPath);
    return 0;
}

if (args is ["--scheduler-cost", var schedulerCostPath, var schedulerCostMode])
    return SchedulerCostWorkload.Run(schedulerCostPath, schedulerCostMode);

if (args is ["--verify-scheduler-records"])
{
    SchedulerRecordChecks.Run();
    return 0;
}

if (args is ["--verify-scheduler-shutdown", var schedulerCheckPath, var schedulerCheckMode])
    return SampledSignalAbortChecks.SchedulerShutdown(schedulerCheckPath, schedulerCheckMode);

if (args is ["--verify-owner-scheduler-shutdown", var ownerSchedulerCheckPath, var ownerSchedulerCheckMode])
    return SampledSignalAbortChecks.OwnerSchedulerShutdown(ownerSchedulerCheckPath, ownerSchedulerCheckMode);

if (args is ["--sampled-signal-scheduler", var schedulerPath, var schedulerPercent, var schedulerCadence,
    var schedulerStartup, var schedulerWait, var schedulerGc])
{
    var wait = SignalWaitMode.Parse(schedulerWait);
    var plan = SampledSignalPlan.Parse(schedulerPercent, schedulerCadence, schedulerStartup, schedulerGc);
    return SampledSignalWorkload.Run(schedulerPath, plan, () => new LinuxSignalBackend(wait),
        sink => new LinuxCpuEventSession(sink), new LinuxThreadCpuClock(),
        schedulerFactory: () => new LinuxSchedulerEventSession());
}

if (args is ["--verify-signal-trace-abort", var abortPath, var abortStartup])
    return SampledSignalAbortChecks.Run(abortPath, abortStartup);

if (args is ["--verify-signal-trace"])
{
    SampledSignalTraceChecks.Run();
    return 0;
}

if (args is [var traceMode, var tracePath, var tracePercent, var traceCadence, var traceStartup, var traceWait, var traceGc]
    && traceMode is "--sampled-signal-trace" or "--sampled-signal-target-cpu" or "--sampled-signal-scheduler-trace")
{
    var parsedWait = SignalWaitMode.Parse(traceWait);
    var plan = SampledSignalPlan.Parse(tracePercent, traceCadence, traceStartup, traceGc);
    bool targetCpu = traceMode == "--sampled-signal-target-cpu";
    Func<ISchedulerEventSession>? schedulerFactory = traceMode == "--sampled-signal-scheduler-trace"
        ? () => new LinuxSchedulerEventSession() : null;
    return SampledSignalWorkload.Run(tracePath, plan,
        () => new LinuxSignalBackend(parsedWait, targetCpu), sink => new LinuxCpuEventSession(sink), new LinuxThreadCpuClock(),
        new SampledSignalTrace(), targetCpu, schedulerFactory);
}

if (args is ["--verify-sampled-gc"])
{
    SampledGcStressChecks.Run();
    return 0;
}

if (args is ["--verify-signal-wait"])
{
    SignalWaitModeChecks.Run();
    return 0;
}

if (args is ["--verify-peer-cadence"])
{
    PeerSignalCadenceChecks.Run();
    return 0;
}

if (args is ["--verify-ring-reader"])
{
    RingReaderChecks.Run();
    return 0;
}

if (args is ["--verify-signal-timing"])
{
    SignalPhaseTimingChecks.Run();
    return 0;
}

if (args is ["--verify-signal-cadence"])
{
    SelectedSignalCadenceChecks.Run();
    return 0;
}

if (args is [var serviceMode, var nativePath, var nativePercent, var nativeCadence, var nativeStartup, var nativeWait, var nativeGc, .. var pollingArgs]
    && serviceMode is "--sampled-service" or "--sampled-service-phases" or "--sampled-service-jit" or "--sampled-service-initialized" or "--sampled-service-submission" or "--sampled-service-scheduler")
{
    var parsedWait = SignalWaitMode.Parse(nativeWait);
    var polling = pollingArgs switch
    {
        [] => OwnerPollingPolicy.Yielding,
        [var value] => OwnerPollingPolicy.Parse(value),
        _ => throw new ArgumentException("Supply at most one native owner polling policy.")
    };
    return SampledSignalWorkload.Run(nativePath, SampledSignalPlan.Parse(nativePercent, nativeCadence, nativeStartup, nativeGc),
        () => new LinuxServiceBackend(parsedWait, serviceMode is "--sampled-service-submission" or "--sampled-service-scheduler", polling), sink => new LinuxCpuEventSession(sink), new LinuxThreadCpuClock(),
        trace: serviceMode == "--sampled-service-jit" ? new SampledSignalTrace() : null,
        schedulerFactory: serviceMode == "--sampled-service-scheduler" ? () => new LinuxSchedulerEventSession() : null,
        captureServiceOwnerScheduling: serviceMode == "--sampled-service-scheduler",
        captureServicePhases: serviceMode != "--sampled-service",
        initializeManagedPaths: serviceMode is "--sampled-service-initialized" or "--sampled-service-submission" or "--sampled-service-scheduler");
}

if (args is ["--sampled-signal", var gcPath, var gcPercent, var gcCadence, var gcStartup, var gcWait, var sampledGc])
{
    var parsedWait = SignalWaitMode.Parse(gcWait);
    return SampledSignalWorkload.Run(gcPath, SampledSignalPlan.Parse(gcPercent, gcCadence, gcStartup, sampledGc),
        () => new LinuxSignalBackend(parsedWait), sink => new LinuxCpuEventSession(sink), new LinuxThreadCpuClock());
}

if (args is ["--sampled-signal", var modePath, var modePercent, var modeCadence, var modeStartup, var waitMode])
{
    var parsedWait = SignalWaitMode.Parse(waitMode);
    return SampledSignalWorkload.Run(modePath, SampledSignalPlan.Parse(modePercent, modeCadence, modeStartup),
        () => new LinuxSignalBackend(parsedWait), sink => new LinuxCpuEventSession(sink), new LinuxThreadCpuClock());
}

if (args is ["--signal-wait", var waitPath, var waitPlan, var waitGc, var waitKind])
{
    var parsedWait = SignalWaitMode.Parse(waitKind);
    bool forceGc = waitGc switch
    {
        "gc" => true,
        "no-gc" => false,
        _ => throw new ArgumentException("Select gc or no-gc signal measurement.")
    };
    return SignalWorkload.Run(waitPath, SignalPlan.Parse(waitPlan), forceGc, () => new LinuxSignalBackend(parsedWait));
}

if (args is ["--sampled-signal", var sampledSignalPath, var sampledSignalPercent])
    return SampledSignalWorkload.Run(sampledSignalPath, SampledSignalPlan.Parse(sampledSignalPercent),
        () => new LinuxSignalBackend(), sink => new LinuxCpuEventSession(sink), new LinuxThreadCpuClock());

if (args is ["--sampled-signal", var sampledCadencePath, var sampledCadencePercent, var sampledCadence])
    return SampledSignalWorkload.Run(sampledCadencePath, SampledSignalPlan.Parse(sampledCadencePercent, sampledCadence),
        () => new LinuxSignalBackend(), sink => new LinuxCpuEventSession(sink), new LinuxThreadCpuClock());

if (args is ["--sampled-signal", var sampledWarmPath, var sampledWarmPercent, var sampledWarmCadence, var sampledStartup])
    return SampledSignalWorkload.Run(sampledWarmPath, SampledSignalPlan.Parse(sampledWarmPercent, sampledWarmCadence, sampledStartup),
        () => new LinuxSignalBackend(), sink => new LinuxCpuEventSession(sink), new LinuxThreadCpuClock());

if (args is ["--verify-signal-targets"])
{
    SignalTargetChecks.Run();
    return 0;
}

if (args is ["--signal-transition-wait", var transitionWaitPath, var transitionWaitMode, var transitionWaitGc, var transitionWaitKind])
{
    var parsedWait = SignalWaitMode.Parse(transitionWaitKind);
    bool forceGc = transitionWaitGc switch
    {
        "gc" => true,
        "no-gc" => false,
        _ => throw new ArgumentException("Select gc or no-gc transition measurement.")
    };
    return SignalTransitionWorkload.Run(transitionWaitPath, SignalTransitionWorkload.Parse(transitionWaitMode), forceGc,
        () => new LinuxSignalBackend(parsedWait), () => new LinuxSignalTransitionOperations());
}

if (args is ["--signal-transition", var transitionPath, var transitionMode, var transitionGc])
{
    bool forceGc = transitionGc switch
    {
        "gc" => true,
        "no-gc" => false,
        _ => throw new ArgumentException("Select gc or no-gc transition measurement.")
    };
    return SignalTransitionWorkload.Run(transitionPath, SignalTransitionWorkload.Parse(transitionMode), forceGc,
        () => new LinuxSignalBackend(), () => new LinuxSignalTransitionOperations());
}

if (args is ["--signal", var signalPath, var signalMode, var signalGc])
{
    bool forceGc = signalGc switch
    {
        "gc" => true,
        "no-gc" => false,
        _ => throw new ArgumentException("Select gc or no-gc signal measurement.")
    };
    return SignalWorkload.Run(signalPath, SignalPlan.Parse(signalMode), forceGc, () => new LinuxSignalBackend());
}

CollectorChecks.Run();
TimingChecks.CadenceBoundaries();
InjectedProbeChecks.Run();
RawIlCandidateChecks.Run();
if (args is ["--verify-collector"]) return 0;
if (args is ["--verify-overhead"])
{
    OverheadChecks.Run();
    return 0;
}
if (args is ["--overhead", var overheadPath, var overheadStage, var overheadDuration])
{
    var plan = OverheadPlan.Parse(overheadStage);
    if (plan.Callbacks && (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64))
        throw new PlatformNotSupportedException("The current overhead CPU clock requires Linux x64.");
    OverheadWorkload.Run(overheadPath, plan, MeasurementWindow.Parse(overheadDuration),
        plan.Callbacks ? new LinuxThreadCpuClock() : null, plan.Collect ? sink => new LinuxCpuEventSession(sink) : null);
    return 0;
}
if (args.Length is 3 or 4 && args[0] == "--physical")
{
    PhysicalWorkload.Run(args[1], args[2], MeasurementWindow.Parse(args.Length == 4 ? args[3] : "500"));
    return 0;
}
if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
    throw new PlatformNotSupportedException("The syscall probe requires Linux x64.");
if (args.Length is < 1 or > 5) throw new ArgumentException("Pass a JSON evidence path, speedup percentage, wait backend, probe CPU milliseconds, and optional measurement milliseconds.");
var window = MeasurementWindow.Parse(args.Length == 5 ? args[4] : "500");
var probePlan = ProbePlan.Parse(Environment.GetEnvironmentVariable("COZNET_PROBE_MODE"),
    Environment.GetEnvironmentVariable("COZNET_PROBE_INTERVAL"));
var probeMode = probePlan.Mode;
var attributionMode = AttributionPlan.Parse(Environment.GetEnvironmentVariable("COZNET_ATTRIBUTION"), probePlan,
    Environment.GetEnvironmentVariable("COZNET_IL_MAP_MODE"), Environment.GetEnvironmentVariable("COZNET_PROBE_PLACEMENT"));
if (attributionMode != AttributionMode.Method && (Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") != "0" ||
    Environment.GetEnvironmentVariable("DOTNET_ReadyToRun") != "0"))
    throw new ArgumentException("Raw attribution requires disabled tiered compilation and ReadyToRun.");
int speedup = args.Length >= 2 ? int.Parse(args[1]) : 50;
if (speedup is not (0 or 50)) throw new ArgumentException("The capability workload supports zero or 50 percent.");
IWaitBackend waitBackend = (args.Length >= 3 ? args[2] : "managed") switch
{
    "managed" => new ManagedWaitBackend(),
    "linux-nanosleep" => new LinuxNanoSleepBackend(),
    _ => throw new ArgumentException("Select managed or linux-nanosleep waits.")
};
int probeCpuMilliseconds = args.Length >= 4 ? int.Parse(args[3]) : 10;
if (probeCpuMilliseconds is not (0 or 10)) throw new ArgumentException("Select zero or 10 CPU milliseconds between probes.");
IThreadCpuClock cpuClock = new LinuxThreadCpuClock();
TimingChecks.WaitDurationAndCancellation(waitBackend);
Thread.Sleep(1);
long beforeWarmupCalls = InjectedProbes.Calls;
if (CpuWorkload.Run() != 49995000L) throw new Exception("The workload warmup returned an invalid checksum.");
if (probeMode == ProbeMode.Injected) InjectedProbes.VerifyRewrite(probePlan, InjectedProbes.Calls - beforeWarmupCalls);
long initialInjectedCalls = InjectedProbes.Calls;
long tid = Native.GetTid(186);
var attr = PerfAttribute.TaskClock();
long fd = Native.PerfEventOpen(298, ref attr, 0, -1, -1, 8);
if (fd < 0)
{
    File.WriteAllText(args[0], JsonSerializer.Serialize(new { available = false, errno = Marshal.GetLastPInvokeError() }));
    return 2;
}
int page = Environment.SystemPageSize;
nuint mappingLength = (nuint)(65 * page);
IntPtr mapping = Native.Mmap(IntPtr.Zero, mappingLength, 3, 1, (int)fd, 0);
if (mapping == new IntPtr(-1))
{
    int error = Marshal.GetLastPInvokeError();
    Native.Close((int)fd);
    throw new Exception($"mmap failed: {error}");
}
try
{
    ulong offset = (ulong)Marshal.ReadInt64(mapping, 1040);
    ulong size = (ulong)Marshal.ReadInt64(mapping, 1048);
    if (offset != (ulong)page || size != (ulong)(64 * page)) throw new Exception("Unexpected perf ring layout.");
    IInstructionMap codeMap = PerfMapSnapshot.Read($"/tmp/perf-{Environment.ProcessId}.map");
    const string selectedSymbol = "int64 [CozNet.LinuxPerf] CpuWorkload::Run()[Optimized]";
    if (attributionMode != AttributionMode.Method)
        codeMap = RawIlCandidateMap.Read(codeMap, selectedSymbol, attributionMode == AttributionMode.RawExcludeCandidates);

    var observations = new List<PhaseObservation>();
    ulong previousCpu = 0;
    long checksum = 0;
    long completedBatches = 0;
    foreach (bool busy in new[] { true, false })
    {
        using var intervention = busy ? new SampledIntervention(selectedSymbol, speedup, waitBackend,
            cpuClock, probeCpuMilliseconds * 1000000L, probePlan) : null;
        using var injectedBinding = busy && probeMode == ProbeMode.Injected ? InjectedProbes.Bind(intervention!.Probe) : null;
        intervention?.WaitUntilReady();
        var capture = new SampleCapture(intervention);
        var reader = new RingReader(mapping, offset, size, tid, codeMap, selectedSymbol, capture);
        using var collector = new LiveSampleCollector(reader.Drain);
        if (Native.Ioctl((int)fd, 0x2400, 0) != 0) throw new Exception($"Enable failed: {Marshal.GetLastPInvokeError()}");
        var elapsed = Stopwatch.StartNew();
        if (busy)
        {
            while (elapsed.ElapsedMilliseconds < window.Milliseconds)
            {
                checksum += CpuWorkload.Run();
                completedBatches++;
                if (probeMode == ProbeMode.Manual) intervention?.Probe();
            }
        }
        else Thread.Sleep(500);
        int liveSamples = collector.Latest?.Samples ?? 0;
        int liveDeliveredSamples = capture.Published;
        var liveIntervention = intervention?.ObserveLive();
        double experimentMilliseconds = elapsed.Elapsed.TotalMilliseconds;
        if (Native.Ioctl((int)fd, 0x2401, 0) != 0) throw new Exception($"Disable failed: {Marshal.GetLastPInvokeError()}");
        if (Native.Read((int)fd, out ulong cpu, 8) != 8) throw new Exception($"Read failed: {Marshal.GetLastPInvokeError()}");
        var records = collector.Complete();
        var delivered = capture.AfterCollectorJoin();
        var interventionResult = intervention?.Complete(liveIntervention!);
        if (delivered.Length != records.Samples
            || delivered.Count(sample => sample.Method == selectedSymbol) != records.SelectedMethodSamples
            || delivered.Aggregate(0UL, (total, sample) => checked(total + sample.PeriodNanoseconds)) != records.RepresentedCpuNanoseconds)
            throw new Exception("Delivered sample records do not match the ring totals.");
        if (cpu < previousCpu) throw new Exception("The task CPU clock decreased.");
        observations.Add(new PhaseObservation(busy, experimentMilliseconds, elapsed.Elapsed.TotalMilliseconds, cpu - previousCpu,
            liveSamples, liveDeliveredSamples, records, delivered, interventionResult));
        previousCpu = cpu;
    }
    File.WriteAllText(args[0], JsonSerializer.Serialize(new { available = true, runtime = RuntimeInformation.FrameworkDescription,
        os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
        requestedPeriodNanoseconds = attr.SamplePeriod, measurementMilliseconds = window.Milliseconds,
        checksum, completedBatches, probeMode = probeMode.ToString(), probeBackedgeInterval = probePlan.BackedgeInterval,
        injectedCalls = InjectedProbes.Calls - initialInjectedCalls,
        attributionMode = attributionMode.ToString(), rawAttribution = (codeMap as RawIlCandidateMap)?.Evidence,
        observations }, new JsonSerializerOptions {
            WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    var active = observations[0];
    if (attributionMode == AttributionMode.RawExcludeCandidates && !active.Delivered.Any(x => x.Method == RawIlCandidateMap.CandidateSymbol))
        throw new Exception("The attribution diagnostic captured no excluded candidate sample.");
    if (InjectedProbes.Calls - initialInjectedCalls != checked(completedBatches * probePlan.CallsPerBatch))
        throw new Exception("Injected probe counts do not match completed work.");
    var blocked = observations[1];
    if (completedBatches == 0 || checksum != checked(completedBatches * 49995000L))
        throw new Exception("The arithmetic result does not match completed work.");
    if (active.CpuNanoseconds == 0 || active.Records.Samples == 0) throw new Exception("The busy phase produced no CPU samples.");
    if (active.LiveSamples == 0) throw new Exception("No samples reached the target before collection stopped.");
    if (active.LiveDeliveredSamples == 0) throw new Exception("No individual samples reached the sink before collection stopped.");
    if (active.Records.SelectedMethodSamples == 0) throw new Exception("No CPU event resolved to the selected managed method.");
    if (blocked.CpuNanoseconds >= active.CpuNanoseconds || blocked.Records.Samples >= active.Records.Samples)
        throw new Exception("The blocked phase was not below the busy phase in both CPU time and sample count.");
    if (observations.Any(phase => phase.Records.LostRecords != 0 || phase.Records.LostSamples != 0 || phase.Records.Throttles != 0))
        throw new Exception("The run contains lost or throttled events. Inspect the evidence file.");
    return 0;
}
finally { Native.Munmap(mapping, mappingLength); Native.Close((int)fd); }

sealed record SampleBatch(int Samples, int DistinctInstructionPointers, ulong RepresentedCpuNanoseconds,
    int LostRecords, ulong LostSamples, int Throttles, int SelectedMethodSamples, int UnmappedSamples,
    IReadOnlyList<string> SelectedSymbols);
sealed record PhaseObservation(bool Busy, double ExperimentMilliseconds, double WallMilliseconds, ulong CpuNanoseconds,
    int LiveSamples, int LiveDeliveredSamples, SampleBatch Records, IReadOnlyList<CpuSample> Delivered,
    InterventionResult? Intervention);

static class CpuWorkload
{
    public const int Iterations = 10000;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Probe() => InjectedProbes.Hit();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ProbeBound(ulong generation, uint site) => InjectedProbes.HitBound(generation, site);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static long Run()
    {
        long sum = 0;
        for (int i = 0; i < Iterations; i++) sum += i;
        return sum;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static long RunOptimized()
    {
        long sum = 37497500;
        for (int i = 0; i < 5000; i++) sum += i;
        return sum;
    }
}

sealed class RingReader(IntPtr mapping, ulong offset, ulong size, long tid,
    IInstructionMap codeMap, string selectedSymbol, ICpuSampleSink sink)
{
    private SampleBatch? snapshot;
    private ulong tail = (ulong)Marshal.ReadInt64(mapping, 1032);
    private ulong priorTime;
    private ulong priorCpu;
    private int samples = 0, lostRecords = 0, throttles = 0;
    private ulong lostSamples = 0, periods = 0;
    private readonly HashSet<ulong> ips = new();

    private readonly HashSet<string> selectedSymbols = new();
    private int selected = 0, unmapped = 0;

    private byte[] Copy(ulong position, int length)
    {
        var bytes = new byte[length];
        for (int i = 0; i < length; i++) bytes[i] = Marshal.ReadByte(mapping + checked((int)(offset + ((position + (ulong)i) % size))));
        return bytes;
    }
    public SampleBatch Drain()
    {
        ulong head = (ulong)Marshal.ReadInt64(mapping, 1024);
        Thread.MemoryBarrier();
        if (head < tail || head - tail > size) throw new Exception("Ring bounds are invalid.");
        if (head == tail && snapshot is not null) return snapshot;
        snapshot = null;
        while (tail < head)
        {
            if (head - tail < 8) throw new Exception("Truncated record header.");
            byte[] header = Copy(tail, 8);
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(header);
            ushort misc = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(4));
            ushort length = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6));
            if (length < 8 || length > head - tail) throw new Exception("Invalid record length.");
            byte[] record = Copy(tail, length);
            if (type == 9)
            {
                if (length != 48 || (misc & 7) != 2) throw new Exception("Unexpected sample layout or CPU mode.");
                ulong ip = BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(8));
                uint recordedPid = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(16));
                uint recordedTid = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(20));
                ulong timestamp = BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(24));
                ulong period = BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(32));
                ulong cpu = BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(40));
                if (ip == 0 || recordedPid != Environment.ProcessId || recordedTid != tid || timestamp < priorTime || period == 0)
                    throw new Exception("Invalid sample attribution metadata.");
                if (cpu < priorCpu) throw new InvalidDataException("The cumulative sample CPU counter regressed.");
                priorCpu = cpu;
                ips.Add(ip); priorTime = timestamp; periods += period; samples++;
                string? symbol = codeMap.Resolve(ip);
                sink.Publish(CpuSample.Create(recordedPid, recordedTid, timestamp, period, ip, symbol, cpu));

                if (symbol is null) unmapped++;
                else if (string.Equals(symbol, selectedSymbol, StringComparison.Ordinal)) { selected++; selectedSymbols.Add(symbol); }
            }
            else if (type == 2)
            {
                if (length != 24) throw new Exception("Unexpected lost-record layout.");
                lostRecords++; lostSamples += BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(16));
            }
            else if (type is 5 or 6) throttles++;
            else throw new Exception($"Unexpected perf record type {type}.");
            tail += length;
        }
        Thread.MemoryBarrier();
        Marshal.WriteInt64(mapping, 1032, (long)tail);
        snapshot = new SampleBatch(samples, ips.Count, periods, lostRecords, lostSamples, throttles,
            selected, unmapped, Array.AsReadOnly(selectedSymbols.Order().ToArray()));
        return snapshot;
    }
}

static class Native
{
    [DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
    public static extern long PerfEventOpen(long number, ref PerfAttribute attr, int pid, int cpu, int group, ulong flags);
    [DllImport("libc", EntryPoint = "syscall")]
    public static extern long GetTid(long number);
    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    public static extern int Ioctl(int fd, ulong request, ulong arg);
    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    public static extern long Read(int fd, out ulong value, ulong length);
    [DllImport("libc", EntryPoint = "mmap", SetLastError = true)]
    public static extern IntPtr Mmap(IntPtr address, nuint length, int protection, int flags, int fd, long offset);
    [DllImport("libc", EntryPoint = "munmap")]
    public static extern int Munmap(IntPtr address, nuint length);
    [DllImport("libc", EntryPoint = "close")]
    public static extern int Close(int fd);
}
