# Physical CPU calibration

`--calibrate-physical <output.json>` measures the original and optimized checksum calls with the calling thread's CPU clock.
The command currently selects `LinuxThreadCpuClock` and requires Linux x64.
The measurement core accepts `IThreadCpuClock` and `TimeProvider` for other platform backends and controlled checks.
`--verify-physical-calibration` runs the focused portable checks without collecting a live calibration.

## Plan and measurement

The process writes `<output.json>.plan.json` before clock setup or timed work.
The plan records runtime identity, seed `20261014`, and the exact order.
Two fixed warmup trials precede eight paired trials.
Each pair contains baseline and optimized work. `System.Random.Next(2)` chooses which mode runs first.
The saved order is authoritative. The seed does not reproduce operating-system scheduling or CPU frequency.

Each trial performs 50,000 calls on one thread.
Each call must return `49995000`. Only verified calls contribute to completed work and the checksum aggregate.
The wall-clock interval contains both CPU-clock reads and the work.
Measurements retain raw CPU and wall readings, clock frequency, completed calls, checksum, and any failure.
The process requires disabled tiering and ReadyToRun, with no startup profiler.

The worker harness and calibration use `OriginalChecksumArithmetic` and `OptimizedChecksumArithmetic`.
Value-type arithmetic implementations allow controlled dispatch checks without adding per-batch test callbacks to live workers.

## Analysis

Analysis requires both warmups and all sixteen measured trials in the declared order.
A failed, missing, duplicated, reordered, incomplete, or invalid trial prevents an estimate.
The driver retains trial failures and continues the remaining declared trials.
After each trial, the driver appends a JSON record to `<output.json>.trials.jsonl` and flushes it to disk.
The flush occurs outside the measured interval and before the next trial.
Storage failure stops collection. A process kill can leave the current row incomplete, but earlier flushed rows remain available.
If the final result remains writable, it retains the measured trial whose checkpoint failed and suppresses the estimate.
An incomplete journal cannot replace the final result or establish a complete calibration.
Setup failure retains the list of unexecuted steps.
Existing output, plan, or journal files prevent reuse of the same evidence path.

For each pair, CPU reduction is `1 - optimized CPU duration / baseline CPU duration`.
The result reports the mean, sample standard deviation, and interval half-width.
The half-width uses Student-t critical value `2.364624251` with seven degrees of freedom.
`AbsoluteHalfMismatchUpper` is `abs(mean - 0.5) + interval half-width`.
Negative reductions remain negative.
Warmup values do not enter the estimate, but failed warmup invalidates the run.

`calibrationComplete` means the declared measurements completed and passed their structural checks.
It does not prove that the optimization matches 50 percent.
`predictionAccuracyEstablished` and `clockAccuracyEstablished` remain false.

## Limits

CPU duration includes the call loop, checksum checks, and some measurement overhead.
It does not isolate CPU time inside the selected method.
Journal writes occur between trials and can affect the workload's timing context.
The Student-t interval describes pair variation under its statistical assumptions.
It does not bound clock bias, measurement overhead, or all effects of CPU frequency changes.
No virtual percentage is selected automatically from this estimate.

Portable checks run on Windows x64 and Linux x64. Live thread-CPU calibration has run on Linux x64 only.
The first retained result has a 4.73-percentage-point distance-plus-half-width from 50 percent.
That value alone exceeds the complete two-point prediction-error budget.
The result does not justify a nominal 50-percent physical comparison.
See [the evidence](../../reliability/features/native-signal/physical-calibration.md) and [the procedure](../How_Tos/run-physical-calibration.md).
