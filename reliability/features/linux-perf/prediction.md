# Sampled prediction against physical work

All four retained runs fail the two-percentage-point accuracy gate.
The two-second run reduces mean error, but its interval remains too wide.
This evidence does not establish general prediction accuracy.

## Protocol

The source baseline is `a62c9cd`, plus the prediction runner and cadence changes in this change.
Eight blocks use seed `20261013` and a Fisher–Yates shuffle within each block.
Each block contains zero-speedup profiling, 50-percent profiling, physical baseline work, and physical optimized work.
Each case runs in a separate Linux x64 container with a 30-second process limit.
The runtime is .NET 10.0.0. Each archive records the image identity and kernel.
Tiered compilation and ReadyToRun are disabled.

Both profiled cases use `linux-nanosleep` and a 10-millisecond thread CPU cadence.
The physical cases use the same companion CPU workload, without sampling or cooperative probes.
The selected method sums integers from zero through 9999.
The physical optimization replaces additions from 5000 through 9999 with their sum, `37497500`.
Both versions return `49995000`. The runner verifies progress against this checksum.

The experiment interval ends before sampler shutdown and final settlement.
The profiled cost is `(experiment milliseconds - live global delay milliseconds) / completed batches`.
The physical cost is `experiment milliseconds / completed batches`.
Prediction is `1 - profile-half cost / profile-zero cost`.
Actual reduction is `1 - physical-optimized cost / physical-baseline cost`.
Each paired error is prediction minus actual reduction.

The interval uses the sample standard deviation of eight paired errors.
The 95-percent Student-t critical value is `2.364624251`, with seven degrees of freedom.
Acceptance requires `abs(mean error) + interval half-width <= 0.02` and a positive mean actual reduction.
This interval assumes independent block errors. It does not account for systematic measurement bias.
Repeated diagnostic runs are not independent confirmation of accuracy.

## Retained results

| Run suffix | Window | Cadence implementation | Prediction | Actual | Mean error | Interval half-width | Mean overhead |
| --- | ---: | --- | ---: | ---: | ---: | ---: | ---: |
| `bb1920ef` | 500 ms | CPU clock at every probe | 45.04% | 49.49% | -4.46 pp | 2.88 pp | 11.66% |
| `773c7b13` | 500 ms | Elapsed gate with per-probe conversion | 46.46% | 48.67% | -2.21 pp | 1.93 pp | 4.52% |
| `67507a04` | 500 ms | Cached elapsed threshold | 46.79% | 49.35% | -2.56 pp | 0.98 pp | 4.16% |
| `29f7032e` | 2000 ms | Cached elapsed threshold | 49.05% | 49.55% | -0.50 pp | 2.04 pp | 1.41% |

The first three configurations differ in the cadence implementation.
The final configuration changes the measurement duration and records it in each result.
The threshold rounds down, so the elapsed gate can permit an early CPU read.
Only CPU time can make a cadence check due.
The common path reads an elapsed timestamp and compares it with the cached threshold.

The last run has a gate value of 2.54 percentage points and therefore fails.
Its physical reductions range from 46.94% to 52.58% across blocks.
The overhead estimate also includes differences between separate process runs.
Negative block overhead does not demonstrate that profiling improves execution speed.
Further work must separate scheduling variance, startup effects, probe cost, and sample-cutoff effects.
Do not discard blocks or increase the acceptance limit to obtain a passing result.

`data/prediction/` retains a ZIP archive for each complete run.
Each archive contains all raw case records, randomized order, environment, and summary.
Adjacent summary files permit inspection without archive extraction.
The first three runs predate explicit duration metadata and use a fixed 500-millisecond work window.

## Platform checks and reproduction

```sh
dotnet run --project build/build.fsproj -- -t LinuxPredictionVerify
```

The target defaults to 2000 milliseconds. The script accepts `-MeasurementMilliseconds 500` for the shorter configuration.
PowerShell 7 and Docker supply the host-side interfaces. Only a Windows host has been verified.
Linux sampling, the CPU clock, and native waits require Linux x64.
`IThreadCpuClock`, `IWaitBackend`, and `TimeProvider` permit replacement of platform timing implementations.
The physical reference uses only managed .NET APIs and runs before the Linux guard.

Portable cadence and collector checks passed on Windows x64 and in the Linux runs.
Both physical modes passed checksum checks on Windows x64 with .NET 10.0.12.
The Windows records use two-second windows and remain under `data/prediction/`.
They establish execution portability only. They do not establish Windows sampled prediction accuracy.
macOS and ARM64 remain unverified.

See [the backend contract](../../../lode/profiling/linux-perf.md) and [the wait comparison](wait-comparison.md).
