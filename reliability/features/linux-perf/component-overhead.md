# Component overhead

Injected probes are the largest measured overhead stage in this controlled loop.
The complete zero-speedup path costs 25.49% more per selected-thread batch than the baseline.
All 40 Linux workload cases pass their applicable checks.
The profiler and workload hashes remain unchanged throughout the run.

| Stage | Mean increase over baseline | Mean increase over previous stage | 95% interval half-width for previous-stage increase |
| --- | ---: | ---: | ---: |
| Baseline | 0% | — | — |
| Loaded profiler | 0.33% | 0.33% | 0.56 points |
| Injected probes | 16.59% | 16.21% | 2.07 points |
| Cooperative callbacks | 23.86% | 6.27% | 1.79 points |
| CPU collection | 25.49% | 1.32% | 1.54 points |

The loaded and collection intervals include zero. These measurements do not establish positive incremental costs for those stages.
The ratios are not additive cost shares. Each ratio has a different denominator.
The intervals use eight paired blocks and Student's t critical value `2.364624251`.
One machine and one workload do not establish general overhead bounds.

## Protocol

Eight shuffled blocks use seed `20261015` and two-second measurement windows.
Each block contains one run of every stage in a separate Linux x64 container.
Runtime perf maps remain enabled in all stages. Tiering and ReadyToRun remain disabled.
Every stage uses the same selected-thread loop and arithmetic workload.
Each batch performs 10,000 additions and returns `49995000`.
A second thread performs the same work until the measurement finishes.

The baseline has no startup profiler.
The loaded stage selects the absent instrumentation assembly. It requires profiler status 1 and zero rewritten methods.
The probe stage rewrites `CpuWorkload.Run` with registered maps and branch placement at interval 1024.
It requires status 2, one rewritten method, and ten helper calls per batch on each thread.
These probes include helper calls and counters but have no cooperative callback binding.

The callback stage uses the normal cooperative runtime and a ten-millisecond thread-CPU cadence.
It supplies no CPU-event source and permits only zero-speedup completion without samples.
The collection stage adds Linux task-clock events, the collector thread, event decoding, and zero-speedup sample processing.
It requires live samples and preserves event periods, selected counts, and zero causal delay.
Special no-sample completion rejects a nonzero speedup or any applied event.
Ordinary sampled completion still rejects a run without live samples.

Cost is `experimentMilliseconds / completedBatches` for the selected thread.
Measurement ends before event shutdown and final settlement. Worker counts remain diagnostic and do not enter the denominator.
The plain worker and cooperative worker use their respective existing control paths.
The stage differences therefore include control-path and scheduling effects, not only individual instruction costs.
No service-time correction enters the result.

## Platform evidence

[Linux records](data/overhead/linux/) retain all cases, order, outcomes, environment, and summary.
The runner also retains subprocess logs under `artifacts/linux-overhead/`. Successful logs are empty.
[Windows records](data/overhead/windows/) verify baseline, loaded-only, and probe-only stages with bounded 500-millisecond runs.
Those three runs verify behavior. They do not establish a Windows performance comparison.
Callback and collection launch support remains Linux x64 only.
The shared harness uses `IThreadCpuClock` and `ICpuEventSession` for those platform dependencies.
macOS remains unverified.

```powershell
dotnet run --project build/build.fsproj -- -t LinuxOverheadVerify
dotnet run --project build/build.fsproj -- -t WindowsOverheadVerify
dotnet tests/CozNet.LinuxPerf/bin/Release/net10.0/CozNet.LinuxPerf.dll --verify-overhead
```

The completion checks pass on Windows and Linux.

| Requirement | Evidence |
| --- | --- |
| Zero-speedup callbacks can finish without events | `OverheadChecks.ZeroSpeedupWithoutEventsCompletes` |
| A treatment cannot use no-sample completion | `OverheadChecks.NonzeroSpeedupWithoutEventsFails` |
| No-sample completion rejects an event | `OverheadChecks.UnexpectedEventFails` |
| The normal sampled gate remains mandatory | `OverheadChecks.OrdinaryCompletionStillRequiresEvents` |

## Decision

Do not subtract these stage means from experiment duration.
The measurements describe relative workload costs, not independent service intervals.
The prior physical-prediction failures remain unresolved.
Investigate a lower-cost intervention mechanism while preserving the existing cooperative fallback and accuracy gate.
A signal-based backend needs separate GC, thread-lifetime, signal-ownership, and delay-bound checks.
See [signal research](../../../docsSrc/Research/native-signal-intervention.md) and [the attribution sensitivity result](attribution-sensitivity.md).
