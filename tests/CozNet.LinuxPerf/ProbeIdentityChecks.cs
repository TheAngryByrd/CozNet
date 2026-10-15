using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using static CooperativeManagedChecks;

static class ProbeIdentityChecks
{
    private sealed record Binding(ulong Generation, ulong Module, ulong Function, uint Method, uint Sites);
    private sealed record Loaded(WeakReference Context, Binding Binding, ulong[] Observation);

    private static Binding ReadBinding()
    {
        Require(CozGetProbeBinding(out ulong generation, out nuint module, out nuint function, out uint method, out uint sites) != 0,
            "The profiler has no admitted probe binding.");
        Require(generation != 0 && module != 0 && function != 0 && method != 0 && sites == 2,
            "The profiler returned an incomplete controlled binding.");
        return new(generation, module, function, method, sites);
    }

    private static void CheckObservation(Binding binding, ulong[] observation, int method, ProbePlan plan)
    {
        Require(binding.Method == (uint)method && observation.Length == 4 && observation[0] == binding.Generation
            && observation[1] == 1 && observation[2] == (ulong)(plan.CallsPerBatch - 1) && observation[3] == 0,
            "The injected literals or site counts differ from the admitted method.");
        Require(CozIsProbeBindingCurrent(binding.Generation, 1) != 0 && CozIsProbeBindingCurrent(binding.Generation, 2) != 0
            && CozIsProbeBindingCurrent(0, 1) == 0 && CozIsProbeBindingCurrent(binding.Generation + 1, 1) == 0
            && CozIsProbeBindingCurrent(binding.Generation, 0) == 0 && CozIsProbeBindingCurrent(binding.Generation, 3) == 0,
            "The binding admitted an invalid generation or site.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Loaded LoadCollectible(ProbePlan plan, bool duplicate)
    {
        var context = new AssemblyLoadContext("coz-probe-lifetime", isCollectible: true);
        try
        {
            var assembly = context.LoadFromAssemblyPath(typeof(ProbeIdentityChecks).Assembly.Location);
            var method = assembly.GetType("CpuWorkload", throwOnError: true)!.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
            Require((long)method.Invoke(null, null)! == (long)CpuWorkload.Iterations * (CpuWorkload.Iterations - 1) / 2,
                "The collectible workload checksum changed.");
            if (duplicate)
            {
                Require(CozGetProbeLifetimeState() == 5, "A second selected module did not invalidate the first binding.");
                return new(new WeakReference(context), new(0, 0, 0, 0, 0), []);
            }
            var observation = (ulong[])assembly.GetType("InjectedProbes", throwOnError: true)!
                .GetMethod("ReadBoundObservation", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!;
            var binding = ReadBinding();
            CheckObservation(binding, observation, method.MetadataToken, plan);
            return new(new WeakReference(context), binding, observation);
        }
        finally { context.Unload(); }
    }

    public static int Run(string path, string mode)
    {
        if (mode is not ("identity" or "unload" or "duplicate-module")) throw new ArgumentException("Select identity, unload, or duplicate-module.");
        Binding? binding = null;
        ulong[]? observation = null;
        bool unloaded = false, passed = false;
        string? failure = null;
        int finalState = -1;
        try
        {
            var plan = ProbePlan.Parse("injected", Environment.GetEnvironmentVariable("COZNET_PROBE_INTERVAL"));
            Require(CozGetProbeLifetimeState() == 0 && CozIsProbeBindingCurrent(1, 1) == 0,
                "A probe binding was admitted before target compilation.");
            Require(CozGetProbeBinding(out ulong before, out _, out _, out _, out _) == 0 && before == 0,
                "The uncompiled target returned a binding.");
            if (mode == "unload")
            {
                var loaded = LoadCollectible(plan, false);
                binding = loaded.Binding;
                observation = loaded.Observation;
                var deadline = Stopwatch.StartNew();
                while ((loaded.Context.IsAlive || CozGetProbeLifetimeState() != 7) && deadline.Elapsed < TimeSpan.FromSeconds(5))
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    Thread.Sleep(10);
                }
                unloaded = !loaded.Context.IsAlive;
                Require(unloaded && CozGetProbeLifetimeState() == 7, "The collectible target did not unload and invalidate its binding.");
            }
            else
            {
                Require(CpuWorkload.Run() == (long)CpuWorkload.Iterations * (CpuWorkload.Iterations - 1) / 2,
                    "The bound workload checksum changed.");
                binding = ReadBinding();
                observation = InjectedProbes.ReadBoundObservation();
                CheckObservation(binding, observation, typeof(CpuWorkload).GetMethod(nameof(CpuWorkload.Run))!.MetadataToken, plan);
                if (mode == "duplicate-module") _ = LoadCollectible(plan, true);
            }
            if (mode != "identity")
            {
                Require(CozIsProbeBindingCurrent(binding.Generation, 1) == 0 && CozIsProbeBindingCurrent(binding.Generation, 2) == 0
                    && CozGetProbeBinding(out ulong after, out _, out _, out _, out _) == 0 && after == 0,
                    "Invalidation left an admitted binding or site.");
            }
            finalState = CozGetProbeLifetimeState();
            Require(finalState == (mode == "identity" ? 3 : mode == "unload" ? 7 : 5), "The lifetime state differs from the selected case.");
            passed = true;
        }
        catch (Exception error) { failure = error.ToString(); }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { mode, passed, failure, binding, observation, unloaded, finalState,
            paymentLifetimeIntegrated = false, nativeVersionIdentityEstablished = false, predictionAccuracyEstablished = false },
            new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 1;
    }

    [DllImport("CozNet.Profiler")] private static extern int CozGetProbeLifetimeState();
    [DllImport("CozNet.Profiler")] private static extern int CozGetProbeBinding(out ulong generation, out nuint module, out nuint function, out uint method, out uint sites);
    [DllImport("CozNet.Profiler")] private static extern int CozIsProbeBindingCurrent(ulong generation, uint site);
}
