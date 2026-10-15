using System.Collections.Immutable;
using System.Text.Json;

static class InterleavedCalibrationChecks
{
    private sealed class CpuClock : IThreadCpuClock
    {
        private long reading = 100;
        public long ReadNanoseconds() => reading += 100;
    }
    private sealed class Clock : TimeProvider
    {
        private long reading = 1000;
        public override long TimestampFrequency => 1000000000;
        public override long GetTimestamp() => reading += 1000;
    }
    private sealed class Calls { public int Count; }
    private readonly struct Arithmetic(Calls calls) : IChecksumArithmetic
    {
        public long Run() { calls.Count++; return 49995000; }
    }

    public static void Run()
    {
        PlanPreservesDeclaredWorkAndOrder();
        SegmentSizeAndClockConsistency();
        BlockStatisticsAndSensitivity();
        InvalidSequencesCannotProduceEstimate();
        DominatingControlAndNegativeReduction();
        CaptureContinuesAfterMeasurementFailure();
        DriverPreservesBlocksAndFailureOutcomes();
    }

    private static void PlanPreservesDeclaredWorkAndOrder()
    {
        var plan = InterleavedCalibration.CreatePlan();
        var repeat = InterleavedCalibration.CreatePlan();
        Require(plan.Length == 9 && plan[0].Warmup && plan[0].Index == -1,
            "The plan must contain one warmup and eight measured blocks.");
        for (int index = 0; index < plan.Length; index++)
        {
            var block = plan[index];
            Require(block.Index == index - 1 && block.Warmup == (index == 0)
                && block.Steps.Length == 300 && block.Steps.SequenceEqual(repeat[index].Steps),
                "Each block must retain its identity and reproduce the declared order.");
            Require(block.Steps.Chunk(3)
                .Select(round => string.Join(",", round.Select(step => step.Arithmetic)))
                .Distinct().Count() == 6,
                "The declared seed must exercise all six mode orders within each block.");
            for (int round = 0; round < 100; round++)
            {
                var steps = block.Steps.Skip(round * 3).Take(3).ToArray();
                Require(steps.All(step => step.Round == round)
                    && steps.Select(step => step.Arithmetic).Order().SequenceEqual(new[]
                    { CalibrationArithmetic.Baseline, CalibrationArithmetic.Optimized, CalibrationArithmetic.Control }),
                    "Every round must contain exactly one segment of each arithmetic mode.");
            }
        }
    }

    private static void SegmentSizeAndClockConsistency()
    {
        var calls = new Calls();
        var raw = PhysicalCalibration.Measure(new Arithmetic(calls), new CpuClock(), new Clock(), CalibrationBatchSize.Interleaved);
        Require(calls.Count == 500 && raw.CompletedBatches == 500 && raw.Checksum == 24997500000
            && CompleteCalibrationSegment.TryCreate(raw, CalibrationBatchSize.Interleaved)?.CpuNanoseconds == 100,
            "An interleaved segment must execute exactly 500 verified calls and retain its CPU interval.");
        Require(CompleteCalibrationSegment.TryCreate(raw, CalibrationBatchSize.Contiguous) is null,
            "A short segment must not satisfy the contiguous work-count contract.");
        var exact = raw with { BeforeTimestamp = 1000, AfterTimestamp = 1100 };
        Require(CompleteCalibrationSegment.TryCreate(exact, CalibrationBatchSize.Interleaved) is not null
            && CompleteCalibrationSegment.TryCreate(exact with { AfterTimestamp = 1099 }, CalibrationBatchSize.Interleaved) is null,
            "CPU time equal to its enclosing wall interval is valid. CPU time beyond that interval must fail.");
        var wide = raw with { BeforeTimestamp = long.MinValue, AfterTimestamp = long.MaxValue, Frequency = long.MaxValue, CpuBefore = 0, CpuAfter = 1 };
        Require(CompleteCalibrationSegment.TryCreate(wide, CalibrationBatchSize.Interleaved) is not null,
            "Clock consistency must avoid intermediate integer overflow.");
    }

