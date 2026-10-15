static class InterleavedCalibrationAnalysis
{
    public sealed record BlockCosts(int Index, long BaselineCpu, long OptimizedCpu, long ControlCpu,
        double RawReduction, double SensitivityLower, double SensitivityUpper);
    public sealed record Estimate(BlockCosts[] Blocks, double MeanReduction, double StandardDeviation,
        double IntervalHalfWidth, double MaxObservedOverheadShift, double DistancePlusIntervalAndSensitivity);
    public sealed record Analysis(Estimate? Estimate, string[] Errors);

    public static Analysis Analyze(IReadOnlyList<InterleavedCalibration.BlockResult> blocks)
    {
        var plan = InterleavedCalibration.CreatePlan();
        if (blocks.Count != plan.Length) return new(null, ["The complete declared block sequence is required."]);
        var errors = new List<string>();
        var costs = new List<BlockCosts>();
        long? previousCpu = null, previousWall = null, frequency = null;
        for (int index = 0; index < plan.Length; index++)
        {
            var expected = plan[index];
            var block = blocks[index];
            if (block.Index != expected.Index || block.Warmup != expected.Warmup || block.Segments.Length != expected.Steps.Length)
            {
                errors.Add($"Block position {index} does not match its declared identity or size.");
                continue;
            }
            long baseline = 0, optimized = 0, control = 0;
            bool valid = true;
            for (int segmentIndex = 0; segmentIndex < expected.Steps.Length; segmentIndex++)
            {
                var segment = block.Segments[segmentIndex];
                var complete = CompleteCalibrationSegment.TryCreate(segment.Measurement, CalibrationBatchSize.Interleaved);
                if (segment.Step != expected.Steps[segmentIndex] || complete is null)
                {
                    errors.Add($"Block {block.Index}, segment {segmentIndex} has invalid identity, work, or clocks.");
                    valid = false;
                    continue;
                }
                var raw = complete.Raw;
                if ((previousCpu is { } cpu && raw.CpuBefore < cpu)
                    || (previousWall is { } wall && raw.BeforeTimestamp < wall)
                    || (frequency is { } ticks && raw.Frequency != ticks))
                {
                    errors.Add($"Block {block.Index}, segment {segmentIndex} has inconsistent cumulative clocks.");
                    valid = false;
                }
                previousCpu = raw.CpuAfter;
                previousWall = raw.AfterTimestamp;
                frequency = raw.Frequency;
                try
                {
                    switch (segment.Step.Arithmetic)
                    {
                        case CalibrationArithmetic.Baseline: baseline = checked(baseline + complete.CpuNanoseconds); break;
                        case CalibrationArithmetic.Optimized: optimized = checked(optimized + complete.CpuNanoseconds); break;
                        case CalibrationArithmetic.Control: control = checked(control + complete.CpuNanoseconds); break;
                    }
                }
                catch (OverflowException)
                {
                    errors.Add($"Block {block.Index} CPU totals overflowed.");
                    valid = false;
                }
            }
            if (!valid) continue;
            if (control <= 0 || control >= Math.Min(baseline, optimized))
            {
                errors.Add($"Block {block.Index} control cost does not leave positive method costs.");
                continue;
            }
            if (!block.Warmup)
                costs.Add(new(block.Index, baseline, optimized, control,
                    1 - (double)optimized / baseline,
                    1 - (double)optimized / (baseline - control),
                    1 - (double)(optimized - control) / baseline));
        }
        if (errors.Count != 0) return new(null, errors.ToArray());
        double mean = costs.Average(value => value.RawReduction);
        double deviation = Math.Sqrt(costs.Sum(value => Math.Pow(value.RawReduction - mean, 2)) / (costs.Count - 1));
        double halfWidth = 2.364624251 * deviation / Math.Sqrt(costs.Count);
        double overheadShift = costs.Max(value => Math.Max(value.RawReduction - value.SensitivityLower, value.SensitivityUpper - value.RawReduction));
        return new(new(costs.ToArray(), mean, deviation, halfWidth, overheadShift,
            Math.Abs(mean - 0.5) + halfWidth + overheadShift), []);
    }
}
