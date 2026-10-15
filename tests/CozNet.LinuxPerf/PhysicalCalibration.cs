using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

static class PhysicalCalibration
{
    public const int Seed = 20261014;
    public const int PairCount = 8;
    public const int CallsPerTrial = 50000;
    public sealed record Step(int Pair, PhysicalWorkMode Mode, bool Warmup);
    public sealed record Measurement(long? BeforeTimestamp, long? AfterTimestamp, long Frequency,
        long? CpuBefore, long? CpuAfter, int CompletedBatches, long Checksum, string? Error);
    public sealed record Trial(Step Step, Measurement Measurement);
    public sealed record Estimate(double MeanReduction, double StandardDeviation, double IntervalHalfWidth,
        double AbsoluteHalfMismatchUpper, double[] PairedReductions);

    public static ImmutableArray<Step> CreatePlan()
    {
        var steps = ImmutableArray.CreateBuilder<Step>();
        var baseline = PhysicalWorkMode.Baseline;
        var optimized = PhysicalWorkMode.Parse("optimized");
        steps.Add(new(-1, baseline, true));
        steps.Add(new(-1, optimized, true));
        var random = new Random(Seed);
        for (int pair = 0; pair < PairCount; pair++)
        {
            bool optimizedFirst = random.Next(2) == 1;
            steps.Add(new(pair, optimizedFirst ? optimized : baseline, false));
            steps.Add(new(pair, optimizedFirst ? baseline : optimized, false));
        }
        return steps.ToImmutable();
    }

    internal static Measurement Measure<T>(T arithmetic, IThreadCpuClock cpu, TimeProvider clock)
        where T : struct, IChecksumArithmetic
    {
        long? before = null, after = null, cpuBefore = null, cpuAfter = null;
        long checksum = 0, frequency = 0;
        int completed = 0;
        string? failure = null;
        try
        {
            frequency = clock.TimestampFrequency;
            if (frequency <= 0) throw new InvalidDataException("The calibration clock frequency must be positive.");
            before = clock.GetTimestamp();
            cpuBefore = cpu.ReadNanoseconds();
            for (int index = 0; index < CallsPerTrial; index++)
            {
                long result = arithmetic.Run();
                if (result != MatchedCpuWorkers.ExpectedChecksum)
                    throw new InvalidDataException("Calibration arithmetic changed the checksum.");
                checksum = checked(checksum + result);
                completed++;
            }
            cpuAfter = cpu.ReadNanoseconds();
            after = clock.GetTimestamp();
            if (cpuBefore < 0 || cpuAfter <= cpuBefore || after <= before)
                throw new InvalidDataException("Calibration requires positive CPU and wall intervals with nonnegative CPU counters.");
        }
        catch (Exception error) { failure = error.ToString(); }
        return new(before, after, frequency, cpuBefore, cpuAfter, completed, checksum, failure);
    }

    public static Estimate? Analyze(IReadOnlyList<Trial> trials)
    {
        var plan = CreatePlan();
        if (trials.Count != plan.Length) return null;
        for (int index = 0; index < plan.Length; index++)
        {
            var expected = plan[index];
            var trial = trials[index];
            var measurement = trial.Measurement;
            if (trial.Step.Pair != expected.Pair || trial.Step.Warmup != expected.Warmup
                || trial.Step.Mode.Name != expected.Mode.Name || measurement.Error is not null
                || measurement.CompletedBatches != CallsPerTrial
                || measurement.Checksum != (long)CallsPerTrial * MatchedCpuWorkers.ExpectedChecksum
                || measurement.CpuBefore is not { } cpuBefore || cpuBefore < 0
                || measurement.CpuAfter is not { } cpuAfter || cpuAfter <= cpuBefore
                || measurement.BeforeTimestamp is not { } before || measurement.AfterTimestamp is not { } after
                || after <= before || measurement.Frequency <= 0) return null;
        }
        var reductions = new double[PairCount];
        for (int pair = 0; pair < PairCount; pair++)
        {
            var values = trials.Where(trial => !trial.Step.Warmup && trial.Step.Pair == pair).ToArray();
            var baseline = values.Single(trial => !trial.Step.Mode.Optimized).Measurement;
            var optimized = values.Single(trial => trial.Step.Mode.Optimized).Measurement;
            double originalCpu = baseline.CpuAfter!.Value - baseline.CpuBefore!.Value;
            double optimizedCpu = optimized.CpuAfter!.Value - optimized.CpuBefore!.Value;
            reductions[pair] = 1 - optimizedCpu / originalCpu;
        }
        double mean = reductions.Average();
        double deviation = Math.Sqrt(reductions.Sum(value => (value - mean) * (value - mean)) / (PairCount - 1));
        double halfWidth = 2.364624251 * deviation / Math.Sqrt(PairCount);
        return new(mean, deviation, halfWidth, Math.Abs(mean - 0.5) + halfWidth, reductions);
    }

    public static int Run(string path, Func<IThreadCpuClock> cpuFactory,
        Func<PhysicalWorkMode, IThreadCpuClock, Measurement>? measure = null, Action<Trial>? checkpoint = null)
    {
        if (File.Exists(path) || File.Exists(path + ".plan.json") || File.Exists(path + ".trials.jsonl"))
            throw new IOException("Calibration evidence already exists.");
        var plan = CreatePlan();
        var options = new JsonSerializerOptions { WriteIndented = true };
        WriteNew(path + ".plan.json", new
        {
            seed = Seed, pairCount = PairCount, callsPerTrial = CallsPerTrial, steps = plan,
            orderGenerator = "System.Random.Next(2)", runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString()
        }, options);
        using var journal = new FileStream(path + ".trials.jsonl", FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        var trials = new List<Trial>();
        var failures = new List<string>();
        string? clockType = null;
        try
        {
            if (Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") != "0"
                || Environment.GetEnvironmentVariable("DOTNET_ReadyToRun") != "0"
                || Environment.GetEnvironmentVariable("CORECLR_ENABLE_PROFILING") == "1")
                throw new InvalidDataException("Physical calibration requires disabled tiering and ReadyToRun, and no startup profiler.");
            var cpu = cpuFactory();
            clockType = cpu.GetType().FullName;
            MatchedCpuWorkers.Warmup();
            foreach (var step in plan)
            {
                var measurement = measure is not null ? measure(step.Mode, cpu)
                    : step.Mode.Optimized ? Measure(new OptimizedChecksumArithmetic(), cpu, TimeProvider.System)
                    : Measure(new OriginalChecksumArithmetic(), cpu, TimeProvider.System);
                var trial = new Trial(step, measurement);
                trials.Add(trial);
                if (checkpoint is not null) checkpoint(trial);
                else
                {
                    byte[] row = JsonSerializer.SerializeToUtf8Bytes(trial);
                    journal.Write(row);
                    journal.WriteByte((byte)'\n');
                    journal.Flush(flushToDisk: true);
                }
            }
        }
        catch (Exception error) { failures.Add(error.ToString()); }
        var estimate = failures.Count == 0 ? Analyze(trials) : null;
        bool complete = failures.Count == 0 && estimate is not null;
        WriteNew(path, new
        {
            plan, trials, failures, unexecutedSteps = plan.Skip(trials.Count), clockType, estimate,
            calibrationComplete = complete, predictionAccuracyEstablished = false,
            clockAccuracyEstablished = false
        }, options);
        return complete ? 0 : 1;
    }

    private static void WriteNew(string path, object value, JsonSerializerOptions options)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(stream, value, options);
    }
}
