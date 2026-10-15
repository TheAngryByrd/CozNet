# Interleaved CPU calibration

`--calibrate-physical-interleaved <output.json>` runs protocol `interleaved-control-v1` with the Linux x64 thread CPU clock.
The measurement core accepts `IThreadCpuClock` and `TimeProvider` for portable controlled checks and future platform backends.
`--verify-interleaved-calibration` runs seven portable checks without live calibration.

## Declared plan

The process saves `<output.json>.plan.json` before clock setup.
The plan contains one warmup block and eight measured blocks, with seed `20261015`.
Each block has 100 rounds. Each round contains baseline, optimized, and control segments in a saved Fisher-Yates order.
Each segment performs 500 verified checksum calls. Each mode performs 50,000 calls per block.
The control calls a separate non-inlined constant-return method through the same measurement loop.
Tiered compilation and ReadyToRun must be disabled. A startup profiler must not be enabled.

## Records and failures

Each segment retains its declared identity, CPU readings, wall readings, clock frequency, completed calls, checksum, and failure.
Ordinary measurement failures do not stop later segments.
The process flushes each completed block to `<output.json>.blocks.jsonl` outside the measured segments.
Persistence failure stops collection and suppresses the estimate, including failure after the last block.
The final result retains collected blocks, failures, and unexecuted blocks when the result path remains writable.
An incomplete journal cannot establish a completed series.
A process interruption can lose completed segments in the current, unflushed block. Earlier flushed blocks remain available.
Existing result, plan, trial journal, or block journal files prevent reuse of the evidence path.

Analysis requires the complete declared order, valid work, and consistent cumulative clocks.
Each CPU duration must fit within its enclosing wall interval.
Warmup must pass these checks, but does not enter the statistics.

## Block statistics

For each measured block, `B`, `O`, and `C` are summed baseline, optimized, and control CPU durations.
The analyzer requires `0 < C < min(B, O)`.

| Field | Definition |
| --- | --- |
| `RawReduction` | `1 - O / B` |
| `SensitivityLower` | `1 - O / (B - C)` |
| `SensitivityUpper` | `1 - (O - C) / B` |
| `MeanReduction` | Mean of the eight block reductions |
| `StandardDeviation` | Sample deviation of the eight block reductions |
| `IntervalHalfWidth` | `2.364624251 * StandardDeviation / sqrt(8)` |
| `MaxObservedOverheadShift` | Largest distance from a block reduction to either sensitivity endpoint |
| `DistancePlusIntervalAndSensitivity` | `abs(MeanReduction - 0.5) + IntervalHalfWidth + MaxObservedOverheadShift` |

Negative reductions remain negative. Raw durations remain unchanged.
The statistical sample has eight blocks, not 800 rounds.
`calibrationComplete` reports structural completion only.
`predictionAccuracyEstablished`, `clockAccuracyEstablished`, and `overheadBoundEstablished` remain false.

See [the procedure](../How_Tos/run-interleaved-calibration.md) and [the interpretation limits](../Explanations/calibration-sensitivity.md).

## Observed platform checks

Portable checks passed on Windows x64 and Linux x64. Live calibration completed on Linux x64 only.
The first series reported a combined distance, interval, and sensitivity of `0.009432048437511635`.
This does not establish prediction accuracy. See [the retained evidence](../../reliability/features/native-signal/interleaved-calibration.md).
