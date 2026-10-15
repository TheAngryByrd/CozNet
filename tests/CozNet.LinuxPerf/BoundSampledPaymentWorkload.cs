using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using CozNet;
using Microsoft.FSharp.Core;
using static CooperativeManagedChecks;

static class BoundSampledPaymentWorkload
{
    private sealed record CreditedSample(int Index, CpuSample Sample, long Credit);
    private sealed record Payment(ulong Command, ulong Generation, long Budget, long Started,
        long Finished, CooperativeObservation Observation, CooperativeCleanupCapture Cleanup);
    private sealed record Debt(long Timestamp, long Global, long SelectedCredit, long PeerWait, long Selected, long Peer);
    private sealed record FailureBoundary(Debt? LastObservedDebt, ulong Command, ulong Generation, long Budget,
        long RequestStarted, CooperativeFailureCapture Capture);

    public static int Run(string path, SampledSignalPlan settings, string library,
        Func<CpuSampleOwner> readOwner, Func<IInstructionMap, ICpuSampleSink, ICpuEventSession> createSource,
        bool invalidateAfterPayment = false)
    {
        NativeLibrary.SetDllImportResolver(typeof(CooperativeNativeMethods).Assembly,
            (name, _, _) => name == "CozNet.Cooperative" ? NativeLibrary.Load(Path.GetFullPath(library)) : IntPtr.Zero);
        var plan = ProbePlan.Parse("injected", Environment.GetEnvironmentVariable("COZNET_PROBE_INTERVAL"));
        var selected = Parsed(SignalAccountingModule.register(FSharpOption<int>.Some(settings.Percent), SignalAccountingModule.create()));
        var peer = Parsed(SignalAccountingModule.register(FSharpOption<int>.None, selected.Item2));
        var state = peer.Item2;
        var stream = Parsed(SignalAccountingModule.sampleIdentity(selected.Item1, state));
        var samples = new List<CreditedSample>();
        var payments = new List<Payment>();
        var cleanupErrors = new List<string>();
        BoundSampleCollection? collection = null;
        BoundSampleResult? result = null;
        BoundCooperativeClient? client = null;
        CooperativeInteropChecks.Worker? worker = null;
        CooperativeSite? site = null;
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

        Debt ObserveDebt()
        {
            long global = SignalAccountingModule.globalNanoseconds(state);
            var debt = new Debt(Stopwatch.GetTimestamp(), global, credit, paid,
                Math.Max(0, global - credit), Math.Max(0, global - paid));
            lastObservedDebt = debt;
            if (selectedPeak is null || debt.Selected > selectedPeak.Selected) selectedPeak = debt;
            if (peerPeak is null || debt.Peer > peerPeak.Peer) peerPeak = debt;
            Require(debt.Selected <= 2000000 && debt.Peer <= 2000000,
                $"Sampled cooperative debt exceeded 2 ms: selected={debt.Selected}, peer={debt.Peer}.");
            return debt;
        }

        void Publish(IReadOnlyList<CpuSample> incoming)
        {
            foreach (var sample in incoming)
            {
                represented = checked(represented + sample.PeriodNanoseconds);
                var observation = Parsed(CpuSampleObservationModule.tryCreate(stream, checked((ulong)samples.Count + 1),
                    represented, 1, SampleThreadState.Runnable,
                    sample.Method == OverheadWorkload.SelectedSymbol ? SampleLocation.Selected : SampleLocation.Unmapped));
                var observed = Parsed(SignalAccountingModule.observe(selected.Item1, observation, state));
                state = observed.Item2;
                long amount = DelayTicksModule.value(observed.Item1);
                credit = checked(credit + amount);
                samples.Add(new(samples.Count, sample, amount));
                if (SignalAccountingModule.globalNanoseconds(state) <= credit)
                {
                    var prepared = Parsed(SignalAccountingModule.prepare(selected.Item1, state));
                    Require(prepared.Item1 is null, "The no-payment selected owner reserved a delay.");
                    state = prepared.Item2;
                }
                ObserveDebt();
                Service();
            }
        }

        void Service()
        {
            if (pending is not null)
            {
                Require(Stopwatch.GetElapsedTime(pendingStarted) <= TimeSpan.FromMilliseconds(100),
                    "A sampled cooperative request exceeded 100 ms.");
                if (client!.CleanupHeader(0).Visibility == 3)
                {
                    var accepted = client.ReadAndAccept(0, pending, state);
                    Require(accepted.Adapter.IsTerminal, "A retired cooperative request did not settle its ledger lease.");
                    pending = accepted.Adapter;
                    state = accepted.State;
                    var observation = client.LastAccountingObservation
                        ?? throw new InvalidDataException("The accepted request has no retained receipt observation.");
                    Require(observation.Raw.Header.Command == command
                        && observation.Raw.Header.Generation == generation && observation.Raw.Header.Budget == (ulong)pendingBudget,
                        "The retained receipt observation does not match the accepted request.");
                    long nextPaid = checked(paid + pending.ConfirmedNanoseconds);
                    client.ReleaseAfterRetiredCopy(0);
                    payments.Add(new(command, generation, pendingBudget, pendingStarted, Stopwatch.GetTimestamp(), observation, client.LastCleanup!));
                    paid = nextPaid;
                    pending = null;
                    lease = null;
                }
            }
            if (pending is null)
            {
                var prepared = Parsed(SignalAccountingModule.prepareService(peer.Item1, state));
                state = prepared.Item2;
                if (prepared.Item1 is not null)
                {
                    lease = prepared.Item1.Value;
                    pendingBudget = SignalServiceLeaseModule.nanoseconds(lease);
                    command = checked(command + 1);
                    pendingStarted = Stopwatch.GetTimestamp();
                    state = Parsed(SignalAccountingModule.sentService(lease, state));
                    generation = 0;
                    generation = client!.Submit(0, command, checked((ulong)pendingBudget));
                    pending = new(CooperativeAccountingBinding.Parse(lease, generation, command, site!));
                }
            }
            ObserveDebt();
        }

        try
        {
            Require(CpuWorkload.Run() == 49995000L, "Selected warmup changed its checksum.");
            var binding = ClrProbeBinding.ReadCurrent();
            client = new(binding);
            worker = new();
            registered = true;
            site = worker.Run(() => client.RegisterCurrentThread(0, 2));
            worker.Run(() => Require(CpuWorkload.Run() == 49995000L, "Peer warmup changed its checksum."));
            peerLoop = Task.Run(() => worker.Run(() =>
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
            }));
            Require(peerStarted.Wait(TimeSpan.FromSeconds(5)), "The paying peer did not start.");
            collection = new(new ClrProbeLifetime(binding), readOwner());
            collection.Start(createSource);
            long initialCalls = InjectedProbes.Calls;
            var elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed < TimeSpan.FromSeconds(2))
            {
                Require(!peerLoop.IsCompleted, "The paying peer stopped during sample collection.");
                checksum += CpuWorkload.Run();
                batches++;
                if (collection.LiveSamples > samples.Count) Publish(collection.ReadProvisional(samples.Count));
                Service();
                if (invalidateAfterPayment && payments.Count > 0)
                {
                    ProbeIdentityChecks.InvalidateWithDuplicateModule(plan);
                    invalidated = true;
                    collection.ReadProvisional(samples.Count);
                    throw new InvalidOperationException("The invalidated collection accepted a live read after payment.");
                }
            }
            Require(InjectedProbes.Calls - initialCalls == checked(batches * plan.CallsPerBatch),
                "The selected workload changed its injected probe count.");
            result = collection.Complete();
            Require(result.Samples.Take(samples.Count).SequenceEqual(samples.Select(item => item.Sample)),
                "The accepted sample source changed its provisional prefix.");
            Publish(result.Samples.Skip(samples.Count).ToArray());
            var drain = Stopwatch.StartNew();
            while (pending is not null || SignalAccountingModule.globalNanoseconds(state) > paid)
            {
                Require(drain.Elapsed < TimeSpan.FromMilliseconds(100), "The final sampled cooperative debt did not settle within 100 ms.");
                Service();
                Thread.Yield();
            }
            finalDebt = ObserveDebt();
            Require(samples.Count > 1 && result.Batch.SelectedMethodSamples > 0 && batches > 0 && checksum == checked(batches * 49995000L),
                "The sampled cooperative workload did not preserve selected work and sample reach.");
            UInt128 selectedPeriods = 0;
            foreach (var item in samples.Skip(1))
                if (item.Sample.Method == OverheadWorkload.SelectedSymbol) selectedPeriods += item.Sample.PeriodNanoseconds;
            Require((UInt128)credit == selectedPeriods * (uint)settings.Percent / 100,
                "Sample credits differ from selected periods after the epoch baseline.");
            Require(settings.Percent == 0 ? credit == 0 && paid == 0 && payments.Count == 0 : credit > 0 && paid >= credit && payments.Count > 0,
                "The sampled intervention did not reach its zero or positive payment condition.");
            Require(SignalAccountingModule.pendingCount(state) == 0, "The completed run retains a ledger reservation.");
        }
        catch (Exception error)
        {
            var capturedDebt = lastObservedDebt;
            var capture = CooperativeFailureCapture.Capture(error,
                () => client is not null && client.HasPending(0) ? client.CaptureDiagnostic(0) : null);
            failureBoundary = new(capturedDebt, command, generation, pendingBudget, pendingStarted, capture);
            failure = capture.Failure;
            rejectedAfterPayment = invalidated && error is InvalidDataException
                && error.GetBaseException().Message == "The CLR probe lifetime is no longer admitted.";
        }
        finally
        {
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
            if (client is not null && worker is not null && registered && stopped)
            {
                try
                {
                    client.BeginCleanup();
                    if (client.HasPending(0))
                    {
                        var deadline = Stopwatch.StartNew();
                        while (client.CleanupHeader(0).Visibility < 3)
                        {
                            Require(deadline.Elapsed < TimeSpan.FromSeconds(2), "Sampled cooperative cleanup did not retire the request.");
                            worker.Run(() => client.PollForCleanup(0));
                        }
                        client.ReleaseAfterRetiredCopy(0);
                    }
                    worker.Run(() => client.RetireCurrentThread(0));
                    registered = false;
                }
                catch (Exception error) { cleanupErrors.Add(error.ToString()); }
            }
            if (worker is not null && !registered)
                try { joined = worker.Join(); } catch (Exception error) { cleanupErrors.Add(error.ToString()); }
            if (client is not null && joined)
                try { client.CloseAfterThreadJoin(); closed = true; } catch (Exception error) { cleanupErrors.Add(error.ToString()); }
        }
        try
        {
            Require(joined && closed && cleanupErrors.Count == 0,
                "The sampled cooperative run did not complete with clean source and native ownership.");
            if (invalidateAfterPayment)
            {
                bool sourceRejected = false, paymentRejected = false;
                try { collection!.Complete(); } catch (InvalidDataException) { sourceRejected = true; }
                try { client!.Complete(); } catch (InvalidDataException) { paymentRejected = true; }
                Require(invalidated && rejectedAfterPayment && result is null && paid > 0 && payments.Count > 0
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
            percent = settings.Percent, passed = completed || rejectionVerified, completed, invalidateAfterPayment,
            invalidated, rejectionVerified, rejectedAfterPayment, failure, failureBoundary, verificationFailure, peerFailure, cleanupErrors, joined, closed,
            binding = bindingEvidence is null ? null : new { bindingEvidence.Generation, Module = (ulong)bindingEvidence.Module,
                Function = (ulong)bindingEvidence.Function, bindingEvidence.Method, bindingEvidence.Sites }, nativeRange = collection?.Map.Range,
            selectedBatches = batches, checksum, peerBatches, peerChecksum, peerCalls,
            credit, paid, selectedPeak, peerPeak, finalDebt, samples, payments,
            pending = lease is null ? null : new { command, generation, budget = pendingBudget, started = pendingStarted },
            lastAccountingObservation = client?.LastAccountingObservation, nativeCleanup = client?.LastCleanup,
            sourceCleanup = collection?.Cleanup, initialCounters = collection?.InitialCounters, finalCounters = collection?.FinalCounters,
            sourceSamples = collection?.Cleanup is null ? null : collection.Diagnostics, batch = collection?.Latest,
            selectedPaymentSupported = false, predictionAccuracyEstablished = false, profilerCpuExcluded = false
        }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        return completed || rejectionVerified ? 0 : 1;
    }
}
