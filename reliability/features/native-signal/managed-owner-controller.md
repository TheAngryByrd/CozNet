# Managed owner/controller checks

This report covers managed integration on source base `f4116d7de88dd1f77cb601a0f78dd61b8c01853f` with the staged integration changes.
Each capture retains source and binary hashes in its manifest.
The workload targets .NET 10 and uses the existing CLR profiler and cooperative native library.
No random generator or scheduler replay is involved.
Current host inventory has .NET 10.0.13 for the workload's .NET 10 framework requirement, with no `DOTNET_ROLL_FORWARD` override.
This is installation evidence, not an in-process runtime field from the completed cases.
The captured Linux image has .NET 10.0.0, confirmed by querying its retained image digest.

## Controlled execution

Each matrix runs five cases at probe intervals 1, 64, 256, and 1024.
The cases are paid/reuse, before-submit invalidation, before-poll invalidation, before-receipt invalidation, and after-settlement invalidation.
Each case uses one target and a separate process.

| Platform and mode | Capture under `artifacts/` | Result |
| --- | --- | --- |
| Windows owner/controller | `owner-controller-managed-host/20261016T033029398-15522068` | 20/20, unchanged inputs |
| Linux owner/controller | `owner-controller-managed-linux/20261016T033334372-8a9ff203` | 20/20, unchanged inputs |
| Windows separate controller | `owner-controller-default-host/20261016T033603585-7e3cd5b7` | 20/20, unchanged inputs |
| Linux separate controller | `owner-controller-default-linux/20261016T033932563-35496063` | 20/20, unchanged inputs |

All four runners returned zero. Their deterministic managed checks and 16 native checks also passed.
The paid cases settle two independent generations.
Before-submit rejection creates no native ownership.
Before-poll rejection retains a cancelled child with zero wait.
Before-receipt rejection preserves the unsettled reservation while cleanup retains the paid native evidence.
After-settlement rejection preserves settlement but rejects final experiment completion.

The enabled captures preceded the runner's switch to the shared outcome reader.
All 40 enabled records were then replayed through that reader with matching and mismatched ownership expectations.
Matching expectations passed. Mismatched expectations retained errors.
The default captures execute the final shared reader in the runner.

## Commands and deterministic checks

```powershell
./native/VerifyCooperativeInterop.ps1 -Platform Host -BoundCooperative -OwnerController
./native/VerifyCooperativeInterop.ps1 -Platform LinuxContainer -BoundCooperative -OwnerController
./native/VerifyCooperativeInterop.ps1 -Platform Host -BoundCooperative
./native/VerifyCooperativeInterop.ps1 -Platform LinuxContainer -BoundCooperative
```

The executions used separate `EvidenceDirectory` values from the table.
Each managed case has a 25-second container timeout or a 30-second host timeout.
Cleanup polling has its existing two-second watchdog. Accounting limits were not relaxed.

| Requirement | Executed check |
| --- | --- |
| Default mode and unsupported combinations | `ModesPreserveDefaultAndRejectUnsupportedCombinations` |
| Malformed modes and invalid slots before native open | `InvalidModesAndReservedSlotsRejectBeforeNativeOpen` |
| Valid mode survives selected replay | `test_owner_controller_replays_with_explicit_compatible_modes` |
| Forged or incompatible activation fails replay | `test_owner_controller_rejects_incompatible_or_forged_modes` |
| Legacy records retain separate-controller interpretation | `test_legacy_and_disabled_owner_controller_remain_separate` |
| Requested mode controls runner acceptance | `BoundSampledOutcomeChecks.ps1` ownership cases and the 40-record replay |

`BoundSelectedPaymentAnalysisChecks.py` passed all 91 checks.
The managed checks ran through `--verify-bound-cooperative-managed` with exit zero.
Existing receipt interval checks retain the exact 100 ms boundary and reject invalid or over-limit intervals.

## Limits

