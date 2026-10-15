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
        long? CallbackFinished = null, long? Resumed = null, BoundSelectedAdmission? Admission = null);
    private sealed record FailureBoundary(Debt? LastObservedDebt, ulong Command, ulong Generation, long Budget,
        long RequestStarted, CooperativeFailureCapture Capture);

    public static int Run(string path, SampledSignalPlan settings, string library,
        Func<CpuSampleOwner> readOwner, Func<IInstructionMap, ICpuSampleSink, ICpuEventSession> createSource,
        bool invalidateAfterPayment = false,
        Func<IInstructionMap, ICpuSampleSink, IOwnerStoppedCpuEventSession>? selectedPaymentSource = null,
        bool invalidateAfterSelectedPayment = false, BoundSelectedCadencePlan? cadencePlan = null)
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

        T OnController<T>(Func<T> action) => controller is null ? action() : controller.Run(action);
        void Control(Action action) => OnController(() => { action(); return true; });

        Debt ObserveDebt()
        {
            var observed = accounting.Observe(state);
            var debt = new Debt(Stopwatch.GetTimestamp(), observed.Global, credit, paid,
                observed.Selected, observed.Peer, observed.SelectedLocal, observed.PeerLocal, selectedPaid);
            lastObservedDebt = debt;
            if (selectedPeak is null || debt.Selected > selectedPeak.Selected) selectedPeak = debt;
            if (peerPeak is null || debt.Peer > peerPeak.Peer) peerPeak = debt;
            if (debt.Selected > 2000000 || debt.Peer > 2000000)
                throw new InvalidDataException($"Sampled cooperative debt exceeded 2 ms: selected={debt.Selected}, peer={debt.Peer}.");
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
            Service();
        }

        void Publish(IReadOnlyList<CpuSample> incoming, int offset = 0)
        {
            for (int index = offset; index < incoming.Count; ++index) Process(incoming[index], 1);
        }

        void PublishEpochs(IReadOnlyList<BoundEpochSample> incoming, int offset = 0)
        {
            for (int index = offset; index < incoming.Count; ++index)
                Process(incoming[index].Sample, incoming[index].EligibilityEpoch);
        }

        void PaySelected(ulong version, uint point)
        {
            Require(client!.PollFromProbe(1, version, point) == CooperativePollOutcome.Idle,
                "The selected owner entered a probe with an unexpected native request.");
            if (point != 2) return;
            BoundSelectedAdmission? admission = null;
            if (cadence is not null)
            {
                if (collection!.LiveSamples > samples.Count)
                    PublishEpochs(collection.ReadEpochProvisional(samples.Count));
                Service();
                bool admitted = cadence.TryCheck(out var candidate);
                if (!admitted || ObserveDebt().Selected == 0) return;
                admission = candidate;
            }
            else if (accounting.Observe(state).Selected == 0) return;
            int pauseIndex = pauses.Count;
            pauses.Add(new(Stopwatch.GetTimestamp(), samples.Count, Admission: admission));
            collection!.ExecuteExcluded(drained =>
            {
                PublishEpochs(drained, samples.Count);
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
                    selectedGeneration = OnController(() => client.Submit(1, selectedCommand, checked((ulong)selectedBudget)));
                    selectedPending = new(CooperativeAccountingBinding.Parse(selectedLease, selectedGeneration, selectedCommand, selectedSite!));
                    while (OnController(() => client.CleanupHeader(1)).Visibility < 3)
                    {
                        Require(Stopwatch.GetElapsedTime(selectedStarted) <= TimeSpan.FromMilliseconds(100),
                            "A selected cooperative request exceeded 100 ms.");
                        Require(client.PollFromProbe(1, version, point) == CooperativePollOutcome.Serviced,
                            "The selected probe rejected cooperative service.");
                    }
                    Require(Stopwatch.GetElapsedTime(selectedStarted) <= TimeSpan.FromMilliseconds(100),
                        "A selected cooperative receipt exceeded 100 ms.");
                    var accepted = OnController(() => client.ReadAndAccept(1, selectedPending, state));
                    var interval = CooperativeReceiptInterval.Parse(selectedStarted, Stopwatch.GetTimestamp(), Stopwatch.Frequency);
                    selectedPending = accepted.Adapter;
                    state = accepted.State;
                    Require(selectedPending.IsTerminal, "The selected cooperative lease did not settle.");
                    selectedReceipt = client.LastAccountingObservation
                        ?? throw new InvalidDataException("The selected request has no retained receipt observation.");
                    Control(() => client.ReleaseAfterRetiredCopy(1));
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
            });
            pauses[pauseIndex] = pauses[pauseIndex] with { Resumed = Stopwatch.GetTimestamp() };
            cadence?.Resume();
        }

        void Service()
        {
            if (pending is not null)
            {
                Require(Stopwatch.GetElapsedTime(pendingStarted) <= TimeSpan.FromMilliseconds(100),
                    "A sampled cooperative request exceeded 100 ms.");
                if (OnController(() => client!.CleanupHeader(0)).Visibility == 3)
                {
                    var accepted = OnController(() => client!.ReadAndAccept(0, pending, state));
                    var interval = CooperativeReceiptInterval.Parse(pendingStarted, Stopwatch.GetTimestamp(), Stopwatch.Frequency);
                    Require(accepted.Adapter.IsTerminal, "A retired cooperative request did not settle its ledger lease.");
                    pending = accepted.Adapter;
                    state = accepted.State;
                    var observation = client!.LastAccountingObservation
                        ?? throw new InvalidDataException("The accepted request has no retained receipt observation.");
                    Require(observation.Raw.Header.Command == command
                        && observation.Raw.Header.Generation == generation && observation.Raw.Header.Budget == (ulong)pendingBudget,
                        "The retained receipt observation does not match the accepted request.");
                    long nextPaid = checked(paid + pending.ConfirmedNanoseconds);
                    Control(() => client.ReleaseAfterRetiredCopy(0));
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
                    generation = OnController(() => client!.Submit(0, command, checked((ulong)pendingBudget)));
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
            client = OnController(() => new BoundCooperativeClient(binding, selectedPaymentEnabled ? 2U : 1U));
            if (selectedPaymentEnabled)
            {
                selectedRegistered = true;
                selectedSite = client.RegisterCurrentThread(1, 1);
            }
            worker = new();
            registered = true;
            site = worker.Run(() => client.RegisterCurrentThread(0, 2));
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
            collection = new(new ClrProbeLifetime(binding), readOwner());
            if (selectedPaymentEnabled)
            {
                collection.StartPausable(selectedPaymentSource!);
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
                ? OnController(() => client.CaptureDiagnostic(0)) : null;
            var captures = selectedPaymentEnabled
                ? CooperativeFailureCapture.CaptureAll(error, readPeer,
                    () => client is not null && client.HasPending(1) ? OnController(() => client.CaptureDiagnostic(1)) : null)
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
            if (client is not null)
                try { Control(client.BeginCleanup); } catch (Exception error) { cleanupErrors.Add(error.ToString()); }
            if (client is not null && selectedRegistered)
            {
                try
                {
                    if (client.HasPending(1))
                    {
                        var deadline = Stopwatch.StartNew();
                        while (OnController(() => client.CleanupHeader(1)).Visibility < 3)
                        {
                            Require(deadline.Elapsed < TimeSpan.FromSeconds(2), "Selected cleanup did not retire the request.");
                            client.PollForCleanup(1);
                        }
                        Control(() => client.ReleaseAfterRetiredCopy(1));
                        selectedCleanup = client.LastCleanup;
                    }
                    client.RetireCurrentThread(1);
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
                        while (OnController(() => client.CleanupHeader(0)).Visibility < 3)
                        {
                            Require(deadline.Elapsed < TimeSpan.FromSeconds(2), "Sampled cooperative cleanup did not retire the request.");
                            worker.Run(() => client.PollForCleanup(0));
                        }
                        Control(() => client.ReleaseAfterRetiredCopy(0));
                    }
                    worker.Run(() => client.RetireCurrentThread(0));
                    registered = false;
                }
                catch (Exception error) { cleanupErrors.Add(error.ToString()); }
            }
            if (worker is not null && !registered)
                try { joined = worker.Join(); } catch (Exception error) { cleanupErrors.Add(error.ToString()); }
            if (client is not null && joined && !selectedRegistered)
                try { Control(client.CloseAfterThreadJoin); closed = true; } catch (Exception error) { cleanupErrors.Add(error.ToString()); }
            if (controller is null) controllerJoined = true;
            else if (closed || client is null)
                try { controllerJoined = controller.Join(); } catch (Exception error) { cleanupErrors.Add(error.ToString()); }
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
            selectedPaymentEnabled, selectedPaymentSupported = false, predictionAccuracyEstablished = false, profilerCpuExcluded = false
        }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        return completed || rejectionVerified ? 0 : 1;
    }
}
