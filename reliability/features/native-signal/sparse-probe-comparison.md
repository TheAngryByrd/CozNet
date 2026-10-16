# Four-arm comparison at interval 8192

Capture: `artifacts/sparse-probe-comparison/20261016T062509678-848a6fda1e8c491d94be6ba6cffa1db0`.

```powershell
pwsh -NoProfile -File native/VerifyCooperativeComparison.ps1 -ProbeInterval 8192 `
  -EvidenceDirectory artifacts/sparse-probe-comparison -SelectedBatches 50000
```

All four arms complete with process exit zero. No arm is invalid.
The analyzer reports no integrity or runner errors. Original inputs remain unchanged.
The runner exits one because prediction remains unqualified.
Both sampled records match the plan's interval 8192 under `probeIntervalEvidenceVersion=1`.
Physical arms retain disabled profiling.

## Descriptive result

| Arm | Lower cost, ns per progress | Upper cost, ns per progress |
| --- | ---: | ---: |
| Physical baseline | 4091.29286 | 4091.57872 |
| Physical optimized | 2185.88254 | 2186.25136 |
| Sampled zero | 5173.29416 | 5174.47528 |
| Sampled half | 5893.95202 | 5919.91334 |

Sampled elapsed reduction is −14.4322% to −13.9043%.
Physical elapsed reduction is 46.5633% to 46.5761%.
The sampled treatment is slower despite the physical improvement.
These ranges describe retained clock brackets. They are not confidence intervals or accuracy bounds.
The earlier 1024 blocks are separate captures and cannot isolate an interval-dependent performance change.

Zero speedup skips 100,000 of 100,257 service checks.
Treatment skips 63,109 of 100,323 checks and records 45 selected pauses.
Treatment freezes peer command 231 with a 499,895 ns lease budget and no accepted child.
Native ownership, ledger residuals, source membership, and instrumentation attribution remain unqualified.
Later cleanup does not change the frozen estimator inputs.

## Timing observations for follow-up

The 45 recorded pause brackets total 19.943184 ms.
Their start-through-callback intervals total 19.107088 ms, and callback-through-resume intervals total 0.836096 ms.
Recorded selected payment totals 0.263552 ms.
These wall-time brackets include work and possible descheduling. Their difference does not isolate a component's CPU cost.

The first peer request's managed bracket lasts 27.378076 ms.
The median across 231 recorded peer request brackets is 0.703650 ms.
The last request spans 19.255260 ms and completes after measurement freeze.
These request brackets overlap selected work and must not be summed as selected-thread overhead.
First-use receipt work and recurring callback work remain candidates for separate measurement.
The observations do not identify JIT, GC, or scheduling as the cause.

## Contract checks

All 26 analyzer checks and the runner checks pass on the final code.
The runner checks cover default and 8192 activation, original positional arguments, process failures, preflight failure, and changed inputs.
The full-collection replay check first accepts all four real fixture arms.
It then rejects a missing or mismatched treatment interval while preserving the other three costs and suppressing the aggregate comparison.
Legacy records at 1024 remain accepted. Explicit 8192 metadata cannot pass under that legacy plan.

The new analyzer also replays the earlier idle-service capture without changing its descriptive comparison.
The separate output is `artifacts/sparse-comparison-legacy-replay.json`.
The original capture is unchanged.
Both sampled records from the retained default-1024 integration also pass replay with mandatory interval metadata.
That check records `artifacts/sparse-comparison-versioned-default-replay.json` and makes no new timing claim.

The live capture precedes the positional-parameter compatibility fix and the final test additions.
The final runner checks cover that parameter fix. Analyzer and workload behavior did not change after the live capture.
No failed runtime arm was discarded or repeated for a passing result.

See [the comparison reference](../../../docsSrc/Reference/cooperative-comparison.md)
and [the explanation](../../../docsSrc/Explanations/cooperative-physical-comparison.md).
