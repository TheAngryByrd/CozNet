using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using static CooperativeManagedChecks;

static class BoundSamplesWorkload
{
    public static int Run(string path, string mode)
    {
        if (mode is not ("collect" or "before-enable" or "during" or "after-close"))
            throw new ArgumentException("Select collect, before-enable, during, or after-close.");
        BoundSampleCollection? collection = null;
        BoundSampleResult? result = null;
        ClrProbeLifetime? lifetime = null;
        BoundSampleCleanup? cleanup = null;
        bool invalidated = false, rejected = false, passed = false;
        string? failure = null, verificationFailure = null;
        long batches = 0, checksum = 0, calls = 0;
        try
        {
            if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
                throw new PlatformNotSupportedException("Live bound CPU samples require Linux x64.");
            var plan = ProbePlan.Parse("injected", Environment.GetEnvironmentVariable("COZNET_PROBE_INTERVAL"));
            Require(CpuWorkload.Run() == 49995000L, "The bound sample warmup changed its checksum.");
            lifetime = new(ClrProbeBinding.ReadCurrent());
            var owner = CpuSampleOwner.Parse((uint)Environment.ProcessId, checked((uint)Native.GetTid(186)));
            collection = new(lifetime, owner);
            collection.Start((map, sink) =>
            {
                var source = new LinuxCpuEventSession(sink, map);
                if (mode == "before-enable")
                {
                    try { ProbeIdentityChecks.InvalidateWithDuplicateModule(plan); invalidated = true; }
                    catch { source.Dispose(); throw; }
                }
                return source;
            });
            long initialCalls = InjectedProbes.Calls;
            var elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed < TimeSpan.FromSeconds(2))
            {
                checksum += CpuWorkload.Run();
                batches++;
                if (mode == "during" && collection.LiveSamples > 0 && collection.Latest?.SelectedMethodSamples > 0)
                {
                    ProbeIdentityChecks.InvalidateWithDuplicateModule(plan);
                    invalidated = true;
                    break;
                }
            }
            calls = InjectedProbes.Calls - initialCalls;
            if (mode == "after-close")
            {
                collection.CloseForCleanup();
                ProbeIdentityChecks.InvalidateWithDuplicateModule(plan);
                invalidated = true;
            }
            result = collection.Complete();
        }
        catch (Exception error)
        {
            failure = error.ToString();
            rejected = invalidated && error is InvalidDataException
                && lifetime?.FirstFailure?.Message == "The CLR probe lifetime is no longer admitted.";
        }
        finally { cleanup = collection?.CloseForCleanup(); }
        try
        {
            Require(collection is not null && cleanup is { SourceCreated: true, DisableSucceeded: true, CollectorJoined: true, SourceDisposed: true },
                "The bound sample source did not preserve cleanup ownership.");
            Require(cleanup!.Errors.All(error => mode == "during" && error.Stage == "collector"
                && error.Type == typeof(InvalidDataException).FullName), "The bound sample cleanup contains an unexpected failure.");
            if (mode == "collect")
            {
                Require(result is not null && failure is null && result.Batch.Samples > 0 && result.Batch.SelectedMethodSamples > 0
                    && result.CpuNanoseconds > 0 && collection!.LiveSamples > 0, "No real CPU samples reached the admitted bound map.");
            }
            else
            {
                Require(invalidated && rejected && result is null, "CLR invalidation did not reject sample completion.");
                if (mode == "before-enable")
                    Require(collection!.Diagnostics.Count == 0 && collection.FinalCounters is { Epoch: 0, Enabled: false },
                        "The source enabled or accepted samples after pre-enable invalidation.");
                else Require(collection!.Diagnostics.Any(sample => sample.Method == OverheadWorkload.SelectedSymbol),
                    "The invalidation case did not retain a previously mapped real sample.");
            }
            if (mode != "before-enable")
            {
                var plan = ProbePlan.Parse("injected", Environment.GetEnvironmentVariable("COZNET_PROBE_INTERVAL"));
                Require(batches > 0 && checksum == checked(batches * 49995000L) && calls == checked(batches * plan.CallsPerBatch),
                    "The bound sample workload changed progress, checksum, or probe counts.");
            }
            passed = true;
        }
        catch (Exception error) { verificationFailure = error.ToString(); }
        var binding = lifetime?.Binding;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            mode, passed, failure, verificationFailure, invalidated, rejected, completed = result is not null,
            batches, checksum, calls, cpuNanoseconds = result?.CpuNanoseconds, cleanup,
            binding = binding is null ? null : new { binding.Generation, Module = (ulong)binding.Module,
                Function = (ulong)binding.Function, binding.Method, binding.Sites }, nativeRange = collection?.Map.Range,
            initialCounters = collection?.InitialCounters, finalCounters = collection?.FinalCounters,
            samples = collection?.Diagnostics, batch = collection?.Latest,
            sampledPaymentCapacityEstablished = false, predictionAccuracyEstablished = false, codePinned = false
        }, new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 1;
    }
}
