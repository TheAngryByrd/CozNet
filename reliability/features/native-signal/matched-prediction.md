# Matched prediction diagnostic evidence

Run `20261015T013834940-6d02c69e` executed all 32 declared cases once.
The runner retained eight blocks with four modes per block, using seed `20261014`.
Calibration input was the retained interleaved run `20261015T012028818-3c78e708`.
No workload case was retried or replaced.

## Outcomes

All eight profile-zero, eight physical-baseline, and eight physical-optimized processes returned zero.
Profile-half completed in blocks 2, 3, and 8. Five other treatments failed the unchanged debt or service-capacity gate.

| Treatment block | Selected debt peak, ns | Peer debt peak, ns | First failure |
| --- | ---: | ---: | --- |
| 1 | 5,215 | 3,000,000 | Service lease capacity |
| 4 | 171 | 2,143,950 | Observed debt |
| 5 | 399,318 | 2,999,970 | Service lease capacity |
| 6 | 400,263 | 2,000,077 | Observed debt |
| 7 | 0 | 2,500,000 | Observed debt |

The runner continued after each failure and retained every later outcome.
`runner-errors.json` is empty. Collection remains incomplete because five cases failed.
The target returned nonzero after writing the diagnostic analysis.

## Interpretation

The analyzer retained frozen cost ranges for 27 structurally valid records.
All eight treatment cases remain prediction-ineligible because sample-period and native-journal tail attribution lack verified bounds.
Three completed treatments therefore do not establish a valid subset prediction.
The report contains all eight blocks with no block prediction or series prediction summary.
Calibration clock, overhead, and local-speedup uncertainty also remain unresolved.

`artifacts/matched-prediction/recompute.py` independently reconciled all 32 outcomes, recomputed 27 costs, and checked 172 snapshot hashes.
It used selected progress and measurement-window global-delay deltas.
No shutdown totals entered those calculations.
The evidence directory retains the plan, snapshots, logs, outcomes, original analysis, and final offline analysis.
The source snapshot is not a complete managed rebuild checkout.

## Final checks and snapshot boundary

Sixteen deterministic analyzer checks passed on Windows and Linux.
The unequal-block fixture checks nonzero sample deviation and the seven-degree Student-t interval against independent constants.
Linux checks used Python 3.12 in image `sha256:df18428df8a0f9189c84e7424d214b4c50092af1f9dca5842a00c41c39bda316`.
The native compiler image has no Python executable, so it cannot run these portable checks directly.
An invalid-calibration check also verified rejection before creation of a run directory.

The live snapshot predates calibration reconciliation, hash-metadata checks, the runner-error check, and the frozen-window payment check.
Final checks also verify structured rejection of invalid hash metadata and preservation of an existing analysis file.
These changes do not alter the measured C# workloads.
Final offline analysis is retained separately as `analysis-reviewed.json`, with its analyzer hash.
Its 32-case result agrees with the original eligibility counts and preserves all five failures.
The live series was not repeated after these analyzer and input-boundary changes.

See [the reference](../../../docsSrc/Reference/matched-prediction.md), [the procedure](../../../docsSrc/How_Tos/run-matched-prediction.md), and [calibration evidence](interleaved-calibration.md).
