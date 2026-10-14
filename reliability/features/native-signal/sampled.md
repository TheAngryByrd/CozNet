# Sampled signal capacity experiment

The live Linux x64 controller generates causal credit and completes its request protocol.
The 50-percent case fails the observed unpaid-debt limit. It is not ready for prediction measurements.
The zero-percent case generates no credit or signal requests.

```powershell
dotnet run --project build/build.fsproj -- -t LinuxSampledSignalsVerify
```

The target intentionally fails when the capacity gate fails. It retains both subprocess results first.
Each process uses two arithmetic workers and one controller for two seconds.
Only the selected worker supplies one-millisecond nominal perf periods.
The controller drains samples and services both signal slots continuously.
Platform operations remain behind the event-session, signal-backend, and thread-clock interfaces.

Limits selected before execution:

| Observation | Limit |
| --- | ---: |
| Requested wait | 50 µs |
| Measured handler interval | 5 ms |
| Acknowledgment | 100 ms |
| Observed unpaid debt per thread | 2 ms |
| Process watchdog | 30 seconds, then forced termination after 2 seconds |

The debt limit equals 0.1 percent of the two-second window.
Debt is checked after each service iteration and at the measurement boundary.
This does not prove a continuous bound or two-percentage-point prediction accuracy.

## Retained evidence

The [current raw archive](data/sampled/run-20261014T005405569-a438d585.zip) contains environment metadata, source/binary hashes, both JSON measurements, logs, outcomes, and summary.
All 17 recorded source and binary hashes matched the measured files after execution.
Each archive member was compared with its original file using SHA-256.
Raw sample credits reconcile with the measurement credit total.
Raw acknowledgment waits reconcile with both the live and final wait totals.

| Observation | Zero percent | 50 percent |
| --- | ---: | ---: |
| Selected method samples | 1,895 | 984 |
| Positive-credit observations | 0 | 88 |
| Credited nominal CPU time | 0 ms | 44 ms |
| Requests, including final settlement | 0 | 15,913 |
| Selected sampling pauses | 0 | 7,725 |
| Selected measured wait in window | 0 ms | 846.638358 ms |
| Peer measured wait in window | 0 ms | 890.583017 ms |
| Maximum observed selected debt | 0 ms | 1.437098 ms |
| Maximum observed peer debt | 0 ms | 20.085964 ms |
| Peer debt at measurement boundary | 0 ms | 0.055341 ms |
| Maximum acknowledgment | — | 10.3817 ms |
| Maximum measured handler interval | — | 1.995094 ms |

The 50-percent controller consumes 1,981.5965 ms of thread CPU during the two-second window.
Both workers preserve their checksums and complete native retirement, thread join, and signal restoration.
The event source reports no lost samples or throttling.
One sampled address is unmapped in the 50-percent case and earns no credit.

The [earlier raw archive](data/sampled/run-20261014T005118942-224177dd.zip) also fails the capacity limit.
Its maximum observed peer debt is 19.781672 ms.
It predates explicit registered-handle fields in the boundary record.
Its final debt display includes a retired handle and must not be treated as unresolved live ownership.
The current record reports registration state and zero debt for retired handles.

## Interpretation and next experiment

The large peer backlog occurs during startup.
In the current run, the first periodic snapshot arrives at about 33 ms.
The snapshot at about 43 ms shows 18.976209 ms of peer debt.
Debt falls below the limit before about 84 ms.
Controller JIT and startup cost are plausible causes. This experiment does not isolate them.

Later service repeatedly propagates native wait overshoot through the Linux delay ledger.
The selected thread pays that debt, which pauses sampling and advances its eligibility epoch.
Only 88 observations earn credit despite 984 samples attributed to the selected method.
The current data therefore expose both startup backlog and substantial sampling exclusion.
A small final debt does not erase the earlier capacity failure or establish valid sampling.

Add controller warmup before the next measurement window.
Keep the failed cold runs and the existing limits.
Then measure steady-state debt, sampling exclusions, and wait overshoot separately.
Do not subtract aggregate controller cost or final settlement from the earlier measurement window.
Forced-GC cases, matched baseline and zero-delay controls, debt-age analysis, and physical-change prediction checks remain incomplete.

See [the controller contract](../../../lode/profiling/sampled-signals.md), [signal transitions](transitions.md), and [the native experiment](README.md).
