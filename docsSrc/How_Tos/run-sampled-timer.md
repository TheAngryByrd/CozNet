# How to run the sampled timer experiment

This procedure checks cold timer delivery from real CPU samples on Linux x64.
It does not verify prediction accuracy or extend the public engine's supported workloads.

## Prerequisites

Use a Linux Docker engine and the existing `coznet-native-build:10.0.100` compiler image.
The runner also requires `mcr.microsoft.com/dotnet/sdk:10.0.100`.
The kernel must permit per-thread `perf_event_open` calls.
The sampled containers disable Docker's seccomp filter for these calls.
They retain a read-only root filesystem, no network, and dropped capabilities.

## Run and inspect

1. Run the repository target:

   ```powershell
   dotnet run --project build/build.fsproj -- -t LinuxSampledTimer
   ```

2. Open the new directory under `artifacts/timer-sampled/`.
3. Read `summary.json` and both `sampled-0.json` and `sampled-50.json`.
4. Check `failures`, `peaks`, `pending`, and worker lifecycle fields before interpreting sample credit.
5. Retain failed records with their manifest and logs.
6. Inspect `sampleClock.start`, `sampleClock.end`, and `sampleClock.rejection` for clock evidence.
   A true `observedMappingConsistent` value does not establish prediction accuracy.
7. Inspect `cpuCounters`, including its four endpoint readings, `enabledCountRange`, and source failure flags.
   Compare cumulative sample counts separately from nominal periods. See the [counter reference](../Reference/cpu-counter-evidence.md).

The runner executes both percentages even when the first workload fails.
It checks source and binary hashes after both processes finish.
Each process has a 20-second watchdog.
The measurement window is two seconds. Setup and shutdown remain outside that window.

See [the sampled timer reference](../Reference/sampled-timer.md) for accounting and output fields.

## Capture controller phases

Run this target when you need to locate controller startup cost:

```powershell
dotnet run --project build/build.fsproj -- -t LinuxSampledTimerPhases
```

Read `controllerPhases` in each workload record.
Check `Completed` before you compare CPU and elapsed intervals.
Use the manifest's `controllerPhases` field to distinguish diagnostic runs from ordinary capacity runs.

## Exercise managed startup before measurement

Run the initialized diagnostic:

```powershell
dotnet run --project build/build.fsproj -- -t LinuxInitializedTimer
```

Check the separate `initialization` record before interpreting live samples or timer journals.
Compare completed phase intervals with a cold diagnostic run.
Retain capacity and cleanup failures from both modes.

## Exercise native receipt handling before measurement

Run the fully prepared diagnostic:

```powershell
dotnet run --project build/build.fsproj -- -t LinuxPreparedTimer
```

Check `transportInitialization.Passed` and `transportInitialization.Released` before interpreting live results.
Inspect the separate setup observations if either field is false.
The live measurement does not start after failed native setup.

## Compare a shorter initial timer interval

Run the 10 µs initial-interval variant:

```powershell
dotnet run --project build/build.fsproj -- -t LinuxFastTimer
```

Check `initialIntervalNanoseconds` in the manifest and each workload record.
Compare with retained `LinuxPreparedTimer` evidence, which uses 1 ms initial delivery.
Both variants keep the same debt and receipt limits.

To measure capacity without continuous phase diagnostics, run:

```powershell
dotnet run --project build/build.fsproj -- -t LinuxFastTimerCapacity
```

Confirm that the manifest has `controllerPhases = false` and that workload phase fields are null.
The result still includes samples, timer journals, accounting boundaries, and failures.

## Capture scheduler events without forced GC

To capture scheduler events without forced GC, run:

```powershell
dotnet run --project build/build.fsproj -- -t LinuxFastTimerSchedulerNoGc
```

Check `sampledGc = false` in the manifest and `forceGc = false` in each workload record.
Each outcome must use `--sampled-timer-scheduler` with the `no-gc` argument.
Check scheduler termination and loss counters before interpreting switched-out intervals.
This mode retains ordinary runtime GC activity. It does not disable GC.
It keeps the same initialization, timer interval, debt limits, and handler limits as `LinuxFastTimerCapacity`.
The [retained no-forced-GC capture](../../reliability/features/native-signal/timer-scheduler-no-gc.md) includes a failed treatment and its scheduler analysis.

## Check sampled capacity during forced GC

Run the forced-GC capacity cases:

```powershell
dotnet run --project build/build.fsproj -- -t LinuxFastTimerGc
```

Check `gcJoined`, `gcReachedMeasurement`, `gcError`, and each collection interval.
Confirm that `gcObservations` contains a complete collection inside the recorded measurement window.
Retain debt failures even when GC reach and cleanup pass.

## Inspect a debt failure

To capture controller and target scheduling, run:

```powershell
dotnet run --project build/build.fsproj -- -t LinuxFastTimerScheduler
```

Check `schedulerStopped` and all three `schedulerCaptures` entries before you interpret switch records.
Require zero loss, no capture failures, and distinct thread identities in the same process.
Compare native timer intervals only within the capture's `EnableAfter` and `DisableBefore` bounds.
Retain failed workload results. Scheduler capture adds observer cost.

1. Find the first `boundaries` entry with debt above 2,000,000 ns.
2. Find the slot's journal whose submission and observation timestamps enclose that boundary.
3. Check the journal's stage, generation, submission status, and confirmed child waits.
4. Subtract the first child's `Armed` timestamp from its `Started` timestamp.
5. Check sample receipt spacing and forced-collection overlap with the managed command interval.

Use `timestampFrequency` to convert managed timestamp differences into seconds.
Compare native child timestamps within their native clock domain.
Do not subtract perf sample timestamps from managed timestamps without clock alignment evidence.
Use the first excessive boundary to locate the failure. Exception handling can delay `failureBoundary`.

A long arm-to-handler interval does not identify its scheduling or delivery cause.
See the [retained pending-command analysis](../../reliability/features/native-signal/timer-sampled.md#pending-command-at-the-first-debt-violation).

## Compare scheduler capture cost

Run the paired zero-percent comparison:

```powershell
dotnet run --project build/build.fsproj -- -t LinuxTimerSchedulerCost
```

Confirm that `summary.json` retains all 16 cases and reports unchanged inputs.
Check workload failures, GC reach, cleanup, and scheduler integrity before computing paired throughput ratios.
Use `measurementProgress` and its capture brackets. Do not use the final `progress` counters, which include shutdown work.
Keep startup time in the measurement. Do not compare shortened failed cases as completed two-second runs.
See [the measurement fields and limits](../Reference/sampled-timer.md#scheduler-cost-comparison).
