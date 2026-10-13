# Serial workload evidence

Date: 2026-10-13. Configuration: Release, .NET 10.0.12, Windows 10.0.26200, 24 logical processors.
The current run used source tree `e040c7bbd9171f3b7b5f633f41e2cbf1d1a6252b` before the initial commit.
The harness uses 21 blocks, five warmups, intervention seed 1729, and real-change seed 2718.
Each checksum has 2,000,000 iterations. Each run verifies the same output.

```sh
dotnet run --project samples/CozNet.Serial -c Release -- artifacts/serial-second
```

## Current result

| Region reduction | Predicted program reduction | Measured program reduction | Error, percentage points |
| --- | --- | --- | --- |
| 0% | -0.25% | 0.20% | 0.45 |
| 25% | 12.50% | 11.73% | 0.77 |
| 50% | 26.02% | 23.87% | 2.15 |

The run exited with code `1`. The 50% case exceeded the proposed two-percentage-point target.
The 126 observations recorded no GC collections. Minimum marked coverage was 99.77%.
Block estimates varied substantially. These facts do not isolate the cause of the discrepancy.
Scheduling, processor state, and spinning remain possible causes that need separate experiments.

[Current raw data](data/current.json) contains all interventions, observed delays, GC counts, and real-change pairs.
No observations were excluded. This run does not establish the requested accuracy for all three cases.

## Preliminary evidence

[Preliminary raw data](data/preliminary.json) comes from the same workload before a timestamp conversion correction.
Its three errors were 1.57, 0.03, and 0.02 percentage points. The original process completion code was not retained.
Do not count this preliminary run as validation of the final source tree.
Deterministic tests exposed truncation in floating-point conversion to `TimeSpan`.
The current implementation rounds to the nearest `TimeSpan` tick.

## What this establishes

The engine executes real interventions and produces inspectable data.
Sixteen deterministic tests pass for configuration, accounting, randomized schedules, and session lifecycle.
They do not establish general physical accuracy. Windows is the only platform exercised locally.
The real workload is serial and CPU-bound. It does not cover locks, queues, async, I/O, or native sampling.

See [the model](../../../docsSrc/Explanations/Background.md) and [the roadmap](../../../lode/plans/roadmap.md).
