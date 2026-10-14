# Sampled signal capacity experiment

The live Linux x64 controller generates causal credit and completes its request protocol.
The latest warm comparison passes one of three 50-percent capacity cases. It is not ready for prediction measurements.
The zero-percent case generates no credit or signal requests.

```powershell
dotnet run --project build/build.fsproj -- -t LinuxSampledSignalsVerify
```

The target intentionally fails when the capacity gate fails. It retains both subprocess results first.
Each process uses two arithmetic workers and one controller for two seconds.
Only the selected worker supplies one-millisecond nominal perf periods.
The controller drains samples and services peer debt continuously.
The original mode checks selected debt continuously. The new mode checks selected debt once per qualified sample batch.
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

The [continuous-cadence archive](data/sampled/run-20261014T005405569-a438d585.zip) contains environment metadata, source/binary hashes, both JSON measurements, logs, outcomes, and summary.
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

## Qualified sample cadence

The selected worker now checks debt after a batch contains a complete interval in the current sampling epoch.
Each qualified batch permits one check. A boundary-only observation does not permit a check.
Qualified zero-percent observations use the same rule, although they earn no credit.
See [the primary-source comparison](../../../docsSrc/Research/signal-sample-cadence.md).

The [cold cadence archive](data/sampled/run-20261014T010639929-eb2aa09f.zip) retains the first qualified-sample comparison.
Its 50-percent case passes the protocol checks but fails capacity.
It records 906 ms of credit, 57 selected sampling pauses, and 20.836919 ms of peak peer debt.
There are 1,812 positive-credit observations among 1,869 selected-method samples.
This run predates warmup and stage labels.

The [first warm archive](data/sampled/run-20261014T010902734-eed180e3.zip) retains an aborted 50-percent measurement.
The zero-percent case passes.
A 50 µs request produces 5.820913 ms of measured wait and a 5.821534 ms handler interval.
The acknowledgment arrives after 5.8254 ms. The handler interval exceeds the unchanged 5 ms limit.
Both workers join and the signal handler restores.
This run predates the explicit `failureBoundary` field.

## Three warm blocks

```powershell
dotnet run --project build/build.fsproj -- -t LinuxWarmSampleCadenceVerify
```

The [three-block archive](data/sampled/run-20261014T011141476-dd6ca4d2.zip) retains every case, including capacity failures.
Each block runs zero percent before 50 percent in separate processes.
Each process warms the same native workers and controller for 250 ms.
Fresh ledger and sample-stream state separate measurement from warmup.
The first measurement observation establishes the epoch baseline and earns no credit.

All six cases pass protocol checks. All three zero-percent cases pass capacity checks.
The table reports the 50-percent measurement windows. Wait totals exclude final settlement.

| Block | Credit (ms) | Selected wait (ms) | Peer wait (ms) | Peak selected debt (ms) | Peak peer debt (ms) | Capacity |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| 1 | 917 | 5.089101 | 921.703883 | 0.360064 | 0.933666 | Pass |
| 2 | 910 | 5.269121 | 915.327191 | 0.941477 | 2.020031 | Fail |
| 3 | 904.5 | 5.239817 | 909.371634 | 2.201593 | 2.748001 | Fail |

Selected sampling pauses are 43, 48, and 58.
Maximum handler intervals across each complete 50-percent process are 0.6009, 2.962554, and 4.950114 ms.
The target fails overall because blocks 2 and 3 exceed the observed debt limit.
These results do not establish stable capacity or prediction accuracy.

All 19 recorded source and binary hashes match the measured files.
Credits and live/final waits reconcile across 12 phases in six cases.
The verification also checks ledger retirement, new-stream baseline exclusion, and unique native request sequences.
All 17 archive members match their original files by SHA-256.

## Interpretation and next experiment

The large peer backlog occurs during startup.
In the continuous-cadence run, the first periodic snapshot arrives at about 33 ms.
The snapshot at about 43 ms shows 18.976209 ms of peer debt.
Debt falls below the limit before about 84 ms.
Controller JIT and startup cost are plausible causes. This experiment does not isolate them.

Later service repeatedly propagates native wait overshoot through the Linux delay ledger.
The selected thread pays that debt, which pauses sampling and advances its eligibility epoch.
Only 88 observations earn credit despite 984 samples attributed to the selected method.
The continuous-cadence data therefore expose both startup backlog and substantial sampling exclusion.
A small final debt does not erase the earlier capacity failure or establish valid sampling.

Qualified sample cadence recovers credit. Warmup reduces the startup backlog, but the three-block comparison still fails capacity.
Keep all failed runs and the existing limits.
Measure steady-state debt, controller service gaps, and wait overshoot separately before selecting another wait actuator.
The [service timing experiment](service-timing.md) adds request timestamps and peak snapshots. It identifies wait overshoot and long pending requests without native waits.
Do not subtract aggregate controller cost or final settlement from the earlier measurement window.
Forced-GC cases, matched baseline and zero-delay controls, debt-age analysis, and physical-change prediction checks remain incomplete.

See [the controller contract](../../../lode/profiling/sampled-signals.md), [signal transitions](transitions.md), and [the native experiment](README.md).
