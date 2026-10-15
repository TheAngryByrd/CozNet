# Physical calibration evidence

The interleaved protocol later generalized the shared loop bound while preserving the contiguous count of 50,000 calls.
The retained contiguous timings do not establish identical generated-code overhead for that refactor.

Run `20261015T004041293-546e85fe` retains two warmups and eight paired CPU measurements.
The exact order was saved before clock setup or timed work.
Each trial completed 50,000 checksum calls.
The Linux x64 process used `mcr.microsoft.com/dotnet/sdk:10.0.100` with tiering and ReadyToRun disabled.
The runner saved source and binary snapshots before execution and reported unchanged inputs afterward.

## Measurements

| Statistic | Value |
| --- | ---: |
| Mean paired CPU reduction | 0.5127666451837813 |
| Sample standard deviation | 0.04136159058717387 |
| Student-t interval half-width | 0.03457915507409129 |
| Distance from 0.5 plus half-width | 0.04734580025787263 |

The mean is 51.28 percent. The interval half-width is 3.46 percentage points.
The distance-plus-half-width from 50 percent exceeds the entire two-percentage-point prediction-error budget.
This result does not justify treating the physical change as an exact 50-percent optimization.
CPU-frequency effects and other causes of pair variation remain unresolved.

The measured interval includes call-loop and checksum work.
The statistical interval does not bound clock bias or measurement overhead.
No virtual percentage or prediction accuracy is established.
The runner completed all five declared processes: host layout, Linux layout, physical baseline, physical optimized, and calibration.
Successful process completion is separate from acceptable calibration uncertainty.

## Timer regression observation

Run `20261015T004511630-7f87596a` passed layout, zero-percent, and 50-percent cases with the shared generic arithmetic loop.
The treatment retained 945,500,000 ns of selected credit and 2,167 journals over a nominal 2.000000761-second window.
Observed selected and peer debt peaks were 321,278 ns and 1,499,934 ns.
Both captures completed and both native sessions closed.
This run predates the calibration driver's journal changes. The worker loop and native controller are unchanged.
This single passing run does not establish repeatability. Earlier failed capacity runs remain retained.

## Portable checks

Final Windows and Linux layout checks passed after the existing-output guard and setup-failure checks were added.
The live snapshot predates those additions, per-trial journaling, and build-script formatting. Its timed measurement loop and statistical analysis are unchanged.
Journal writes now occur between trials. The retained calibration does not measure their effect on timing context.
No additional calibration run replaced the retained result.
The source snapshot captures top-level workload C# files and selected build inputs, not a complete rebuild checkout.

| Check | Evidence protected |
| --- | --- |
| `OnlySelectedWorkerUsesOptimizedArithmetic` | Both modes and both slots invoke only the declared arithmetic. |
| `WrongChecksumCannotPublishProgress` | A bad batch fails before worker progress changes. |
| `FixedWorkRetainsExactReadings` | Every call and raw clock reading reaches the result. |
| `BadChecksumRetainsPartialWork` | Failure retains the completed prefix without a fabricated endpoint. |
| `InvalidClocksCannotCompleteMeasurement` | Negative counters, nonpositive intervals, and invalid frequency fail. |
| `EstimateRequiresEveryPlannedTrial` | Warmup failure, missing or duplicated work, and invalid records prevent estimates. |
| `PairedCpuReductionHasDeclaredInterval` | Exact synthetic pairs produce the independent expected mean, deviation, and interval. |
| `NegativeReductionsAreRetained` | Slower optimized work remains negative. |
| `DriverRetainsFailedSetupAndRejectsOverwrite` | Planning precedes setup, failed setup retains all unexecuted steps, and old results cannot be overwritten. |
| `DriverContinuesAndCheckpointsFailedTrial` | A failed trial does not remove later outcomes. Earlier rows are readable before each next measurement. |
| `OrphanedEvidencePreventsExecution` | A retained plan or journal prevents new work and remains unchanged. |
| `CheckpointFailureRetainsMeasuredTrial` | A failed checkpoint retains the measured trial, stops collection, and prevents an estimate. |

The console verifier emits no case counts. These names identify registered assertions in source.
Logs are under `artifacts/physical-calibration/`.

## Dispatch mutation

An isolated snapshot changed `if (slot == 0 && mode.Optimized)` to `if (mode.Optimized)`.
The mutant compiled with zero errors.
`--verify-matched-workers` failed in `OnlySelectedWorkerUsesOptimizedArithmetic` for optimized mode, slot 1.
The failure states that this slot must call only its declared arithmetic and publish one verified batch.
The live worktree never contained the mutant.
The snapshot source was restored afterward. Its compiled mutant binary is not a passing build.

This mutation establishes sensitivity to wrong peer dispatch. It does not prove complete test coverage.
See [the calibration reference](../../../docsSrc/Reference/physical-calibration.md) and [matched physical evidence](matched-physical.md).
