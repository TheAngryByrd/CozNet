# Native service initialization comparison

Initialization reduces first-use controller cost in these captures. It does not establish service capacity.
Both treatments still fail the unchanged 2 ms lease capacity bound during warmup.
Both zero-speedup cases pass. All targets join and native sessions close.

| Initialization | Original run under `artifacts/linux-sampled-service/` | Treatment debt at failure |
| --- | --- | ---: |
| [Managed paths](managed/summary.json) | `20261014T164705380-80dc6b54` | 2,499,928 ns |
| [Managed paths and native transport](transport/summary.json) | `20261014T165025095-064d0656` | 2,098,793 ns |

Each manifest records unchanged source and binary hashes during its run.
The retained timing runs predate the target-lifetime fix and its check wiring.
`UnresolvedDeliveryKeepsTargetAlive` checks that fix separately. No timing run was repeated for it.
The [final check records](final-checks/manifest.json) identify the tested sources and binaries.
Target lifetime, accounting, and both initialization-isolation fixtures pass on Windows and Linux.
The existing pending-request check now uses the new lifetime contract, including preservation of a target body failure.
Both retain the prior 50 µs request, 5 ms handler, 100 ms receipt, and 2 ms debt limits.
Neither run forces GC or tests physical prediction accuracy.

## Separate initialization records

The managed initializer calls the controller's actual drain, prepare, and complete functions.
It supplies one synthetic perf record and two synthetic service journals.
The fixed path credits 40,000 ns, settles 70,000 ns peer wait and 30,000 ns selected wait, and leaves no pending ownership.
The `initialization` object retains these records separately from live samples and service journals.

The transport initializer runs after native target registration while both workers wait at their start gate.
It uses synthetic credit to submit one real native peer command.
The command must return one deferred child with zero measured wait.
The `transportInitialization` object retains that command and its receipt.
Accounting resets before live sampling. Native command numbering continues.

`InitializationRetainsSeparateEvidenceAndResetsLiveState` passes for zero and 50 percent on Windows and Linux.
The Windows records predate the transport initializer and exercise the managed initializer with an injected post-initialization failure.
The Linux records exercise the same isolation check after the transport code was added.
The live Linux zero-speedup result also retains one native setup command, zero live service commands, and zero live credit.

## Phase evidence

The managed-only run's first live drain takes 0.974588 ms, compared with 28.340907 ms in the earlier uninitialized phase capture.
Its first native peer completion still takes 3.903 ms CPU within 3.904718 ms elapsed.
The next completion takes approximately 0.03 ms.

After native transport initialization, two completed peer preparation phases take 2.368511 ms and 2.257352 ms elapsed.
Their controller CPU times are 0.0436 ms and 0.2739 ms.
This separates the remaining delay from the predominantly CPU-bound first-use costs.
It does not distinguish scheduling, native lock acquisition, or runtime suspension.
The later failed preparation includes exception construction and cannot measure normal submission cost.

The next investigation must separate native command handoff from controller scheduling and runtime suspension.
These failed captures remain part of the evidence. No unchanged timing workload was rerun to obtain a pass.

See [the startup JIT evidence](../service-startup/README.md) and [the experiment guide](../../../../docsSrc/How_Tos/run-native-sampled-service.md).
