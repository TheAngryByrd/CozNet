static class PhysicalCalibrationChecks
{
    private sealed class CpuClock(long before, long after) : IThreadCpuClock
    {
        private int reads;
        public long ReadNanoseconds() => ++reads == 1 ? before : after;
    }

    private sealed class Clock(long before = 1000, long after = 1100, long frequency = 1000000) : TimeProvider
    {
        private int reads;
        public override long TimestampFrequency => frequency;
        public override long GetTimestamp() => ++reads == 1 ? before : after;
    }

    private sealed class Calls { public int Count; }
    private readonly struct Arithmetic(Calls calls, int failAt = int.MaxValue) : IChecksumArithmetic
    {
        public long Run() => ++calls.Count == failAt ? 0 : 49995000;
    }

    public static void Run()
    {
        FixedWorkRetainsExactReadings();
        BadChecksumRetainsPartialWork();
        InvalidClocksCannotCompleteMeasurement();
        EstimateRequiresEveryPlannedTrial();
        PairedCpuReductionHasDeclaredInterval();
        NegativeReductionsAreRetained();
        DriverRetainsFailedSetupAndRejectsOverwrite();
        DriverContinuesAndCheckpointsFailedTrial();
        OrphanedEvidencePreventsExecution();
        CheckpointFailureRetainsMeasuredTrial();
    }

    private static void FixedWorkRetainsExactReadings()
    {
        var calls = new Calls();
        var result = PhysicalCalibration.Measure(new Arithmetic(calls), new CpuClock(100, 300), new Clock());
        Require(result == new PhysicalCalibration.Measurement(1000, 1100, 1000000, 100, 300, 50000, 2499750000000, null)
            && calls.Count == 50000, "Calibration must retain exact clocks and verify every fixed-work call.");
    }

    private static void BadChecksumRetainsPartialWork()
    {
        var calls = new Calls();
        var result = PhysicalCalibration.Measure(new Arithmetic(calls, 3), new CpuClock(100, 300), new Clock());
        Require(result.Error?.Contains("Calibration arithmetic changed the checksum.") == true
            && result.CompletedBatches == 2 && result.Checksum == 99990000 && calls.Count == 3
            && result.BeforeTimestamp == 1000 && result.CpuBefore == 100
            && result.CpuAfter is null && result.AfterTimestamp is null,
            "A checksum failure must retain completed work and initial clocks without fabricating an endpoint.");
    }

    private static void InvalidClocksCannotCompleteMeasurement()
    {
        foreach (var clocks in new (CpuClock Cpu, Clock Wall)[]
        {
            (new(-1, 100), new()), (new(100, 100), new()), (new(100, 99), new()),
            (new(100, 200), new(1000, 1000)), (new(100, 200), new(1000, 999)),
            (new(100, 200), new(frequency: 0))
        })
        {
            var result = PhysicalCalibration.Measure(new Arithmetic(new Calls()), clocks.Cpu, clocks.Wall);
            Require(result.Error is not null, "Negative CPU counters, nonpositive intervals, and invalid frequencies must fail calibration.");
        }
    }

    private static PhysicalCalibration.Trial[] Trials()
    {
        return PhysicalCalibration.CreatePlan().Select(step => new PhysicalCalibration.Trial(step,
            new(1000, 1100, 1000000, 100, step.Mode.Optimized ? 600 : 1100,
                50000, 2499750000000, null))).ToArray();
    }

    private static void EstimateRequiresEveryPlannedTrial()
    {
        var trials = Trials();
        var estimate = PhysicalCalibration.Analyze(trials);
        Require(trials.Length == 18 && trials.Count(trial => trial.Step.Warmup) == 2
            && estimate is { MeanReduction: 0.5, StandardDeviation: 0, IntervalHalfWidth: 0, AbsoluteHalfMismatchUpper: 0 }
            && estimate.PairedReductions.Length == 8,
            "Eight complete pairs plus both warmups must produce the exact fixed reduction.");
        Require(PhysicalCalibration.Analyze(trials[..^1]) is null,
            "A missing final trial must prevent a calibration estimate.");
        foreach (int index in new[] { 0, 2, 17 })
        {
            var changed = trials.ToArray();
            changed[index] = changed[index] with { Measurement = changed[index].Measurement with { Error = "Injected trial failure." } };
            Require(PhysicalCalibration.Analyze(changed) is null,
                "A failed warmup or measured trial must prevent the complete-series estimate.");
        }
        var duplicate = trials.ToArray();
        duplicate[^1] = duplicate[^2];
        Require(PhysicalCalibration.Analyze(duplicate) is null,
            "A duplicated trial cannot replace its missing paired mode.");
        var reordered = trials.ToArray();
        (reordered[2], reordered[3]) = (reordered[3], reordered[2]);
        Require(PhysicalCalibration.Analyze(reordered) is null,
            "A complete pair executed in the wrong order must not match the declared plan.");
        foreach (var invalid in new[]
        {
            trials[2].Measurement with { CompletedBatches = 49999 },
            trials[2].Measurement with { Checksum = 0 },
            trials[2].Measurement with { CpuAfter = 100 },
            trials[2].Measurement with { CpuBefore = null },
            trials[2].Measurement with { AfterTimestamp = 999 },
            trials[2].Measurement with { Frequency = 0 }
        })
        {
            var changed = trials.ToArray();
            changed[2] = changed[2] with { Measurement = invalid };
            Require(PhysicalCalibration.Analyze(changed) is null,
                "Incomplete work or invalid retained clocks must prevent analysis even without an error string.");
        }
    }

    private static void PairedCpuReductionHasDeclaredInterval()
    {
        var trials = Trials();
        for (int index = 0; index < trials.Length; index++)
        {
            var step = trials[index].Step;
            long duration = step.Warmup ? 900000 : step.Mode.Optimized ? 300 + 50 * step.Pair : 1000;
            trials[index] = trials[index] with { Measurement = trials[index].Measurement with { CpuAfter = 100 + duration } };
        }
        var estimate = PhysicalCalibration.Analyze(trials)!;
        Require(Math.Abs(estimate.MeanReduction - 0.525) < 1e-12
            && Math.Abs(estimate.StandardDeviation - 0.1224744871391589) < 1e-12
            && Math.Abs(estimate.IntervalHalfWidth - 0.10239123358853752) < 1e-12
            && Math.Abs(estimate.AbsoluteHalfMismatchUpper - 0.12739123358853752) < 1e-12,
            "Analysis must use paired CPU reductions, sample deviation, and the declared Student-t interval without warmup contamination.");
    }

    private static void NegativeReductionsAreRetained()
    {
        var trials = Trials();
        for (int index = 0; index < trials.Length; index++)
            if (trials[index].Step.Mode.Optimized)
                trials[index] = trials[index] with { Measurement = trials[index].Measurement with { CpuAfter = 1600 } };
        var estimate = PhysicalCalibration.Analyze(trials)!;
        Require(estimate.MeanReduction == -0.5 && estimate.AbsoluteHalfMismatchUpper == 1
            && estimate.PairedReductions.All(value => value == -0.5),
            "A slower optimized workload must remain a negative reduction without clipping.");
    }

    private static void DriverRetainsFailedSetupAndRejectsOverwrite()
    {
        string path = Path.Combine(Path.GetTempPath(), "coz-calibration-check-" + Guid.NewGuid().ToString("N") + ".json");
        string[] names = ["DOTNET_TieredCompilation", "DOTNET_ReadyToRun", "CORECLR_ENABLE_PROFILING"];
        var previous = names.Select(Environment.GetEnvironmentVariable).ToArray();
        bool factoryCalled = false;
        try
        {
            foreach (string name in names) Environment.SetEnvironmentVariable(name, "0");
            int exit = PhysicalCalibration.Run(path, () =>
            {
                factoryCalled = true;
                using var plan = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path + ".plan.json"));
                Require(plan.RootElement.GetProperty("steps").GetArrayLength() == 18,
                    "The exact calibration order must exist before CPU-clock setup or any timed work.");
                throw new IOException("Injected CPU-clock setup failure.");
            });
            string original = File.ReadAllText(path);
            using var result = System.Text.Json.JsonDocument.Parse(original);
            Require(exit == 1 && factoryCalled && result.RootElement.GetProperty("trials").GetArrayLength() == 0
                && result.RootElement.GetProperty("unexecutedSteps").GetArrayLength() == 18
                && !result.RootElement.GetProperty("calibrationComplete").GetBoolean()
                && result.RootElement.GetProperty("failures")[0].GetString()!.Contains("Injected CPU-clock setup failure."),
                "Failed setup must preserve the failure and every unexecuted trial without a completed calibration.");
            File.Delete(path + ".plan.json");
            File.Delete(path + ".trials.jsonl");
            bool rejected = false;
            try { PhysicalCalibration.Run(path, () => throw new InvalidOperationException("Existing results must prevent setup.")); }
            catch (IOException) { rejected = true; }
            Require(rejected && File.ReadAllText(path) == original && !File.Exists(path + ".plan.json")
                && !File.Exists(path + ".trials.jsonl"),
                "An existing result must prevent new planning and execution without replacing retained evidence.");
        }
        finally
        {
            for (int index = 0; index < names.Length; index++) Environment.SetEnvironmentVariable(names[index], previous[index]);
            File.Delete(path);
            File.Delete(path + ".plan.json");
            File.Delete(path + ".trials.jsonl");
        }
    }

    private static void DriverContinuesAndCheckpointsFailedTrial()
    {
        string path = Path.Combine(Path.GetTempPath(), "coz-calibration-check-" + Guid.NewGuid().ToString("N") + ".json");
        string[] names = ["DOTNET_TieredCompilation", "DOTNET_ReadyToRun", "CORECLR_ENABLE_PROFILING"];
        var previous = names.Select(Environment.GetEnvironmentVariable).ToArray();
        var expected = Trials();
        int calls = 0;
        try
        {
            foreach (string name in names) Environment.SetEnvironmentVariable(name, "0");
            int exit = PhysicalCalibration.Run(path, () => new CpuClock(100, 300), (mode, _) =>
            {
                using var input = new FileStream(path + ".trials.jsonl", FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(input);
                Require(reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == calls,
                    "Every previous trial must be flushed before the next measurement begins.");
                int index = calls++;
                Require(mode.Name == expected[index].Step.Mode.Name,
                    "The driver must preserve the saved order after a failed measured trial.");
                return index == 2
                    ? expected[index].Measurement with { Error = "Injected measured-trial failure.", CompletedBatches = 3, Checksum = 149985000 }
                    : expected[index].Measurement;
            });
            using var result = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            var rows = File.ReadAllLines(path + ".trials.jsonl");
            Require(exit == 1 && calls == 18 && rows.Length == 18
                && result.RootElement.GetProperty("trials").GetArrayLength() == 18
                && result.RootElement.GetProperty("unexecutedSteps").GetArrayLength() == 0
                && result.RootElement.GetProperty("estimate").ValueKind == System.Text.Json.JsonValueKind.Null
                && !result.RootElement.GetProperty("calibrationComplete").GetBoolean(),
                $"A failed measured trial must retain every later outcome and prevent a complete-series estimate. Calls={calls}, rows={rows.Length}, failures={result.RootElement.GetProperty("failures").GetRawText()}.");
            using var failedRow = System.Text.Json.JsonDocument.Parse(rows[2]);
            Require(failedRow.RootElement.GetProperty("Measurement").GetProperty("CompletedBatches").GetInt32() == 3
                && failedRow.RootElement.GetProperty("Measurement").GetProperty("Error").GetString() == "Injected measured-trial failure.",
                "The journal must retain the failed trial and its completed prefix.");
        }
        finally
        {
            for (int index = 0; index < names.Length; index++) Environment.SetEnvironmentVariable(names[index], previous[index]);
            File.Delete(path);
            File.Delete(path + ".plan.json");
            File.Delete(path + ".trials.jsonl");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void CheckpointFailureRetainsMeasuredTrial()
    {
        foreach (int failedIndex in new[] { 2, 17 })
        {
            string path = Path.Combine(Path.GetTempPath(), "coz-calibration-check-" + Guid.NewGuid().ToString("N") + ".json");
            string[] names = ["DOTNET_TieredCompilation", "DOTNET_ReadyToRun", "CORECLR_ENABLE_PROFILING"];
            var previous = names.Select(Environment.GetEnvironmentVariable).ToArray();
            var expected = Trials();
            int measured = 0, checkpoints = 0;
            try
            {
                foreach (string name in names) Environment.SetEnvironmentVariable(name, "0");
                int exit = PhysicalCalibration.Run(path, () => new CpuClock(100, 300),
                    (_, _) => expected[measured++].Measurement,
                    _ => { if (checkpoints++ == failedIndex) throw new IOException("Injected checkpoint failure."); });
                using var result = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
                Require(exit == 1 && measured == failedIndex + 1 && checkpoints == failedIndex + 1
                    && result.RootElement.GetProperty("trials").GetArrayLength() == measured
                    && result.RootElement.GetProperty("unexecutedSteps").GetArrayLength() == 18 - measured
                    && result.RootElement.GetProperty("estimate").ValueKind == System.Text.Json.JsonValueKind.Null
                    && !result.RootElement.GetProperty("calibrationComplete").GetBoolean()
                    && result.RootElement.GetProperty("failures")[0].GetString()!.Contains("Injected checkpoint failure."),
                    "Checkpoint failure must retain the measured trial, stop later work, and suppress estimates even after the final measurement.");
                Require(result.RootElement.GetProperty("trials")[failedIndex].GetProperty("Measurement").GetProperty("CompletedBatches").GetInt32() == 50000,
                    "A measured trial must not be mislabeled as unexecuted after its checkpoint fails.");
            }
            finally
            {
                for (int index = 0; index < names.Length; index++) Environment.SetEnvironmentVariable(names[index], previous[index]);
                File.Delete(path);
                File.Delete(path + ".plan.json");
                File.Delete(path + ".trials.jsonl");
            }
        }
    }

    private static void OrphanedEvidencePreventsExecution()
    {
        foreach (string suffix in new[] { ".plan.json", ".trials.jsonl" })
        {
            string path = Path.Combine(Path.GetTempPath(), "coz-calibration-check-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(path + suffix, "retained evidence");
                bool rejected = false;
                try { PhysicalCalibration.Run(path, () => throw new InvalidOperationException("Orphaned evidence must prevent setup.")); }
                catch (IOException) { rejected = true; }
                string otherSuffix = suffix == ".plan.json" ? ".trials.jsonl" : ".plan.json";
                Require(rejected && !File.Exists(path) && !File.Exists(path + otherSuffix)
                    && File.ReadAllText(path + suffix) == "retained evidence",
                    "An orphaned plan or journal must prevent execution without creating or replacing other evidence.");
            }
            finally
            {
                File.Delete(path);
                File.Delete(path + ".plan.json");
                File.Delete(path + ".trials.jsonl");
            }
        }
    }
}
