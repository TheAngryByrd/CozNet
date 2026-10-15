# Bound sample coverage

The offline partition is retained in `artifacts/bound-coverage/20261015T191528612/coverage.json`.
It replays all twelve records from `artifacts/bound-selected-cadence/20261015T183603714-21ff53d4` before classifying observations.
The report retains input and analyzer hashes. Analyzer copies remain beside the report.
The partition separates mapped and unmapped observations, then separates epoch baselines from complete same-epoch intervals.
It reconciles sample and credit totals with the retained records.
All epoch baselines and unmapped intervals earn zero credit.

| Treatment group | Cadence | Enabled epochs | Epochs without processed samples | Mapped baselines | Credited mapped intervals |
| --- | --- | ---: | ---: | ---: | ---: |
| 0 | Continuous | 19,174 | 18,631 | 17 | 2 |
| 1 | Qualified sample | 13 | 0 | 3 | 15 |
| 2 | Qualified sample | 15 | 0 | 7 | 9 |
| 3 | Continuous | 10,505 | 10,145 | 13 | 12 |

Each normal case completes 5,000 selected batches.
More than 96% of continuous treatment epochs contain no processed sample.
The qualified treatments have no empty epochs, but retain few credited mapped intervals.
These counts are source-session observations, not fractions of application CPU.

## Longer qualified workload

Capture: `artifacts/bound-qualified-long/20261015T191601502-17f1035a`.
Command:

```powershell
./native/VerifyBoundSampledPayment.ps1 -SelectedPayment -SelectedCadence qualified-sample -SelectedBatches 50000 -EvidenceDirectory artifacts/bound-qualified-long
```

The zero-speedup, 50-percent, and expected-invalidation cases passed.
The native checks, three managed groups, outcome checks, and Python checks passed.
Source and binary hashes remained unchanged.
The capture retains its manifests, kernel identity, copied sources, raw results, and logs.
Separate `coverage.json` and `pause-summary.json` reports retain the subsequent diagnostic calculations.
Their analyzer sources remain beside those reports.

| Observation | Zero speedup | 50 percent |
| --- | ---: | ---: |
| Selected batches | 50,000 | 50,000 |
| Selected work ms | 351.857 | 522.706 |
| Source observations | 357 | 421 |
| Enabled epochs | 1 | 125 |
| Mapped complete intervals | 192 | 125 |
| Mapped baselines | 0 | 62 |
| Unmapped baselines | 1 | 63 |
| Unmapped complete intervals | 164 | 171 |
| Credit ms | 0 | 62.500 |
| Enabled counter ms | 366.082 | 443.358 |
| Nominal source periods ms | 357 | 421 |

The treatment retains 124 nonoverlapping pauses within the selected-work interval.
Their total duration is 60.507091 ms. Recorded selected payment totals 0.165611 ms.
The first pause is the longest at 26.116901 ms.
No pause or outlier was removed.
Final selected debt is 87 ns and final peer debt is zero.
The 2 ms debt, 50 microsecond child, 5 ms handler, and 100 ms receipt limits remain unchanged.

## Interpretation limits

The counter includes enabled task-clock CPU. Nominal periods and endpoint IPs do not partition that CPU by instruction ownership.
The counter and source records span setup and completion outside the selected-work bracket.
Their totals cannot normalize selected work or establish an application CPU coverage percentage.
The raw elapsed comparison is not a causal speedup estimate.
The candidate still samples one owner and does not establish general parallel prediction.

A baseline can span a disabled interval, so the mapped baseline count is not recoverable credit under the existing contract.
The 26.117 ms first pause includes multiple phases. These records cannot assign its duration to JIT, collection, accounting, or scheduling.
The next diagnostic needs phase timing around producer stop, acknowledged drain, payment, and resume.
No timing bound or eligibility rule should change based on these totals alone.

See [observer explanation](../../../docsSrc/Explanations/bound-observer-overhead.md), [eligibility](../../../docsSrc/Research/cooperative-sample-eligibility.md), and [cadence evidence](bound-selected-cadence.md).

## Phase timing

Capture: `artifacts/bound-pause-phases/20261015T192347082-e5e8a957`.
Command:

```powershell
./native/VerifyBoundSampledPayment.ps1 -SelectedPayment -PauseTiming -SelectedCadence qualified-sample -SelectedBatches 50000 -EvidenceDirectory artifacts/bound-pause-phases
```

The zero-speedup and expected-invalidation cases passed.
The treatment failed after 2,309 batches at 2,499,880 ns peer debt, above the unchanged 2 ms limit.
All three records passed evidence replay. Replay preserved the failed treatment.
Input hashes remained unchanged during capture.
Native checks, all three Linux managed groups, and 38 Python analyzer checks passed.
Windows `--verify-pausable-managed` also passed before capture.
The outcome-setting validation was tightened after capture and passed its portable fixtures.
A later capture used the revised runner and startup validation preparation.
See [validation preparation](validation-preparation.md) for that capture and its retained failure.

| First treatment pause interval | Milliseconds |
| --- | ---: |
| Collection entry to producer stop | 1.734 |
| Producer stop to disabled-counter acceptance | 0.468 |
| Disabled-counter acceptance to drain acknowledgment | 1.064 |
| Drain acknowledgment to batch validation | 35.567 |
| Batch validation to epoch snapshot | 0.065 |
| Snapshot to completed sample publication | 1.635 |
| Publication to callback return | 1.352 |
| Callback return to source enable | 0.061 |
| Source enable to enabled-counter acceptance | 0.037 |
| Counter acceptance to collector resume | 0.274 |
| Collector resume to final guard completion | 0.058 |
| Full outer pause | 42.733 |

The outer pause also includes entry and return work outside the inner timing boundaries.
The replayed `phase-report.json` retains all six treatment pauses and the incomplete invalidation pause.
Its script and per-record hashes remain beside the report.
The largest first-pause interval contains admission closure, lifetime validation, snapshot copying, and batch validation.
It does not prove which operation consumed CPU, or whether scheduling or JIT caused the delay.
No unchanged failed case was rerun to obtain a pass.

| Timing contract | Executed check |
| --- | --- |
| Successful boundary order | `FinalDrainPreservesEpochAndCallbackBoundary` |
| No callback completion after failure | `CallbackFailureDoesNotResume` |
| No producer stop after disable failure | `DisableFailurePreventsCallback` |
| No enable or completion after resume failure | `ResumeFailureRejectsCollection` |
| Missing and reordered phases reject | `PauseTimingChecks` |
| Requested timing matches the record | `BoundSampledOutcomeChecks.ps1` |

The next investigation should isolate first-use batch validation while preserving its complete accounting checks.
