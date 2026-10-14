# Scheduler capture in the sampled workload

The optional scheduler route captures the controller and both signal targets through a Linux x64 backend.
It leaves the signal handler and delay accounting unchanged.
The public engine remains serial and synchronous.

## Capture contract

Each owner opens its per-thread event through `ISchedulerEventSession`.
`LinuxSchedulerEventSession` uses a 96-byte perf attribute with `CLOCK_MONOTONIC` and final loss accounting.
A dedicated collector drains each 256 KiB ring and requests a one-millisecond sleep between drains.
Each stream has storage for 131,072 decoded records. Exhaustion fails capture.
The collector checks record sizes, thread identity, timestamps, and switch flags.
The analyzer checks alternating transitions and request boundaries before computing overlap.

The workload stops request delivery before capture cleanup.
It disables events, joins collectors, drains remaining records, and reads final loss counters before target retirement.
Scheduler errors fail the workload protocol independently of capacity failures.
The current tree also holds the retirement barrier if collector termination throws.
It disposes that barrier only after all targets join.
The deterministic fixtures below exercise this failure guard after the retained timing run.

```powershell
dotnet run --project build/build.fsproj -- -t LinuxSchedulerCollect
```

This command requires built workload and native signal binaries.
The runner snapshots those binaries before execution and records every child exit code.
The current runner fails after collecting every case if any child fails, a record is missing, snapshot hashes change, or analysis fails.
The retained timing run predates this runner gate. Its GC treatment remains a failure despite the earlier runner's successful exit.
The four cases use a fixed order and a 30-second process watchdog.
This is a capability capture, not a randomized overhead comparison.

## Retained run

The [archive](data/scheduler-capture/run-20261014T045332466.zip) includes capture sources, executable snapshots, raw workload records, and the later analyzer.
All 41 archived files match their retained originals.
The archive SHA256 is `14707f759b9385c6ecb1229501c3c0a3f7af3ab2906f8cd9eeb95694868491e9`.
The environment is Linux x64 on `5.15.153.1-microsoft-standard-WSL2`.
The build succeeds on Windows. The four workload processes execute in Linux containers.

| Case | Workload result | Controller records | Selected records | Peer records | Final lost records |
| --- | --- | ---: | ---: | ---: | ---: |
| No GC, 0% | Pass | 458 | 102 | 512 | 0 |
| GC, 50% | Capacity failure | 2,362 | 1,956 | 2,052 | 0 |
| GC, 0% | Pass | 1,262 | 388 | 3,034 | 0 |
| No GC, 50% | Pass | 2,666 | 1,808 | 126 | 0 |

All twelve streams pass the analyzer's integrity checks.
The GC treatment reaches 2,499,905 ns of peer debt and fails the unchanged 2,000,000 ns limit.
The failed run remains evidence. Capture success does not convert it into a passing causal experiment.

## Observed overlap

GC-treatment request 3688 performs no native wait.
The interval before its first handler clock reading is 3,119,930 ns.
Observed target switch-out intervals overlap 3,118,100 ns of that interval.
Observed controller switch-out intervals overlap only 58,403 ns of the same interval.
After the last handler clock reading, controller switch-out intervals overlap 850,722 ns of the 856,690 ns acknowledgment-observation interval.
This interval can include remaining handler execution and descheduling before completion publication.

At peak peer debt, request 3689 remains pending.
Its eventual pre-handler interval is 962,828 ns, with 960,647 ns overlapping an observed target switch-out interval.
That switch-out record has no preemption flag. This does not identify a dependency wait or other blocking cause.

These intervals use native monotonic phase timestamps and perf timestamps from the same requested clock domain.
The first handler timestamp follows handler metadata checks. It does not mark exact handler entry.
The scheduler timestamp marks a kernel hook. It does not establish an exact physical scheduling boundary.
Only complete observed switch pairs contribute overlap. Boundary fragments remain censored.

## Decoder and shutdown checks

The live backend uses `SchedulerRecordParser` for both switch and loss records.
Five checks cover direction, preemption, loss counts, truncation, length mismatches, identity, timestamp order, and invalid flags.
They run through `--verify-scheduler-records` on Windows and Linux.
The parser accepts equal timestamps and keeps loss counts separate from event IDs.

`SampledSignalAbortChecks.SchedulerShutdown` invokes the real workload with deterministic dependencies.
It forces an early workload failure and checks these four cleanup modes:

| Mode | Required result |
| --- | --- |
| Normal capture | Finish and dispose every capture before both targets retire. |
| Capture termination throws | Retain the error, hold both targets, and report incomplete teardown without backend restoration. |
| Capture creation fails | Preserve the missing capture and clean up all captures that were created. |
| Capture reports loss | Retain the loss, fail the protocol, and permit teardown after capture stops. |

All four fixtures pass on Windows and Linux.
These fixtures do not send native signals or exercise a real kernel collector timeout.
They verify workload lifetime and error propagation through the backend interface.

Eight Python checks verify analyzer pairing, censored fragments, final loss, malformed flags, ordered boundaries, and clipped overlap.
The analyzer still accepts all twelve streams from the retained timing run.

The [check archive](data/scheduler-capture/checks-20261014T050110900.zip) preserves the Linux executable, fixture sources, Windows and Linux outputs, and analyzer checks.
All 46 archived files match their retained originals.
The archive SHA256 is `9c6e7f031be903d7e940df51f4a1be0793bf91b0aeee6970c11af14a0ee5157f`.
The earlier Linux runner invocation failed its decoder command because PowerShell split one scalar argument into characters.
The corrected runner uses an explicit `string[]`. The failed runner evidence remains in `artifacts/scheduler-checks/linux/run-20261014T045949804`.

```powershell
dotnet run --project build/build.fsproj -- -t LinuxSchedulerVerify
python native/SchedulerChecks.py
```

The retained Linux fixture run invokes `native/VerifyLinuxScheduler.ps1` directly.
The FAKE build project compiles with both scheduler targets.

## Limits and next action

This run has no EventPipe suspension trace. It cannot identify whether CLR suspension caused the observed switch-outs.
Collector threads and event generation add overhead.
A separate [fixed-work comparison](scheduler-cost.md) measures busy-loop cost. It does not bound cost in this signal workload.
The final Linux check run also passes the decoder, all four shutdown modes, and both cost-workload startup-failure cases.
The [final check archive](data/scheduler-capture/final-checks-20261014T051450099.zip) preserves that executable and source snapshot.
It also contains Windows startup-failure records, six runner gate cases, and retained-evidence gate replay summaries.
All 80 archived files match their retained originals.
The archive SHA256 is `2c93de9da31cd114e5511bd5117e835bf5c94cf30d1172060fb44445f123f09a`.
Do not change debt semantics from these observations.
Use the evidence to assess intervention service and return to physical-prediction validation with the existing limits.

See [scheduler contracts](../../../lode/profiling/scheduler-records.md), [capability checks](switch-probe.md), and [abort trace evidence](abort-trace.md).