The controlled matrices do not exercise sampled two-thread payment. The separate diagnostic below covers that path.
They do not measure controller savings, peer-service latency, or prediction accuracy.
They do not inject a release failure after accepted ledger settlement.
The sampled acceptance, receipt-interval, and release sequence remains unchanged in source.
Scheduler capture and dispatch timing remain unsupported with owner/controller mode.
No macOS execution was performed.

See [the managed reference](../../../docsSrc/Reference/owner-controller.md)
and [the native evidence](cooperative-owner-controller.md).


## Sampled diagnostic

The declared plan is `artifacts/owner-controller-sampled/20261016T034508507-plan/plan.json`.
The capture is its child directory `20261016T035058544-03bcf881`.
The runner returned zero. Source and binary hashes stayed unchanged.
All three records passed replay and requested-mode checks.
The order was zero speedup, 50 percent, then invalidation after selected payment.
Each normal case completed 50,000 selected batches.
Owner draining, direct receipt readiness, finite measurement, and pause timing were enabled.
Scheduler, dispatch timing, child CPU, peer progress, and selected handoff were disabled.
No separate-controller baseline was run. This is one diagnostic, with no unchanged retry.

| Observation | Zero speedup | 50 percent | Expected invalidation |
| --- | --- | --- | --- |
| Work completed | Yes | Yes | No, rejected as required |
| Peer payments | 0 | 301 | 1 |
| Selected payments | 0 | 160 | 1 |
| Peak observed peer debt | 0 ns | 1,499,911 ns | 500,000 ns |
| Accepted selected wait | 0 ns | 1,334,349 ns | 162 ns |
| Peer joined and session closed | Yes | Yes | Yes |
| Measurement status | Unqualified | Unqualified | Invalid |

The 50-percent peer receipt intervals have a 0.055456 ms median and a 15.778825 ms maximum.
These intervals use `ReceiptInterval.Observed - Started` and each record's timestamp frequency.
Full peer intervals use `Finished - Started`. Their median is 0.056935 ms and their maximum is 18.156635 ms.
These are elapsed observation intervals, not isolated native peer-service cost.
Selected receipt intervals have a 0.015270 ms median and a 2.236786 ms maximum.
The 160 complete selected pause timings total 33.486342 ms, with a 0.081183 ms median and a 7.201321 ms maximum.
Pause duration uses `Timing.Completed - Timing.Started`. It is not the native handler interval.

The treatment freezes peer global and local accounting at 72,334,527 ns, with no active wait, pending request, or unsettled wait.
Its selected ledger retains 178 ns of debt at the frozen endpoint.
The normal cases have no failure boundary, receipt parser rejection, or cleanup error.
All three records report peer join, controller completion, and native close.

The invalidation case retains `The CLR probe lifetime is no longer admitted.` for both owners.
Its single pause has no completed endpoint because invalidation interrupts the callback.
It freezes 104 ns of peer debt and no pending request. Cleanup reports no errors.
This expected failure is retained as an invalid measurement, not a successful prediction.

Both normal measurements retain unqualified source-tail, sample-boundary, and instrumentation-attribution reasons.
The treatment also retains an unqualified ledger residual.
The result establishes this bounded two-thread execution only.
It does not establish stable capacity, lower overhead, or causal prediction accuracy.
Any future separate-controller comparison must match the observer settings and retain every failed arm.


## Accepted prototype risks

Direct controller calls omit the separate worker's timeout and exception wrapper.
A stalled direct call can block its owner. The process watchdog remains the outer bound.
Failed registration can leave a pre-set registration flag and prevent normal close.
This inherited failure path remains unverified. The prototype retains failure instead of forcing native close.
Neither limitation permits general application support claims.
Release failure after accepted settlement remains source-reviewed, without injected runtime coverage.
The standalone analyzer checks each record's mode. The runner additionally binds every record to the requested mode.
Optional expectations preserve other interop modes. The default bound matrices passed with the shared outcome reader.

The final `BuildDocs` target completed successfully after the sampled documentation update.
Its log is `artifacts/owner-controller-final-docs-build.log`.