    private static InterleavedCalibration.BlockResult[] Blocks(Func<int, CalibrationArithmetic, long>? duration = null)
    {
        long cpu = 1000, wall = 1000;
        return InterleavedCalibration.CreatePlan().Select(block => new InterleavedCalibration.BlockResult(block.Index, block.Warmup,
            block.Steps.Select(step =>
            {
                long amount = duration?.Invoke(block.Index, step.Arithmetic) ?? step.Arithmetic switch
                {
                    CalibrationArithmetic.Baseline => 1000,
                    CalibrationArithmetic.Optimized => 500,
                    _ => 10
                };
                var raw = new PhysicalCalibration.Measurement(wall, wall + amount + 20, 1000000000,
                    cpu, cpu + amount, 500, 24997500000, null);
                cpu += amount + 5;
                wall += amount + 50;
                return new InterleavedCalibration.Segment(step, raw);
            }).ToImmutableArray())).ToArray();
    }

    private static void BlockStatisticsAndSensitivity()
    {
        var analysis = InterleavedCalibrationAnalysis.Analyze(Blocks());
        var estimate = analysis.Estimate!;
        Require(analysis.Errors.Length == 0 && estimate.Blocks.Length == 8 && estimate.MeanReduction == 0.5
            && estimate.StandardDeviation == 0 && estimate.IntervalHalfWidth == 0,
            "Statistics must use eight block totals, not 800 dependent rounds.");
        foreach (var block in estimate.Blocks)
            Require(block.BaselineCpu == 100000 && block.OptimizedCpu == 50000 && block.ControlCpu == 1000
                && Math.Abs(block.SensitivityLower - 0.4949494949494949) < 1e-12 && block.SensitivityUpper == 0.51,
                "Sensitivity must allow independent external costs for baseline and optimized work without altering raw totals.");
        Require(Math.Abs(estimate.MaxObservedOverheadShift - 0.01) < 1e-12
            && Math.Abs(estimate.DistancePlusIntervalAndSensitivity - 0.01) < 1e-12,
            "The observed sensitivity shift must remain explicit in the distance statistic.");
        var varied = InterleavedCalibrationAnalysis.Analyze(Blocks((block, mode) => mode switch
        {
            CalibrationArithmetic.Baseline => 1000,
            CalibrationArithmetic.Control => 10,
            _ => block < 0 ? 900 : 300 + 50 * block
        })).Estimate!;
        Require(Math.Abs(varied.MeanReduction - 0.525) < 1e-12
            && Math.Abs(varied.StandardDeviation - 0.1224744871391589) < 1e-12
            && Math.Abs(varied.IntervalHalfWidth - 0.10239123358853752) < 1e-12
            && Math.Abs(varied.DistancePlusIntervalAndSensitivity - 0.13739123358853752) < 1e-12,
            "Warmup must be excluded and the interval must use seven degrees of freedom.");
    }

    private static void InvalidSequencesCannotProduceEstimate()
    {
        var valid = Blocks();
        Require(InterleavedCalibrationAnalysis.Analyze(valid[..^1]).Estimate is null, "Missing blocks must prevent analysis.");
        foreach (int index in new[] { 0, 1, 8 })
        {
            var changed = valid.ToArray();
            var first = changed[index].Segments[0];
            changed[index] = changed[index] with { Segments = changed[index].Segments.SetItem(0,
                first with { Measurement = first.Measurement with { Error = "Injected segment failure." } }) };
            Require(InterleavedCalibrationAnalysis.Analyze(changed).Estimate is null,
                "Failure in warmup or any measured block must prevent the full-series estimate.");
        }
        foreach (var segments in new[]
        {
            valid[1].Segments.RemoveAt(0),
            valid[1].Segments.SetItem(0, valid[1].Segments[1]).SetItem(1, valid[1].Segments[0]),
            valid[1].Segments.SetItem(1, valid[1].Segments[0]),
            valid[1].Segments.SetItem(1, valid[1].Segments[1] with
            { Measurement = valid[1].Segments[1].Measurement with { CpuBefore = 0, CpuAfter = 10 } }),
            valid[1].Segments.SetItem(1, valid[1].Segments[1] with
            { Measurement = valid[1].Segments[1].Measurement with { Frequency = 1000000 } })
        })
        {
            var changed = valid.ToArray();
            changed[1] = changed[1] with { Segments = segments };
            var result = InterleavedCalibrationAnalysis.Analyze(changed);
            Require(result.Estimate is null && result.Errors.Length > 0,
                "Missing, reordered, duplicated, or clock-inconsistent segments must retain a rejection reason.");
        }
    }

