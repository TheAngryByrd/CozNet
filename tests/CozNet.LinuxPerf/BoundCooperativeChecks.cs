using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using CozNet;
using static CooperativeManagedChecks;

static class BoundCooperativeChecks
{
    private static void Reject(BoundCooperativeClient client, Action action)
    {
        bool rejected = false;
        try { action(); } catch (Exception error) when (error.GetBaseException() is InvalidDataException) { rejected = true; }
        Require(rejected && client.FirstFailure is not null, "The invalid CLR lifetime did not reject the bound operation.");
    }
    private static long Pay(BoundCooperativeClient client, ProbePlan plan)
    {
        long before = InjectedProbes.Calls, servicedAt = 0;
        using (InjectedProbes.BindBound((generation, site) =>
        {
            if (site == 1)
                Require(client.PollFromProbe(0, generation, site) == CooperativePollOutcome.Idle,
                    "An entry probe serviced a loop-site lease.");
            else if (servicedAt == 0)
            {
                Require(client.PollFromProbe(0, generation, site) == CooperativePollOutcome.Serviced,
                    "The first loop probe did not service the bound lease.");
                servicedAt = InjectedProbes.Calls - before;
            }
        }))
            Require(CpuWorkload.Run() == (long)CpuWorkload.Iterations * (CpuWorkload.Iterations - 1) / 2,
                "Bound payment changed the workload checksum.");
        InjectedProbes.VerifyRewrite(plan, InjectedProbes.Calls - before);
        Require(InjectedProbes.ReadBoundObservation()[3] == 0, "The bound workload observed inconsistent injected literals.");
        Require(servicedAt == 2, "Bound payment did not originate at the first loop probe.");
        return servicedAt;
    }
    public static int Run(string path, string mode, string library)
    {
        if (mode is not ("paid" or "before-submit" or "before-poll" or "before-receipt" or "after-settlement"))
            throw new ArgumentException("Select paid, before-submit, before-poll, before-receipt, or after-settlement.");
        NativeLibrary.SetDllImportResolver(typeof(CooperativeNativeMethods).Assembly,
            (name, _, _) => name == "CozNet.Cooperative" ? NativeLibrary.Load(Path.GetFullPath(library)) : IntPtr.Zero);
        var plan = ProbePlan.Parse("injected", Environment.GetEnvironmentVariable("COZNET_PROBE_INTERVAL"));
        CooperativeInteropChecks.Worker? worker = null;
        BoundCooperativeClient? client = null;
        ClrProbeBinding? binding = null;
        bool registered = false, joined = false, closed = false, completed = false, bodyPassed = false;
        bool settled = false, reservationRetained = false, rejected = false, reuse = false;
        string? failure = null;
        var cleanupFailures = new List<string>();
        var serviceProbes = new List<long>();
        CooperativeCleanupCapture? firstCleanup = null;
        try
        {
            worker = new();
            worker.Run(() =>
            {
                long before = InjectedProbes.Calls;
                Require(CpuWorkload.Run() == (long)CpuWorkload.Iterations * (CpuWorkload.Iterations - 1) / 2,
                    "Bound warmup changed the workload checksum.");
                InjectedProbes.VerifyRewrite(plan, InjectedProbes.Calls - before);
            });
            binding = ClrProbeBinding.ReadCurrent();
            Require(binding.Method == (uint)typeof(CpuWorkload).GetMethod(nameof(CpuWorkload.Run))!.MetadataToken,
                "The admitted CLR binding identifies another method.");
            var altered = new[]
            {
                ClrProbeBinding.Parse(binding.Generation + 1, binding.Module, binding.Function, binding.Method, binding.Sites),
                ClrProbeBinding.Parse(binding.Generation, binding.Module + 1, binding.Function, binding.Method, binding.Sites),
                ClrProbeBinding.Parse(binding.Generation, binding.Module, binding.Function + 1, binding.Method, binding.Sites),
                ClrProbeBinding.Parse(binding.Generation, binding.Module, binding.Function,
                    binding.Method == 0x06000001 ? 0x06000002U : 0x06000001U, binding.Sites)
            };
            foreach (var foreign in altered)
            {
                bool foreignRejected = false;
                var guard = new ClrProbeLifetime(foreign);
                try { guard.RequireCurrent(); } catch (InvalidDataException) { foreignRejected = true; }
                Require(foreignRejected && guard.FirstFailure is not null, "An altered CLR binding passed the native identity comparison.");
            }
            client = new(binding, capacity: 8);
            registered = true;
            var site = worker.Run(() => client.RegisterCurrentThread(0, 1));
            worker.Run(() =>
            {
                using var callback = InjectedProbes.BindBound((generation, point) =>
                    Require(client.PollFromProbe(0, generation, point) == CooperativePollOutcome.Idle, "A bound idle probe performed work."));
                CpuWorkload.Run();
            });
            var fixture = Reserve(100000);
            var state = fixture.State;
            if (mode == "before-submit")
            {
                ProbeIdentityChecks.InvalidateWithDuplicateModule(plan);
                Reject(client, () => client.Submit(0, 7, 50000));
                Require(!client.HasPending(0), "Rejected admission created native ownership.");
                rejected = true;
            }
            else
            {
                ulong generation = client.Submit(0, 7, 50000);
                var adapter = new CooperativeAccountingAdapter(CooperativeAccountingBinding.Parse(fixture.Lease, generation, 7, site));
                if (mode == "before-poll")
                {
                    ProbeIdentityChecks.InvalidateWithDuplicateModule(plan);
                    Reject(client, () => worker.Run(() => Pay(client, plan)));
                    Require(client.CleanupHeader(0).Children == 0, "Rejected probe admission created a child.");
                    rejected = true;
                }
                else
                {
                    serviceProbes.Add(worker.Run(() => Pay(client, plan)));
                    if (mode == "before-receipt")
                    {
                        ProbeIdentityChecks.InvalidateWithDuplicateModule(plan);
                        Reject(client, () => client.ReadAndAccept(0, adapter, state));
                        rejected = true;
                    }
                    else
                    {
                        (adapter, state) = client.ReadAndAccept(0, adapter, state);
                        settled = adapter.IsTerminal && Remaining(fixture, state) == 0 && SignalAccountingModule.pendingCount(state) == 0;
                        Require(settled, "The bound journal did not settle its ledger lease.");
                        if (mode == "after-settlement") ProbeIdentityChecks.InvalidateWithDuplicateModule(plan);
                        else
                        {
                            client.ReleaseAfterRetiredCopy(0);
                            firstCleanup = client.LastCleanup;
                            var independent = Reserve(100000);
                            ulong next = client.Submit(0, 8, 50000);
                            serviceProbes.Add(worker.Run(() => Pay(client, plan)));
                            var again = new CooperativeAccountingAdapter(CooperativeAccountingBinding.Parse(independent.Lease, next, 8, site));
                            var accepted = client.ReadAndAccept(0, again, independent.State);
                            reuse = next == generation + 1 && accepted.Adapter.IsTerminal && Remaining(independent, accepted.State) == 0;
                            Require(reuse, "A second bound generation failed independent settlement.");
                        }
                    }
                }
            }
            if (rejected)
            {
                reservationRetained = SignalAccountingModule.pendingCount(state) == 1
                    && Parsed(SignalAccountingModule.confirmedServiceNanoseconds(fixture.Lease, state)) == 0;
                Require(reservationRetained, "Rejected lifetime evidence changed the ledger reservation.");
                var first = client.FirstFailure;
                Reject(client, () => client.Submit(0, 9, 50000));
                Require(ReferenceEquals(first, client.FirstFailure), "A later operation replaced the first lifetime failure.");
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
                    client.BeginCleanup();
                    if (client.HasPending(0))
                    {
                        var deadline = Stopwatch.StartNew();
                        while (client.CleanupHeader(0).Visibility < 3)
                        {
                            Require(deadline.Elapsed < TimeSpan.FromSeconds(2), "Bound cleanup did not terminate the native request.");
                            worker.Run(() => client.PollForCleanup(0));
                        }
                        client.ReleaseAfterRetiredCopy(0);
                    }
                    worker.Run(() => client.RetireCurrentThread(0));
                    registered = false;
                }
                catch (Exception error) { cleanupFailures.Add(error.ToString()); }
            }
            if (worker is not null && !registered)
                try { joined = worker.Join(); } catch (Exception error) { cleanupFailures.Add(error.ToString()); }
            if (client is not null && !registered && joined)
                try { client.CloseAfterThreadJoin(); closed = true; } catch (Exception error) { cleanupFailures.Add(error.ToString()); }
        }
        if (bodyPassed && client is not null && closed)
        {
            try
            {
                if (mode == "paid") { client.Complete(); completed = true; }
                else { Reject(client, client.Complete); rejected = true; }
            }
            catch (Exception error) { failure = error.ToString(); bodyPassed = false; }
        }
        bool cleanupVerified = mode switch
        {
            "before-submit" => client?.LastCleanup is null,
            "before-poll" => client?.LastCleanup is { Status: 0 } cancelled && cancelled.Header.Stop == 3
                && cancelled.Header.Measured == 0 && cancelled.Children.Count == 1 && cancelled.Children[0].Waited == 0,
            _ => client?.LastCleanup is { Status: 0 } paid && paid.Header.Measured >= 50000 && paid.Children.Count == 1
                && paid.Children[0].CodeVersion == binding?.Generation && paid.Children[0].Site == 2
        };
        bool passed = bodyPassed && joined && closed && cleanupFailures.Count == 0 && cleanupVerified
            && (mode == "paid" ? completed && reuse && !rejected : rejected && !completed);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { mode, passed, failure, cleanupFailures, joined, closed, completed,
            settled, rejected, reservationRetained, reuse, serviceProbes, firstCleanup, cleanupVerified, cleanup = client?.LastCleanup,
            lifetimeFailure = client?.FirstFailure?.ToString(),
            binding = binding is null ? null : new { binding.Generation, Module = (ulong)binding.Module,
                Function = (ulong)binding.Function, binding.Method, binding.Sites },
            paymentLifetimeIntegrated = true, codePinned = false, sampledCapacityEstablished = false, predictionAccuracyEstablished = false },
            new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        return passed ? 0 : 1;
    }
}
