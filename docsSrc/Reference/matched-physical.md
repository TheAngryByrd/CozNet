# Matched physical workload

`--timer-physical <output.json> baseline|optimized` runs two dedicated CPU workers for a nominal two-second window.
This experimental route supplies physical measurements for the planned sampled-timer comparison.
It does not calculate prediction accuracy.

## Worker contract

`MatchedCpuWorkers` supplies the start gate, stop flag, checksum loop, and separate progress counters.
Both this route and `SampledTimerWorkload` use that implementation.
Each worker signals readiness before it waits for release.
The controller captures initial counters before it releases the workers.
Every completed batch must return `49995000` before its worker increments progress.

In `baseline` mode, both workers call `CpuWorkload.Run`.
In `optimized` mode, only the selected worker calls `CpuWorkload.RunOptimized`.
The peer always calls `CpuWorkload.Run`.
Both arithmetic methods run once before worker creation in both physical and sampled-timer modes.
This prepares the methods but does not establish steady-state warmup or calibrate their CPU cost.

The physical controller sleeps until the endpoint.
It installs no timer handlers and opens no perf session.
The sampled-timer controller retains its native registration, accounting, and retirement operations.
Those profiling operations remain part of the measured overhead.

## Settings and output

The process requires `DOTNET_TieredCompilation=0` and `DOTNET_ReadyToRun=0`.
It rejects an enabled startup profiler.
Mode parsing rejects any value other than `baseline` or `optimized` before starting workers.

`measurementCapture` uses the shared [measurement boundary contract](measurement-boundaries.md).
Physical global delay, credit, released wait, and active wait are zero.
Physical boundaries have no pending command.
The controller captures the endpoint before it stops and joins the workers.
`progress` records final counts after cleanup. Use the frozen boundary counts for measurement analysis.

The result includes separate worker join flags, retained failures, runtime identity, clock frequency, and nominal timestamps.
A setup, measurement, or join failure returns exit code one and retains its result when the output path is writable.
Both workers must complete progress for a successful run.
`predictionAccuracyEstablished` remains false.

## Limits

The worker and physical implementations use portable .NET APIs.
Runtime checks have exercised Windows x64 and Linux x64. Other platforms remain unverified.
The timer adapter remains Linux-specific.
[CPU calibration](physical-calibration.md) retains paired call-cost measurements with unresolved uncertainty.
Randomized prediction blocks and uncertainty-aware prediction analysis remain unimplemented.
Halving arithmetic iterations does not prove an exact 50-percent CPU-cost reduction.

The older `--physical` route retains its original topology for existing evidence scripts.
It is not the matched timer comparison route.
