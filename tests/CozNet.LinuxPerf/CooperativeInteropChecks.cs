using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using CozNet;
using static CooperativeManagedChecks;

static class CooperativeInteropChecks
{
    internal sealed class Worker
    {
        private readonly BlockingCollection<Action> tasks = new();
        private readonly Thread thread;
        public Worker()
        {
            thread = new Thread(() => { foreach (var action in tasks.GetConsumingEnumerable()) action(); }) { IsBackground = true };
            thread.Start();
        }
        public Task<T> Start<T>(Func<T> action)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            tasks.Add(() => { try { completion.SetResult(action()); } catch (Exception error) { completion.SetException(error); } });
            return completion.Task;
        }
        public Task Start(Action action) => Start(() => { action(); return true; });
        public T Run<T>(Func<T> action)
        {
            var completion = Start(action);
            if (!completion.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("The cooperative owner did not finish its command.");
            return completion.GetAwaiter().GetResult();
        }
        public void Run(Action action) => Run(() => { action(); return true; });
        public bool Join()
        {
            tasks.CompleteAdding();
            if (!thread.Join(TimeSpan.FromSeconds(5))) return false;
            tasks.Dispose();
            return true;
        }
    }
    public static int Run(string path, string mode, string library)
    {
        if (mode is not ("paid" or "deferred" or "cancelled" or "late" or "clock-failure" or "active-cleanup" or "injected-paid" or "diagnostic" or "cpu"))
            throw new ArgumentException("Select paid, deferred, cancelled, late, clock-failure, active-cleanup, injected-paid, diagnostic, or cpu.");
        bool injected = mode == "injected-paid";
        bool diagnostic = mode is "diagnostic" or "cpu";
        bool pays = mode is "paid" or "injected-paid" or "diagnostic" or "cpu";
        ProbePlan? plan = injected ? ProbePlan.Parse("injected", Environment.GetEnvironmentVariable("COZNET_PROBE_INTERVAL")) : null;
        var injectedPayments = new List<InjectedPayment>();
        NativeLibrary.SetDllImportResolver(typeof(CooperativeNativeMethods).Assembly,
            (name, _, _) => name == "CozNet.Cooperative" ? NativeLibrary.Load(Path.GetFullPath(library)) : IntPtr.Zero);
        NativeCooperativeClient? client = null;
        Worker? worker = null;
        bool registered = false, joined = false, closed = false, bodyPassed = false, rejected = false, firstPreserved = false;
        bool settled = false, reservationRetained = false;
        bool reuseVerified = false;
        CooperativeCleanupCapture? firstCleanup = null;
        long? unpaid = null;
        CooperativeRawCapture? firstRaw = null;
        CooperativeRawCapture? activeDiagnostic = null, retiredDiagnostic = null;
        string? failure = null;
        var cleanupFailures = new List<string>();
        try
        {
            client = new(2, capacity: 8, observeCpu: mode == "cpu");
            worker = new();
            registered = true;
            worker.Run(() => client.RegisterCurrentThread(0, Site));
            Require(worker.Run(() => client.Poll(0, Site)) == CooperativePollOutcome.Idle, "A registered idle owner performed work.");
            if (injected)
                worker.Run(() =>
                {
                    long before = InjectedProbes.Calls;
                    Require(CpuWorkload.Run() == (long)CpuWorkload.Iterations * (CpuWorkload.Iterations - 1) / 2,
                        "Injected warmup changed the workload result.");
                    InjectedProbes.VerifyRewrite(plan!, InjectedProbes.Calls - before);
                    using var binding = InjectedProbes.Bind(() => Require(client.Poll(0, Site) == CooperativePollOutcome.Idle,
                        "An injected warmup probe performed work without a lease."));
                    CpuWorkload.Run();
                });
            var fixture = Reserve(100000);
            ulong generation = client.Submit(0, 7, 50000);
            if (diagnostic)
            {
                activeDiagnostic = client.CaptureDiagnostic(0);
                Require(activeDiagnostic.Status == 0 && activeDiagnostic.Header.Visibility == 0
                    && activeDiagnostic.Header.Children == 0 && activeDiagnostic.Children.Count == 0
                    && activeDiagnostic.Clock.Status == 0 && activeDiagnostic.Clock.Nanoseconds >= activeDiagnostic.Header.Captured
                    && client.Receipts(0).LastRaw is null && client.Receipts(0).FirstFailure is null && client.HasPending(0),
                    "An active diagnostic changed receipt tracking or native ownership.");
            }
            var adapter = Adapter(fixture, generation);
            var state = fixture.State;
            if (mode == "cancelled")
            {
                var requested = adapter.RequestCancellation(state);
                (adapter, state) = (requested.Adapter, requested.State);
                client.Cancel(0);
            }
            if (mode == "active-cleanup")
            {
                var pending = client.Probe(0);
                Require(pending.Visibility == 0 && pending.Children == 0 && client.HasPending(0), "The cleanup case must retain an unclaimed request.");
                reservationRetained = SignalAccountingModule.pendingCount(state) == 1
                    && Parsed(SignalAccountingModule.confirmedServiceNanoseconds(fixture.Lease, state)) == 0;
                Require(reservationRetained, "The pending cleanup case lost its ledger reservation.");
            }
            else
            {
                var observed = mode == "deferred" ? CooperativeSite.Parse(1, 2, 4) : Site;
                var outcome = worker.Run(() =>
                {
                    if (injected)
                    {
                        var payment = PayFromInjectedLoop(client, plan!);
                        injectedPayments.Add(payment);
                        return payment.Outcome;
                    }
                    ulong value = 1;
                    for (uint index = 0; index < 1024; ++index) value = unchecked(value * 33 + index);
                    GC.KeepAlive(value);
                    return client.Poll(0, observed);
                });
                Require(outcome == CooperativePollOutcome.Serviced, "The cooperative owner failed its bounded child.");
                var header = client.Probe(0);
                Require(header.Visibility == 3 && header.Children == 1 && header.Error == 0,
                    "The controlled cooperative child did not produce a valid retired journal.");
                if (diagnostic)
                {
                    retiredDiagnostic = client.CaptureDiagnostic(0);
                    Require(retiredDiagnostic.Status == 0 && retiredDiagnostic.Header.Visibility == 3
                        && retiredDiagnostic.Children.Count == 1 && retiredDiagnostic.Children[0].Waited >= 50000
                        && client.Receipts(0).LastRaw is null && client.Receipts(0).FirstFailure is null && client.HasPending(0),
                        "A retired diagnostic accepted a receipt or released ownership.");
                }
                if (mode == "late") Thread.Sleep(150);
                CooperativeObservation? observation = null;
                try
                {
                    observation = client.Read(0, mode == "clock-failure"
                        ? handle => Task.Run(() => NativeCooperativeClient.ReadClock(handle)).GetAwaiter().GetResult() : null);
                }
                catch (InvalidDataException) { rejected = true; }
                firstRaw = client.Receipts(0).LastRaw;
                Require(firstRaw is not null && firstRaw.Children.Count == 1, "The first cooperative raw capture was lost.");
                Require(rejected == (mode is "late" or "clock-failure"), "The cooperative receipt result differs from the selected case.");
                if (!rejected)
                {
                    var accepted = adapter.Accept(observation!, state);
                    settled = accepted.Adapter.IsTerminal;
                    unpaid = Remaining(fixture, accepted.State);
                    Require(settled && SignalAccountingModule.pendingCount(accepted.State) == 0
                        && unpaid == (pays ? 0 : 50000), "Cooperative ledger settlement differs from actual payment.");
                    Require(pays ? firstRaw!.Header.Measured >= 50000 : firstRaw!.Header.Measured == 0,
                        "The cooperative journal contains unexpected payment.");
                    ulong firstUpper = observation!.Received[0].FirstManagedReceiptUpper;
                    Thread.Sleep(150);
                    var later = client.Read(0);
                    firstPreserved = later.Received[0].FirstManagedReceiptUpper == firstUpper
                        && later.Raw.Clock.Nanoseconds - later.Raw.Children[0].Armed > 100000000;
                    Require(firstPreserved, "A later read replaced or rejected the first cooperative receipt.");
                }
                else
                {
                    var original = client.Receipts(0).FirstFailure;
                    Require(original is not null && (mode == "late"
                        ? firstRaw!.Clock.Status == 0 && firstRaw.Clock.Nanoseconds - firstRaw.Children[0].Armed > 100000000
                        : firstRaw!.Clock.Status == 2 && firstRaw.Clock.Nanoseconds == 0), "The receipt failed at the wrong boundary.");
                    bool stillRejected = false;
                    try { client.Read(0); } catch (InvalidDataException) { stillRejected = true; }
                    Require(stillRejected && ReferenceEquals(original, client.Receipts(0).FirstFailure), "A later read repaired the failed receipt.");
                    reservationRetained = SignalAccountingModule.pendingCount(state) == 1
                        && Parsed(SignalAccountingModule.confirmedServiceNanoseconds(fixture.Lease, state)) == 0;
                    Require(reservationRetained && !adapter.IsTerminal, "Failed receipt evidence released the ledger reservation.");
                }
                var oldTracker = client.Receipts(0);
                var oldFailure = oldTracker.FirstFailure;
                client.ReleaseAfterRetiredCopy(0);
                firstCleanup = client.LastCleanup ?? throw new InvalidDataException("The first cleanup capture is missing.");
            if (mode == "cpu")
            {
                Require(activeDiagnostic!.Cpu is null, "An active request has a retired CPU snapshot.");
                var cpu = firstRaw!.Cpu ?? throw new InvalidDataException("The retired receipt lost its CPU capture.");
                Require(cpu.Slot == 0 && cpu.Generation == generation && cpu.Status == 0 && cpu.Failure is null
                    && cpu.Required == 1 && cpu.Children.Count == 1, "CPU capture identity or count differs.");
                var child = cpu.Children[0];
                Require(child.Generation == generation && child.Command == 7 && child.Ordinal == 1
                    && child.Worker == Site.Worker && child.CodeVersion == Site.CodeVersion && child.Site == Site.Site
                    && child.Requested == 50000, "CPU child identity differs from its service request.");
                ulong expectedStatus = OperatingSystem.IsLinux() ? 1UL : 0UL;
                Require(child.Before.Status == expectedStatus && child.After.Status == expectedStatus,
                    "The CPU clock capability differs from the platform contract.");
                Require(firstCleanup.Cpu is { Status: 0 } cleaned && cleaned.Children.SequenceEqual(cpu.Children)
                    && retiredDiagnostic!.Cpu is { Status: 0 } captured && captured.Children.SequenceEqual(cpu.Children),
                    "Cleanup or diagnostic capture changed CPU observations.");
            }
                Require(firstCleanup is { Status: 0 } && firstCleanup.Header.Generation == generation
                    && firstCleanup.Header.Children == (ulong)firstCleanup.Children.Count
                    && firstCleanup.Children.SequenceEqual(firstRaw!.Children), "Cleanup did not retain the complete first journal.");
                var independent = Reserve(100000);
                ulong nextGeneration = client.Submit(0, 8, 50000);
                Require(nextGeneration == generation + 1 && !ReferenceEquals(oldTracker, client.Receipts(0))
                    && client.Receipts(0).FirstFailure is null && client.Receipts(0).LastRaw is null,
                    "A reused slot retained the prior tracker identity or failure.");
                Require(worker.Run(() =>
                {
                    if (!injected) return client.Poll(0, Site);
                    var payment = PayFromInjectedLoop(client, plan!);
                    injectedPayments.Add(payment);
                    return payment.Outcome;
                }) == CooperativePollOutcome.Serviced, "The reused target failed its child.");
                var nextObservation = client.Read(0);
                var nextSettlement = Adapter(independent, nextGeneration, 8).Accept(nextObservation, independent.State);
                reuseVerified = nextSettlement.Adapter.IsTerminal && Remaining(independent, nextSettlement.State) == 0
                    && nextObservation.Raw.Header.Generation == nextGeneration && nextObservation.Raw.Header.Command == 8
                    && nextObservation.Received[0].FirstManagedReceiptUpper > firstRaw!.Clock.Nanoseconds
                    && ReferenceEquals(oldFailure, oldTracker.FirstFailure)
                    && firstCleanup.Children.SequenceEqual(firstRaw.Children);
                Require(reuseVerified, "A new independent lease did not reset receipt tracking or preserve the old evidence.");
            if (mode == "cpu")
                Require(nextObservation.Raw.Cpu is { Status: 0, Required: 1 } nextCpu && nextCpu.Generation == nextGeneration
                    && nextCpu.Children.Count == 1 && nextCpu.Children[0].Generation == nextGeneration
                    && nextCpu.Children[0].Command == 8 && nextCpu.Children[0].Ordinal == 1,
                    "A reused generation retained stale CPU records.");
                if (diagnostic)
                    Require(activeDiagnostic!.Header.Visibility == 0 && activeDiagnostic.Children.Count == 0
                        && retiredDiagnostic!.Header.Generation == generation
                        && retiredDiagnostic.Children.SequenceEqual(firstCleanup.Children),
                        "Native reuse changed a retained diagnostic snapshot.");
                if (rejected)
                    Require(SignalAccountingModule.pendingCount(state) == 1
                        && Parsed(SignalAccountingModule.confirmedServiceNanoseconds(fixture.Lease, state)) == 0,
                        "Reusing native storage repaired the rejected experiment ledger.");
                }
            bodyPassed = true;
        }
        catch (Exception error) { failure = error.ToString(); }
        finally
        {
            if (client is not null && worker is not null && registered)
            {
                try
                {
                    if (client.HasPending(0))
                    {
                        if (client.Probe(0).Visibility < 3)
                        {
                            client.Cancel(0);
                            var deadline = Stopwatch.StartNew();
                            while (client.Probe(0).Visibility < 3)
                            {
                                Require(deadline.Elapsed < TimeSpan.FromSeconds(2), "Cooperative cleanup did not terminate its request.");
                                worker.Run(() => client.Poll(0, Site));
                            }
                        }
                        client.ReleaseAfterRetiredCopy(0);
                    }
                    worker.Run(() => client.RetireCurrentThread(0));
                    registered = false;
                }
                catch (Exception error) { cleanupFailures.Add(error.ToString()); }
            }
            if (worker is not null && !registered)
            {
                try { joined = worker.Join(); } catch (Exception error) { cleanupFailures.Add(error.ToString()); }
            }
            if (client is not null && !registered && (worker is null || joined))
            {
                try { client.CloseAfterThreadJoin(); closed = true; } catch (Exception error) { cleanupFailures.Add(error.ToString()); }
            }
        }
        bool activeCleanupVerified = mode != "active-cleanup" || (client?.LastCleanup is { Status: 0 } cleanup
            && cleanup.Header.Visibility == 3 && cleanup.Header.Stop == 3 && cleanup.Header.Children == 1
            && cleanup.Header.Measured == 0 && cleanup.Children.Count == 1 && cleanup.Children[0].Waited == 0
            && cleanup.Children[0].Valid == 1 && !client.HasPending(0));
        bool passed = bodyPassed && joined && closed && cleanupFailures.Count == 0 && activeCleanupVerified;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { mode, passed, failure, cleanupFailures, joined, closed, rejected,
            firstPreserved, settled, reservationRetained, unpaid, firstRaw, activeDiagnostic, retiredDiagnostic, reuseVerified, firstCleanup, activeCleanupVerified, cleanup = client?.LastCleanup,
            injectedPayments, probeInterval = plan?.BackedgeInterval, clrCodeLifetimeEstablished = false,
            sampledCapacityEstablished = false, generalClrSafetyEstablished = false, predictionAccuracyEstablished = false },
            new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        return passed ? 0 : 1;
    }

    private sealed record InjectedPayment(long Calls, long ServiceProbe, CooperativePollOutcome Outcome);

    private static InjectedPayment PayFromInjectedLoop(NativeCooperativeClient client, ProbePlan plan)
    {
        long before = InjectedProbes.Calls;
        long serviceProbe = 0;
        var outcome = CooperativePollOutcome.Idle;
        using (InjectedProbes.Bind(() =>
        {
            long ordinal = InjectedProbes.Calls - before;
            if (ordinal != 2) return;
            serviceProbe = ordinal;
            outcome = client.Poll(0, Site);
        }))
        {
            Require(CpuWorkload.Run() == (long)CpuWorkload.Iterations * (CpuWorkload.Iterations - 1) / 2,
                "Cooperative payment changed the injected workload result.");
        }
        long calls = InjectedProbes.Calls - before;
        InjectedProbes.VerifyRewrite(plan, calls);
        Require(serviceProbe == 2 && outcome == CooperativePollOutcome.Serviced,
            "A loop probe must service the lease after the entry probe returns.");
        return new(calls, serviceProbe, outcome);
    }
}
