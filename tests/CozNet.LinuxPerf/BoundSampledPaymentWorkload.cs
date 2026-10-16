using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using CozNet;
using Microsoft.FSharp.Core;
using static CooperativeManagedChecks;

static class BoundSampledPaymentWorkload
{
    private sealed record CreditedSample(int Index, CpuSample Sample, long Credit, ulong EligibilityEpoch);
    private sealed record Payment(ulong Command, ulong Generation, long Budget, long Started,
        long Finished, CooperativeObservation Observation, CooperativeCleanupCapture Cleanup, CooperativeReceiptInterval ReceiptInterval);
    private sealed record Debt(long Timestamp, long Global, long SelectedCredit, long PeerWait, long Selected, long Peer,
        long SelectedLocal, long PeerLocal, long SelectedWait);
    private sealed record PauseBoundary(long Started, int FirstUnprocessed, int? FinalDrainCount = null,
        long? CallbackFinished = null, long? Resumed = null, BoundSelectedAdmission? Admission = null, BoundPauseTiming? Timing = null);
    private sealed record FailureBoundary(Debt? LastObservedDebt, ulong Command, ulong Generation, long Budget,
        long RequestStarted, CooperativeFailureCapture Capture);

    private sealed record PeerProgressObservation(uint Slot, ulong Command, ulong Generation, long Budget,
        long RequestStarted, CpuSampleOwner? Owner, long? DebtTimestamp, CooperativeProgressCapture? Capture, string? Unavailable);
    public static int Run(string path, SampledSignalPlan settings, string library,
        Func<CpuSampleOwner> readOwner, Func<IInstructionMap, ICpuSampleSink, ICpuEventSession> createSource,
        bool invalidateAfterPayment = false,
        Func<IInstructionMap, ICpuSampleSink, IOwnerStoppedCpuEventSession>? selectedPaymentSource = null,
        bool invalidateAfterSelectedPayment = false, BoundSelectedCadencePlan? cadencePlan = null, IBoundSchedulerBackend? schedulerBackend = null)
    {
        Require(cadencePlan is null || selectedPaymentSource is not null && ReferenceEquals(settings, cadencePlan.Sampling),
            "A cadence comparison requires its parsed settings and a pausable source.");
        NativeLibrary.SetDllImportResolver(typeof(CooperativeNativeMethods).Assembly,
            (name, _, _) => name == "CozNet.Cooperative" ? NativeLibrary.Load(Path.GetFullPath(library)) : IntPtr.Zero);
        var plan = ProbePlan.Parse("injected", Environment.GetEnvironmentVariable("COZNET_PROBE_INTERVAL"));
        var selected = Parsed(SignalAccountingModule.register(FSharpOption<int>.Some(settings.Percent), SignalAccountingModule.create()));
        var peer = Parsed(SignalAccountingModule.register(FSharpOption<int>.None, selected.Item2));
        var state = peer.Item2;
        var accounting = new CooperativeAccountingCache(selected.Item1, peer.Item1);
        var stream = Parsed(SignalAccountingModule.sampleIdentity(selected.Item1, state));
        var samples = new List<CreditedSample>();
        var payments = new List<Payment>();
        var selectedPayments = new List<Payment>();
        var pauses = new List<PauseBoundary>();
        bool selectedPaymentEnabled = selectedPaymentSource is not null;
        Require(schedulerBackend is null || selectedPaymentEnabled, "Scheduler capture requires selected payment.");
        BoundSchedulerCapture? scheduling = null;
        BoundSchedulerCapture.Result? schedulerCapture = null;
        CpuSampleOwner? schedulerControllerOwner = null, selectedOwner = null;
        bool pauseTimingEnabled = Environment.GetEnvironmentVariable("COZNET_PAUSE_TIMING") switch
        {
            null or "0" => false,
            "1" => true,
            _ => throw new ArgumentException("COZNET_PAUSE_TIMING must be 0 or 1.")
        };
        Require(!pauseTimingEnabled || selectedPaymentEnabled, "Pause timing requires selected payment.");
        bool childCpuEnabled = Environment.GetEnvironmentVariable("COZNET_CHILD_CPU") switch
        {
            null or "0" => false,
            "1" => true,
            _ => throw new ArgumentException("COZNET_CHILD_CPU must be 0 or 1.")
        };
        Require(!childCpuEnabled || selectedPaymentEnabled, "Child CPU capture requires selected payment.");
        bool peerProgressEnabled = Environment.GetEnvironmentVariable("COZNET_PEER_PROGRESS") switch
        {
            null or "0" => false, "1" => true,
            _ => throw new ArgumentException("COZNET_PEER_PROGRESS must be 0 or 1.")
        };
        Require(!peerProgressEnabled || selectedPaymentEnabled, "Peer progress capture requires selected payment.");
        Require(schedulerBackend is null || peerProgressEnabled, "Bound scheduler capture requires peer progress capture.");
        scheduling = schedulerBackend is null ? null : new BoundSchedulerCapture(schedulerBackend);
        CpuSampleOwner? peerOwner = null;
        string? peerOwnerFailure = null;
        PeerProgressObservation? peerSubmitProgress = null, peerDebtProgress = null;
        bool ownerDrainEnabled = Environment.GetEnvironmentVariable("COZNET_OWNER_DRAIN") switch
        {
            null or "0" => false,
            "1" => true,
            _ => throw new ArgumentException("COZNET_OWNER_DRAIN must be 0 or 1.")
        };
        Require(!ownerDrainEnabled || selectedPaymentEnabled && cadencePlan is not null,
            "Owner draining requires selected payment with an explicit cadence.");
        var handoff = BoundSelectedHandoff.Parse(Environment.GetEnvironmentVariable("COZNET_SELECTED_HANDOFF"),
            new ThreadYieldHandoffBackend());
        Require(!handoff.Enabled || ownerDrainEnabled, "Selected handoff requires owner draining and an explicit cadence.");
        var cadence = cadencePlan is null ? null : new BoundSelectedCheckCadence(cadencePlan);
        Require(!invalidateAfterSelectedPayment || selectedPaymentEnabled,
            "Selected-payment invalidation requires a pausable source factory.");
        var cleanupErrors = new List<string>();
        BoundSampleCollection? collection = null;
        BoundSampleResult? result = null;
        BoundCooperativeClient? client = null;
        CooperativeInteropChecks.Worker? worker = null;
        CooperativeInteropChecks.Worker? controller = null;
        bool controllerJoined = false;
        CooperativeSite? site = null;
        CooperativeSite? selectedSite = null;
        IDisposable? selectedCallback = null;
        bool selectedRegistered = false;
        CooperativeAccountingAdapter? selectedPending = null;
        SignalServiceLease? selectedLease = null;
        long selectedPaid = 0, selectedStarted = 0, selectedBudget = 0;
        long? selectedWorkStarted = null, selectedWorkFinished = null;
        ulong selectedCommand = 0, selectedGeneration = 0;
        FailureBoundary? selectedFailureBoundary = null;
        CooperativeCleanupCapture? selectedCleanup = null;
        CooperativeObservation? selectedReceipt = null;
        CooperativeAccountingAdapter? pending = null;
        SignalServiceLease? lease = null;
        Task? peerLoop = null;
        using var peerStarted = new ManualResetEventSlim();
        using var peerStopped = new ManualResetEventSlim();
        int stop = 0;
        bool registered = false, joined = false, closed = false, completed = false;
        bool invalidated = false, rejectionVerified = false, rejectedAfterPayment = false;
        string? peerFailure = null;
        string? failure = null;
        string? verificationFailure = null;
        long credit = 0, paid = 0, batches = 0, checksum = 0, peerBatches = 0, peerChecksum = 0, peerCalls = 0;
        long pendingStarted = 0, pendingBudget = 0;
        ulong represented = 0, command = 0, generation = 0;
        Debt? selectedPeak = null, peerPeak = null, finalDebt = null;
        Debt? lastObservedDebt = null;
        FailureBoundary? failureBoundary = null;
        var receiptIntervals = new CooperativeReceiptIntervalCapture();
        var publication = BoundSamplePublication.Parse(Environment.GetEnvironmentVariable("COZNET_BATCH_SERVICE"));
        var receiptReadiness = BoundReceiptReadiness.Parse(Environment.GetEnvironmentVariable("COZNET_DIRECT_RECEIPT_READY"));
        Require(!receiptReadiness.DirectEnabled || selectedPaymentEnabled, "Direct receipt readiness requires selected payment.");

        var dispatchTiming = CooperativeDispatchTiming.Parse(Environment.GetEnvironmentVariable("COZNET_DISPATCH_TIMING"));
        Require(dispatchTiming is null || selectedPaymentEnabled, "Dispatch timing requires the selected-payment controller.");

        T OnController<T>(Func<T> action, CooperativeDispatchOperation operation)
        {
            if (controller is null) return action();
            if (dispatchTiming is null) return controller.Run(action);
            var identity = operation switch
            {
                CooperativeDispatchOperation.SelectedSubmit or CooperativeDispatchOperation.SelectedHeader
                    or CooperativeDispatchOperation.SelectedRead or CooperativeDispatchOperation.SelectedRelease
                    or CooperativeDispatchOperation.SelectedFailureCapture or CooperativeDispatchOperation.SelectedCleanupHeader
                    or CooperativeDispatchOperation.SelectedCleanupRelease => (selectedCommand, selectedGeneration),
                CooperativeDispatchOperation.PeerSubmit or CooperativeDispatchOperation.PeerHeader
                    or CooperativeDispatchOperation.PeerRead or CooperativeDispatchOperation.PeerRelease
                    or CooperativeDispatchOperation.PeerFailureCapture or CooperativeDispatchOperation.PeerCleanupHeader
                    or CooperativeDispatchOperation.PeerCleanupRelease => (command, generation),
                _ => (0UL, 0UL)
            };
            return dispatchTiming.Run(operation, identity.Item1, identity.Item2, action, work => controller.Run(work));
        }
        void Control(Action action, CooperativeDispatchOperation operation) => OnController(() => { action(); return true; }, operation);

        PeerProgressObservation CapturePeerProgress(long? debtTimestamp)
        {
            if (client is null || lease is null || generation == 0)
                return new(0, command, generation, pendingBudget, pendingStarted, peerOwner, debtTimestamp, null,
                    "No submitted peer request is pending.");
            return new(0, command, generation, pendingBudget, pendingStarted, peerOwner, debtTimestamp,
                client.CaptureProgress(0, generation), null);
        }
        void RetireTarget(uint slot)
        {
            if (scheduling is null) client!.RetireCurrentThread(slot);
            else scheduling.Retire(() => client!.RetireCurrentThread(slot));
        }
        Debt ObserveDebt()
        {
            var observed = accounting.Observe(state);
            var debt = new Debt(Stopwatch.GetTimestamp(), observed.Global, credit, paid,
                observed.Selected, observed.Peer, observed.SelectedLocal, observed.PeerLocal, selectedPaid);
            lastObservedDebt = debt;
            if (selectedPeak is null || debt.Selected > selectedPeak.Selected) selectedPeak = debt;
            if (peerPeak is null || debt.Peer > peerPeak.Peer) peerPeak = debt;
            if (debt.Selected > 2000000 || debt.Peer > 2000000)
            {
                if (peerProgressEnabled && peerDebtProgress is null) peerDebtProgress = CapturePeerProgress(debt.Timestamp);
                throw new InvalidDataException($"Sampled cooperative debt exceeded 2 ms: selected={debt.Selected}, peer={debt.Peer}.");
            }
            return debt;
        }

        void Process(CpuSample sample, ulong eligibilityEpoch)
        {
            represented = checked(represented + sample.PeriodNanoseconds);
            var observation = Parsed(CpuSampleObservationModule.tryCreate(stream, checked((ulong)samples.Count + 1),
                represented, eligibilityEpoch, SampleThreadState.Runnable,
                sample.Method == OverheadWorkload.SelectedSymbol ? SampleLocation.Selected : SampleLocation.Unmapped));
            var observed = Parsed(SignalAccountingModule.observe(selected.Item1, observation, state));
            state = observed.Item2;
            long amount = DelayTicksModule.value(observed.Item1);
            credit = checked(credit + amount);
            samples.Add(new(samples.Count, sample, amount, eligibilityEpoch));
            cadence?.Observe(eligibilityEpoch);
            var debt = accounting.Observe(state);
            if (debt.SelectedLocal >= debt.Global)
            {
                var prepared = Parsed(SignalAccountingModule.prepare(selected.Item1, state));
                Require(prepared.Item1 is null, "The no-payment selected owner reserved a delay.");
                state = prepared.Item2;
            }
            ObserveDebt();
        }

        void Publish(IReadOnlyList<CpuSample> incoming, int offset = 0)
        {
            publication.Publish(incoming, offset, sample => Process(sample, 1), Service);
        }

        void PublishEpochs(IReadOnlyList<BoundEpochSample> incoming, int offset = 0)
        {
            publication.Publish(incoming, offset, sample => Process(sample.Sample, sample.EligibilityEpoch), Service);
        }

        void PaySelected(ulong version, uint point)
        {
            Require(client!.PollFromProbe(1, version, point) == CooperativePollOutcome.Idle,
                "The selected owner entered a probe with an unexpected native request.");
            if (point != 2) return;
            if (ownerDrainEnabled) collection!.DrainOnOwner();
            BoundSelectedAdmission? admission = null;
            if (cadence is not null)
            {
                if (collection!.LiveSamples > samples.Count)
                    PublishEpochs(collection.ReadEpochProvisional(samples.Count));
                Service();
                if (handoff.Enabled && handoff.TryHandoff(ObserveDebt().Peer, pending is not null))
                {
                    collection.DrainOnOwner();
                    if (collection.LiveSamples > samples.Count)
                        PublishEpochs(collection.ReadEpochProvisional(samples.Count));
                    Service();
                }
                bool admitted = cadence.TryCheck(out var candidate);
                if (!admitted || ObserveDebt().Selected == 0) return;
                admission = candidate;
            }
            else if (accounting.Observe(state).Selected == 0) return;
            int pauseIndex = pauses.Count;
            var timing = pauseTimingEnabled ? new BoundPauseTiming() : null;
            pauses.Add(new(Stopwatch.GetTimestamp(), samples.Count, Admission: admission, Timing: timing));
            collection!.ExecuteExcluded(drained =>
            {
                PublishEpochs(drained, samples.Count);
                if (timing is not null) timing.SamplesPublished = Stopwatch.GetTimestamp();
                pauses[pauseIndex] = pauses[pauseIndex] with { FinalDrainCount = samples.Count };
                var prepared = Parsed(SignalAccountingModule.prepareSelectedService(selected.Item1, state));
                state = prepared.Item2;
                if (prepared.Item1 is not null)
                {
                    selectedLease = prepared.Item1.Value;
                    selectedBudget = SignalServiceLeaseModule.nanoseconds(selectedLease);
                    selectedCommand = checked(selectedCommand + 1);
                    selectedStarted = Stopwatch.GetTimestamp();
                    state = Parsed(SignalAccountingModule.sentService(selectedLease, state));
                    selectedGeneration = 0;
                    selectedGeneration = OnController(() => client.Submit(1, selectedCommand, checked((ulong)selectedBudget)), CooperativeDispatchOperation.SelectedSubmit);
                    selectedPending = new(CooperativeAccountingBinding.Parse(selectedLease, selectedGeneration, selectedCommand, selectedSite!));
                    while (!receiptReadiness.Ready(1, client.ReceiptReady,
                        slot => OnController(() => client.CleanupHeader(slot), CooperativeDispatchOperation.SelectedHeader).Visibility >= 3))
                    {
                        Require(Stopwatch.GetElapsedTime(selectedStarted) <= TimeSpan.FromMilliseconds(100),
                            "A selected cooperative request exceeded 100 ms.");
                        Require(client.PollFromProbe(1, version, point) == CooperativePollOutcome.Serviced,
                            "The selected probe rejected cooperative service.");
                    }
                    Require(Stopwatch.GetElapsedTime(selectedStarted) <= TimeSpan.FromMilliseconds(100),
                        "A selected cooperative receipt exceeded 100 ms.");
                    var accepted = OnController(() => client.ReadAndAccept(1, selectedPending, state), CooperativeDispatchOperation.SelectedRead);
                    var interval = receiptIntervals.Parse(new(1, selectedCommand, selectedGeneration, selectedBudget),
                        selectedStarted, Stopwatch.GetTimestamp(), Stopwatch.Frequency);
                    selectedPending = accepted.Adapter;
                    state = accepted.State;
                    Require(selectedPending.IsTerminal, "The selected cooperative lease did not settle.");
                    selectedReceipt = client.LastAccountingObservation
                        ?? throw new InvalidDataException("The selected request has no retained receipt observation.");
                    Control(() => client.ReleaseAfterRetiredCopy(1), CooperativeDispatchOperation.SelectedRelease);
                    selectedCleanup = client.LastCleanup;
                    selectedPaid = checked(selectedPaid + selectedPending.ConfirmedNanoseconds);
                    selectedPayments.Add(new(selectedCommand, selectedGeneration, selectedBudget, selectedStarted,
                        Stopwatch.GetTimestamp(), selectedReceipt, selectedCleanup!, interval));
                    selectedPending = null;
                    selectedLease = null;
                    var debt = accounting.Observe(state);
                    if (debt.SelectedLocal >= debt.Global)
                    {
                        var caughtUp = Parsed(SignalAccountingModule.prepare(selected.Item1, state));
                        Require(caughtUp.Item1 is null, "Selected overshoot publication reserved another delay.");
                        state = caughtUp.Item2;
                    }
                    ObserveDebt();
                    if (invalidateAfterSelectedPayment)
                    {
                        ProbeIdentityChecks.InvalidateWithDuplicateModule(plan);
                        invalidated = true;
                    }
                }
                pauses[pauseIndex] = pauses[pauseIndex] with { CallbackFinished = Stopwatch.GetTimestamp() };
            }, timing);
            pauses[pauseIndex] = pauses[pauseIndex] with { Resumed = Stopwatch.GetTimestamp() };
            cadence?.Resume();
        }

        void Service()
        {
            if (pending is not null)
            {
                Require(Stopwatch.GetElapsedTime(pendingStarted) <= TimeSpan.FromMilliseconds(100),
                    "A sampled cooperative request exceeded 100 ms.");
                if (receiptReadiness.Ready(0, slot => client!.ReceiptReady(slot),
                    slot => OnController(() => client!.CleanupHeader(slot), CooperativeDispatchOperation.PeerHeader).Visibility == 3))
                {
                    var accepted = OnController(() => client!.ReadAndAccept(0, pending, state), CooperativeDispatchOperation.PeerRead);
                    var interval = receiptIntervals.Parse(new(2, command, generation, pendingBudget),
                        pendingStarted, Stopwatch.GetTimestamp(), Stopwatch.Frequency);
                    Require(accepted.Adapter.IsTerminal, "A retired cooperative request did not settle its ledger lease.");
                    pending = accepted.Adapter;
                    state = accepted.State;
                    var observation = client!.LastAccountingObservation
                        ?? throw new InvalidDataException("The accepted request has no retained receipt observation.");
                    Require(observation.Raw.Header.Command == command
                        && observation.Raw.Header.Generation == generation && observation.Raw.Header.Budget == (ulong)pendingBudget,
                        "The retained receipt observation does not match the accepted request.");
                    long nextPaid = checked(paid + pending.ConfirmedNanoseconds);
                    Control(() => client.ReleaseAfterRetiredCopy(0), CooperativeDispatchOperation.PeerRelease);
                    payments.Add(new(command, generation, pendingBudget, pendingStarted, Stopwatch.GetTimestamp(), observation, client.LastCleanup!, interval));
                    paid = nextPaid;
                    pending = null;
                    lease = null;
                }
            }
            if (pending is null)
            {
                var prepared = accounting.PreparePeer(state);
                state = prepared.State;
                if (prepared.Lease is not null)
                {
                    lease = prepared.Lease;
                    pendingBudget = SignalServiceLeaseModule.nanoseconds(lease);
                    command = checked(command + 1);
                    pendingStarted = Stopwatch.GetTimestamp();
                    state = Parsed(SignalAccountingModule.sentService(lease, state));
                    generation = 0;
                    generation = OnController(() => client!.Submit(0, command, checked((ulong)pendingBudget)), CooperativeDispatchOperation.PeerSubmit);
                    if (peerProgressEnabled) peerSubmitProgress = CapturePeerProgress(null);
                    pending = new(CooperativeAccountingBinding.Parse(lease, generation, command, site!));
                }
            }
            ObserveDebt();
        }

        try
        {
            Require(CpuWorkload.Run() == 49995000L, "Selected warmup changed its checksum.");
            var binding = ClrProbeBinding.ReadCurrent();
            if (selectedPaymentEnabled) controller = new();
            client = OnController(() =>
            {
                scheduling?.Register(0);
                if (scheduling is not null) schedulerControllerOwner = readOwner();
                return new BoundCooperativeClient(binding, selectedPaymentEnabled ? 2U : 1U, observeCpu: childCpuEnabled);
            }, CooperativeDispatchOperation.Create);
            if (selectedPaymentEnabled)
            {
                selectedRegistered = true;
                selectedSite = client.RegisterCurrentThread(1, 1);
                scheduling?.Register(1);
            }
            worker = new();
            registered = true;
            site = worker.Run(() =>
            {
                var registeredSite = client.RegisterCurrentThread(0, 2, peerProgressEnabled);
                scheduling?.Register(2);
                if (peerProgressEnabled)
                {
                    try { peerOwner = readOwner(); }
                    catch (Exception error) { peerOwnerFailure = error.ToString(); }
                }
                return registeredSite;
            });
            scheduling?.Begin();
            worker.Run(() => Require(CpuWorkload.Run() == 49995000L, "Peer warmup changed its checksum."));
            Action peerWork = () =>
            {
                long initialCalls = InjectedProbes.Calls;
                try
                {
                    using var callback = InjectedProbes.BindBound((version, point) =>
                        Require(client.PollFromProbe(0, version, point) is CooperativePollOutcome.Idle or CooperativePollOutcome.Serviced,
                            "The peer probe rejected cooperative service."));
                    peerStarted.Set();
                    while (Volatile.Read(ref stop) == 0)
                    {
                        peerChecksum += CpuWorkload.Run();
                        peerBatches++;
                    }
                }
                finally { peerCalls = InjectedProbes.Calls - initialCalls; peerStopped.Set(); }
            };
            peerLoop = cadencePlan is null ? Task.Run(() => worker.Run(peerWork)) : worker.Start(peerWork);
            Require(peerStarted.Wait(TimeSpan.FromSeconds(5)), "The paying peer did not start.");
            selectedOwner = readOwner();
            collection = new(new ClrProbeLifetime(binding), selectedOwner);
            if (selectedPaymentEnabled)
            {
                if (ownerDrainEnabled) collection.StartOwnerPausable(selectedPaymentSource!);
                else collection.StartPausable(selectedPaymentSource!);
                selectedCallback = InjectedProbes.BindBound(PaySelected);
            }
            else collection.Start(createSource);
            long initialCalls = InjectedProbes.Calls;
            var elapsed = Stopwatch.StartNew();
            if (cadencePlan is not null) selectedWorkStarted = Stopwatch.GetTimestamp();
            while (cadencePlan is null ? elapsed.Elapsed < TimeSpan.FromSeconds(2) : batches < cadencePlan.Batches)
            {
                Require(!peerLoop.IsCompleted, "The paying peer stopped during sample collection.");
                checksum += CpuWorkload.Run();
                batches++;
                if (ownerDrainEnabled) collection.DrainOnOwner();
                if (collection.LiveSamples > samples.Count)
                {
                    if (selectedPaymentEnabled) PublishEpochs(collection.ReadEpochProvisional(samples.Count));
                    else Publish(collection.ReadProvisional(samples.Count));
                }
                Service();
                if (invalidateAfterPayment && payments.Count > 0)
                {
                    ProbeIdentityChecks.InvalidateWithDuplicateModule(plan);
                    invalidated = true;
                    collection.ReadProvisional(samples.Count);
                    throw new InvalidOperationException("The invalidated collection accepted a live read after payment.");
                }
            }
            if (cadencePlan is not null) selectedWorkFinished = Stopwatch.GetTimestamp();
            Require(InjectedProbes.Calls - initialCalls == checked(batches * plan.CallsPerBatch),
                "The selected workload changed its injected probe count.");
            result = collection.Complete();
            Require(result.Samples.Take(samples.Count).SequenceEqual(samples.Select(item => item.Sample)),
                "The accepted sample source changed its provisional prefix.");
            if (selectedPaymentEnabled) PublishEpochs(collection.EpochDiagnostics, samples.Count);
            else Publish(result.Samples, samples.Count);
            var drain = Stopwatch.StartNew();
            while (pending is not null || accounting.Observe(state).Peer > 0)
            {
                Require(drain.Elapsed < TimeSpan.FromMilliseconds(100), "The final sampled cooperative debt did not settle within 100 ms.");
                Service();
                Thread.Yield();
            }
            finalDebt = ObserveDebt();
            Require(samples.Count > 1 && result.Batch.SelectedMethodSamples > 0 && batches > 0 && checksum == checked(batches * 49995000L),
                "The sampled cooperative workload did not preserve selected work and sample reach.");
            UInt128 selectedPeriods = 0;
            for (int index = 1; index < samples.Count; ++index)
                if (samples[index].EligibilityEpoch == samples[index - 1].EligibilityEpoch
                    && samples[index].Sample.Method == OverheadWorkload.SelectedSymbol)
                    selectedPeriods += samples[index].Sample.PeriodNanoseconds;
            Require((UInt128)credit == selectedPeriods * (uint)settings.Percent / 100,
                "Sample credits differ from selected periods after the epoch baseline.");
            Require(settings.Percent == 0 ? credit == 0 && paid == 0 && payments.Count == 0 : credit > 0 && paid >= credit && payments.Count > 0,
                "The sampled intervention did not reach its zero or positive payment condition.");
            if (selectedPaymentEnabled)
                Require(settings.Percent == 0 ? selectedPaid == 0 && selectedPayments.Count == 0 && pauses.Count == 0
                    : selectedPaid > 0 && selectedPayments.Count > 0 && pauses.Count >= selectedPayments.Count,
                    "The selected-payment run did not reach its zero or positive selected payment condition.");
            Require(SignalAccountingModule.pendingCount(state) == 0, "The completed run retains a ledger reservation.");
        }
        catch (Exception error)
        {
            var capturedDebt = lastObservedDebt;
            Func<CooperativeRawCapture?> readPeer = () => client is not null && client.HasPending(0)
                ? OnController(() => client.CaptureDiagnostic(0), CooperativeDispatchOperation.PeerFailureCapture) : null;
            var captures = selectedPaymentEnabled
                ? CooperativeFailureCapture.CaptureAll(error, readPeer,
                    () => client is not null && client.HasPending(1) ? OnController(() => client.CaptureDiagnostic(1), CooperativeDispatchOperation.SelectedFailureCapture) : null)
                : CooperativeFailureCapture.CaptureAll(error, readPeer);
            var capture = captures[0];
            failureBoundary = new(capturedDebt, command, generation, pendingBudget, pendingStarted, capture);
            if (selectedPaymentEnabled)
                selectedFailureBoundary = new(capturedDebt, selectedCommand, selectedGeneration, selectedBudget, selectedStarted, captures[1]);
            failure = capture.Failure;
            rejectedAfterPayment = invalidated && error is InvalidDataException
                && error.GetBaseException().Message == "The CLR probe lifetime is no longer admitted.";
        }
        finally
        {
            try { selectedCallback?.Dispose(); } catch (Exception error) { cleanupErrors.Add(error.ToString()); }
            try { collection?.CloseForCleanup(); } catch (Exception error) { cleanupErrors.Add(error.ToString()); }
            Volatile.Write(ref stop, 1);
            bool stopped = peerLoop is null;
            if (peerLoop is not null)
            {
                stopped = peerStopped.Wait(TimeSpan.FromSeconds(5));
                if (!stopped) cleanupErrors.Add("The peer did not leave its application loop.");
                else try { peerLoop.GetAwaiter().GetResult(); }
                    catch (Exception error)
                    {
                        peerFailure = error.ToString();
                        if (!invalidated || error.GetBaseException() is not InvalidDataException
                            || error.GetBaseException().Message != "The CLR probe lifetime is no longer admitted.")
                            cleanupErrors.Add(error.ToString());
                    }
            }
            if (scheduling is not null)
            {
                schedulerCapture = scheduling.Finish();
                cleanupErrors.AddRange(schedulerCapture.Failures);
            }
            if (client is not null)
                try { Control(client.BeginCleanup, CooperativeDispatchOperation.BeginCleanup); } catch (Exception error) { cleanupErrors.Add(error.ToString()); }
            if (client is not null && selectedRegistered)
            {
                try
                {
                    if (client.HasPending(1))
                    {
                        var deadline = Stopwatch.StartNew();
                        while (OnController(() => client.CleanupHeader(1), CooperativeDispatchOperation.SelectedCleanupHeader).Visibility < 3)
                        {
                            Require(deadline.Elapsed < TimeSpan.FromSeconds(2), "Selected cleanup did not retire the request.");
                            client.PollForCleanup(1);
                        }
                        Control(() => client.ReleaseAfterRetiredCopy(1), CooperativeDispatchOperation.SelectedCleanupRelease);
                        selectedCleanup = client.LastCleanup;
                    }
                    RetireTarget(1);
                    selectedRegistered = false;
                }
                catch (Exception error) { cleanupErrors.Add(error.ToString()); }
            }
            if (client is not null && worker is not null && registered && stopped)
            {
                try
                {
                    if (client.HasPending(0))
                    {
                        var deadline = Stopwatch.StartNew();
                        while (OnController(() => client.CleanupHeader(0), CooperativeDispatchOperation.PeerCleanupHeader).Visibility < 3)
                        {
                            Require(deadline.Elapsed < TimeSpan.FromSeconds(2), "Sampled cooperative cleanup did not retire the request.");
                            worker.Run(() => client.PollForCleanup(0));
                        }
                        Control(() => client.ReleaseAfterRetiredCopy(0), CooperativeDispatchOperation.PeerCleanupRelease);
                    }
                    worker.Run(() => RetireTarget(0));
                    registered = false;
                }
                catch (Exception error) { cleanupErrors.Add(error.ToString()); }
            }
            if (worker is not null && !registered)
                try { if (scheduling is null) joined = worker.Join(); else scheduling.Retire(() => joined = worker.Join()); } catch (Exception error) { cleanupErrors.Add(error.ToString()); }
            if (client is not null && joined && !selectedRegistered)
                try { Control(client.CloseAfterThreadJoin, CooperativeDispatchOperation.Close); closed = true; } catch (Exception error) { cleanupErrors.Add(error.ToString()); }
            if (controller is null) controllerJoined = true;
            else if (closed || client is null)
                try { if (scheduling is null) controllerJoined = controller.Join(); else scheduling.Retire(() => controllerJoined = controller.Join()); } catch (Exception error) { cleanupErrors.Add(error.ToString()); }
        }
        try
        {
            Require(joined && controllerJoined && closed && cleanupErrors.Count == 0,
                "The sampled cooperative run did not complete with clean source and native ownership.");
            if (invalidateAfterPayment || invalidateAfterSelectedPayment)
            {
                bool sourceRejected = false, paymentRejected = false;
                try { collection!.Complete(); } catch (InvalidDataException) { sourceRejected = true; }
                try { client!.Complete(); } catch (InvalidDataException) { paymentRejected = true; }
                Require(invalidated && rejectedAfterPayment && result is null && paid > 0 && payments.Count > 0
                    && (!invalidateAfterSelectedPayment || selectedPaid > 0 && selectedPayments.Count > 0
                        && pauses.Count > 0 && pauses[^1].Resumed is null)
                    && sourceRejected && paymentRejected && collection!.Cleanup is { CollectorJoined: true, SourceDisposed: true }
                    && collection.Cleanup.Errors.All(error => error.Stage == "collector" && error.Type == typeof(InvalidDataException).FullName),
                    "Invalidation after real payment did not reject both completions with retained cleanup evidence.");
                rejectionVerified = true;
            }
            else
            {
                Require(failure is null && result is not null, "The sampled source did not complete.");
                Require(peerBatches > 0 && peerChecksum == checked(peerBatches * 49995000L)
                    && peerCalls == checked(peerBatches * plan.CallsPerBatch), "The paying peer changed its work or injected probe count.");
                client!.Complete();
                completed = true;
            }
        }
        catch (Exception error) { verificationFailure = error.ToString(); }
        var bindingEvidence = client?.Binding;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            schemaVersion = cadencePlan is not null ? 4 : selectedPaymentEnabled ? 3 : 1,
            selectedCadence = cadencePlan?.Sampling.Cadence.ToString(), requestedBatches = cadencePlan?.Batches,
            selectedChecks = cadence?.Checks,
            selectedWorkStarted, selectedWorkFinished,
            timestampFrequency = Stopwatch.Frequency,
            percent = settings.Percent, passed = completed || rejectionVerified, completed, invalidateAfterPayment,
            invalidateAfterSelectedPayment,
            invalidated, rejectionVerified, rejectedAfterPayment, failure, failureBoundary, verificationFailure, peerFailure, cleanupErrors, joined, controllerJoined, closed,
            receiptIntervalCaptureVersion = 1, receiptIntervalRejection = receiptIntervals.Rejected,
            batchServiceEnabled = publication.BatchServiceEnabled, sampleServiceCalls = publication.ServiceCalls,
            dispatchTimingEnabled = dispatchTiming is not null, dispatchTiming,
            directReceiptReadyEnabled = receiptReadiness.DirectEnabled, receiptReadyCalls = receiptReadiness.Calls,
            peerProgressEnabled, peerOwner, peerOwnerFailure, peerSubmitProgress, peerDebtProgress,
            selectedHandoffEnabled = handoff.Enabled, selectedHandoffAttempts = handoff.Attempts,
            schedulerTiming = scheduling is not null, schedulerCapture,
            schedulerOwners = scheduling is null ? null : new[] { schedulerControllerOwner, selectedOwner, peerOwner },
            binding = bindingEvidence is null ? null : new { bindingEvidence.Generation, Module = (ulong)bindingEvidence.Module,
                Function = (ulong)bindingEvidence.Function, bindingEvidence.Method, bindingEvidence.Sites }, nativeRange = collection?.Map.Range,
            selectedBatches = batches, checksum, peerBatches, peerChecksum, peerCalls,
            credit, paid, selectedPeak, peerPeak, finalDebt, samples, payments,
            selectedPaid, selectedPayments, pauses, selectedFailureBoundary, selectedCleanup, selectedReceipt,
            selectedPending = selectedLease is null ? null : new { command = selectedCommand, generation = selectedGeneration,
                budget = selectedBudget, started = selectedStarted },
            pending = lease is null ? null : new { command, generation, budget = pendingBudget, started = pendingStarted },
            lastAccountingObservation = client?.LastAccountingObservation, nativeCleanup = client?.LastCleanup,
            sourceCleanup = collection?.Cleanup, initialCounters = collection?.InitialCounters, finalCounters = collection?.FinalCounters,
            sourceSamples = collection?.Cleanup is null ? null : collection.Diagnostics, batch = collection?.Latest,
            sourceEpochSamples = !selectedPaymentEnabled || collection?.Cleanup is null ? null : collection.EpochDiagnostics,
            selectedPaymentEnabled, pauseTimingEnabled, childCpuEnabled, ownerDrainEnabled, selectedPaymentSupported = false, predictionAccuracyEstablished = false, profilerCpuExcluded = false
        }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        return completed || rejectionVerified ? 0 : 1;
    }
}
