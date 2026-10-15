using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

[JsonConverter(typeof(JsonStringEnumConverter<CalibrationArithmetic>))]
enum CalibrationArithmetic { Baseline, Optimized, Control }

static class InterleavedCalibration
{
    public const int Seed = 20261015;
    public const int BlockCount = 8;
    public const int RoundsPerBlock = 100;
    public sealed record Step(int Round, CalibrationArithmetic Arithmetic);
    public sealed record Block(int Index, bool Warmup, ImmutableArray<Step> Steps);
    public sealed record Segment(Step Step, PhysicalCalibration.Measurement Measurement);
    public sealed record BlockResult(int Index, bool Warmup, ImmutableArray<Segment> Segments);

    public static ImmutableArray<Block> CreatePlan()
    {
        var blocks = ImmutableArray.CreateBuilder<Block>();
        var random = new Random(Seed);
        for (int block = -1; block < BlockCount; block++)
        {
            var steps = ImmutableArray.CreateBuilder<Step>();
            for (int round = 0; round < RoundsPerBlock; round++)
            {
                CalibrationArithmetic[] order = [CalibrationArithmetic.Baseline, CalibrationArithmetic.Optimized, CalibrationArithmetic.Control];
                for (int index = order.Length - 1; index > 0; index--)
                {
                    int other = random.Next(index + 1);
                    (order[index], order[other]) = (order[other], order[index]);
                }
                foreach (var arithmetic in order) steps.Add(new(round, arithmetic));
            }
            blocks.Add(new(block, block == -1, steps.ToImmutable()));
        }
        return blocks.ToImmutable();
    }

    public static BlockResult Capture(Block block, Func<Step, PhysicalCalibration.Measurement> measure)
    {
        var segments = ImmutableArray.CreateBuilder<Segment>();
        foreach (var step in block.Steps)
        {
            PhysicalCalibration.Measurement measurement;
            try { measurement = measure(step); }
            catch (Exception error) { measurement = new(null, null, 0, null, null, 0, 0, error.ToString()); }
            segments.Add(new(step, measurement));
        }
        return new(block.Index, block.Warmup, segments.ToImmutable());
    }

    private static PhysicalCalibration.Measurement Measure(CalibrationArithmetic arithmetic, IThreadCpuClock clock) => arithmetic switch
    {
        CalibrationArithmetic.Baseline => PhysicalCalibration.Measure(new OriginalChecksumArithmetic(), clock, TimeProvider.System, CalibrationBatchSize.Interleaved),
        CalibrationArithmetic.Optimized => PhysicalCalibration.Measure(new OptimizedChecksumArithmetic(), clock, TimeProvider.System, CalibrationBatchSize.Interleaved),
        CalibrationArithmetic.Control => PhysicalCalibration.Measure(new ControlChecksumArithmetic(), clock, TimeProvider.System, CalibrationBatchSize.Interleaved),
        _ => throw new InvalidDataException("Unknown calibration arithmetic.")
    };

    public static int Run(string path, Func<IThreadCpuClock> cpuFactory,
        Func<Step, PhysicalCalibration.Measurement>? measure = null, Action<BlockResult>? checkpoint = null)
    {
        CalibrationEvidence.RequireNew(path);
        var plan = CreatePlan();
        var options = new JsonSerializerOptions { WriteIndented = true };
        CalibrationEvidence.WriteNew(path + ".plan.json", new
        {
            protocol = "interleaved-control-v1", seed = Seed, blocks = BlockCount, roundsPerBlock = RoundsPerBlock,
            callsPerSegment = CalibrationBatchSize.Interleaved.Calls, plan,
            orderGenerator = "Fisher-Yates with System.Random.Next", runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString()
        }, options);
        using var journal = new FileStream(path + ".blocks.jsonl", FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        var blocks = new List<BlockResult>();
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
            if (new ControlChecksumArithmetic().Run() != MatchedCpuWorkers.ExpectedChecksum)
                throw new InvalidDataException("Calibration control changed the checksum.");
            foreach (var block in plan)
            {
                var result = Capture(block, step => measure is null ? Measure(step.Arithmetic, cpu) : measure(step));
                blocks.Add(result);
                if (checkpoint is not null) checkpoint(result);
                else CalibrationEvidence.Append(journal, result);
            }
        }
        catch (Exception error) { failures.Add(error.ToString()); }
        var analysis = failures.Count == 0 ? InterleavedCalibrationAnalysis.Analyze(blocks)
            : new InterleavedCalibrationAnalysis.Analysis(null, ["Collection failed."]);
        bool complete = failures.Count == 0 && analysis.Estimate is not null;
        CalibrationEvidence.WriteNew(path, new
        {
            protocol = "interleaved-control-v1", blocks, failures, unexecutedBlocks = plan.Skip(blocks.Count), clockType, analysis,
            calibrationComplete = complete, predictionAccuracyEstablished = false,
            clockAccuracyEstablished = false, overheadBoundEstablished = false
        }, options);
        return complete ? 0 : 1;
    }
}