    private static void DominatingControlAndNegativeReduction()
    {
        var dominated = InterleavedCalibrationAnalysis.Analyze(Blocks((_, mode) => mode == CalibrationArithmetic.Baseline ? 1000 : 500));
        Require(dominated.Estimate is null && dominated.Errors.Any(error => error.Contains("positive method costs")),
            "Control equal to optimized CPU cost must reject the sensitivity model.");
        var negative = InterleavedCalibrationAnalysis.Analyze(Blocks((_, mode) => mode switch
        {
            CalibrationArithmetic.Baseline => 1000,
            CalibrationArithmetic.Optimized => 1500,
            _ => 10
        })).Estimate!;
        Require(negative.MeanReduction == -0.5 && negative.Blocks.All(block => block.SensitivityLower < -0.5 && block.SensitivityUpper > -0.5),
            "Negative reductions and their sensitivity range must not be clipped.");
    }

    private static void CaptureContinuesAfterMeasurementFailure()
    {
        var plan = InterleavedCalibration.CreatePlan()[0];
        var expected = Blocks()[0];
        int calls = 0;
        var result = InterleavedCalibration.Capture(plan, _ =>
        {
            int index = calls++;
            if (index == 2) throw new IOException("Injected segment read failure.");
            return expected.Segments[index].Measurement;
        });
        Require(calls == 300 && result.Segments.Length == 300
            && result.Segments[2].Measurement.Error!.Contains("Injected segment read failure.")
            && result.Segments[2].Measurement.CompletedBatches == 0
            && result.Segments[3] == expected.Segments[3] && result.Segments[^1] == expected.Segments[^1],
            "A measurement exception must retain a failed segment and continue the declared block.");
    }

    private static void DriverPreservesBlocksAndFailureOutcomes()
    {
        foreach (int failedCheckpoint in new[] { -1, 2, 8 })
        {
            string path = Path.Combine(Path.GetTempPath(), "coz-interleaved-" + Guid.NewGuid().ToString("N") + ".json");
            string[] names = ["DOTNET_TieredCompilation", "DOTNET_ReadyToRun", "CORECLR_ENABLE_PROFILING"];
            var previous = names.Select(Environment.GetEnvironmentVariable).ToArray();
            var expected = Blocks();
            int measured = 0, checkpoints = 0;
            try
            {
                foreach (string name in names) Environment.SetEnvironmentVariable(name, "0");
                int exit = InterleavedCalibration.Run(path, () =>
                {
                    using var plan = JsonDocument.Parse(File.ReadAllText(path + ".plan.json"));
                    Require(plan.RootElement.GetProperty("plan").GetArrayLength() == 9, "The complete plan must exist before clock setup.");
                    return new CpuClock();
                }, step =>
                {
                    int index = measured++;
                    Require(step == expected[index / 300].Segments[index % 300].Step, "Driver order must match the saved segment sequence.");
                    if (failedCheckpoint == -1 && index % 300 == 0)
                    {
                        using var stream = new FileStream(path + ".blocks.jsonl", FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var reader = new StreamReader(stream);
                        Require(reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == index / 300,
                            "Every previous block must be readable before the next block begins.");
                    }
                    return expected[index / 300].Segments[index % 300].Measurement;
                }, failedCheckpoint == -1 ? null : _ =>
                {
                    if (checkpoints++ == failedCheckpoint) throw new IOException("Injected block checkpoint failure.");
                });
                using var result = JsonDocument.Parse(File.ReadAllText(path));
                int completedBlocks = failedCheckpoint == -1 ? 9 : failedCheckpoint + 1;
                Require(measured == completedBlocks * 300 && result.RootElement.GetProperty("blocks").GetArrayLength() == completedBlocks
                    && result.RootElement.GetProperty("unexecutedBlocks").GetArrayLength() == 9 - completedBlocks
                    && result.RootElement.GetProperty("calibrationComplete").GetBoolean() == (failedCheckpoint == -1)
                    && exit == (failedCheckpoint == -1 ? 0 : 1)
                    && !result.RootElement.GetProperty("overheadBoundEstablished").GetBoolean()
                    && !result.RootElement.GetProperty("predictionAccuracyEstablished").GetBoolean(),
                    "The driver must retain measured blocks, stop after storage failure, and preserve its claim limits.");
                if (failedCheckpoint != -1)
                    Require(result.RootElement.GetProperty("analysis").GetProperty("Estimate").ValueKind == JsonValueKind.Null,
                        "Storage failure must suppress an estimate even after the last measured block.");
            }
            finally
            {
                for (int index = 0; index < names.Length; index++) Environment.SetEnvironmentVariable(names[index], previous[index]);
                File.Delete(path);
                File.Delete(path + ".plan.json");
                File.Delete(path + ".blocks.jsonl");
            }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
