# Matched prediction diagnostics

`LinuxMatchedPrediction` runs protocol `matched-timer-diagnostic-v1` through `native/VerifyMatchedPrediction.ps1`.
It requires an explicitly selected interleaved calibration directory.
The runner rejects incomplete or mismatched calibration input before it creates a run directory.
The shared parser reconciles the declared plan, journal rows, work counts, cumulative clocks, and recomputed statistics for all 2,700 segments.
`native/AnalyzeMatchedPrediction.py` analyzes the retained directory without native platform APIs.
Live collection requires Linux x64 containers. The analyzer uses Python 3.9 or later.

## Plan and evidence

Eight blocks each contain `profile-zero`, `profile-half`, `physical-baseline`, and `physical-optimized`.
The runner saves the full Fisher-Yates order with seed `20261014` before workload execution.
Each process has a two-second measurement window and a 30-second watchdog.
Both profile cases use prepared paths, a 10 µs initial timer interval, and original selected arithmetic.
Physical optimized changes only the selected worker. Forced GC, scheduler capture, and phase diagnostics remain disabled.
All cases use the same runtime image and disable tiering, ReadyToRun, and startup profiling.

The runner snapshots workload binaries, native sources, selected managed sources, scripts, and calibration evidence.
It compiles the timer library from the source snapshot.
The source snapshot is not a complete managed rebuild checkout.
The manifest records snapshot hashes and image identities before timed cases.
The analyzer checks those hashes after collection.
The snapshot includes `AnalyzeCpuCounters.py` and `CpuCounterChecks.py`.
The current analyzer rejects an entire legacy run if its manifest lacks these required hashes.
It then produces a run-level error instead of per-case diagnostics. Previously retained reports remain evidence for those runs.
The runner executes the counter checks before the matched analyzer checks.

`outcomes.json` contains all 32 declared cases from the start.
Each completed process updates its row with arguments, exit code, and result-file presence.
Unexecuted rows remain explicit after setup or runner failure.
Workload failure does not stop later isolated cases. Cases are not retried.
`runner-errors.json` retains runner failures. Per-case logs and JSON records remain in the evidence directory.

## Frozen cost

The analyzer consumes only `measurementCapture.start` and `measurementCapture.end` for the measurement calculation.
It requires complete capture, fresh start accounting, consistent clocks, positive progress, and valid worker ownership.
The selected worker supplies the progress denominator. Peer progress remains a separate diagnostic.

For start bracket `[S0,S1]`, end bracket `[E0,E1]`, and timestamp frequency `F`:

| Quantity | Definition |
| --- | --- |
| Wall duration range | `[(E0-S1)/F, (E1-S0)/F]` |
| Accounted delay | `(end.Global-start.Global)/1e9` |
| Effective duration range | Wall duration range minus accounted delay |
| Selected progress | `end.Selected.Progress-start.Selected.Progress` |
| Accounted cost range | `[effectiveLower/(progress+1), effectiveUpper/(progress-1)]` |

The one-batch denominator range accounts for incomplete work at the endpoints.
Selected progress must exceed one batch. Effective duration must remain positive across the whole bracket.
Shutdown progress, shutdown credit, and summed worker waits do not enter this calculation.

## Eligibility and current limits

Failed exits, workload failures, missing records, invalid boundaries, or snapshot changes reject case eligibility.
Profile cases also require stable maps, loss-free sampling, completed native lifetimes, and the unchanged 2 ms debt bound.
They require reconciled [CPU counter evidence](cpu-counter-evidence.md).
Missing legacy evidence rejects profile eligibility. Physical cases do not require CPU counter evidence.
`cpuCounterDiagnostics` retains counter results even when the process exits unsuccessfully or native cleanup fails.
Counter reconciliation does not remove workload failures or establish treatment eligibility.
Zero-intervention cases must contain no delay credit or native wait.
Treatment must contain positive credit and paid intervention inside the frozen window. Later shutdown payment cannot repair this requirement.

The current profile-half record schema has no verified bounds for unreceived sample periods or native journal suffixes.
Every profile-half case is therefore prediction-ineligible, even when collection and cleanup succeed.
Its `observedAccountedCost` is conditional on controller-accounted delay. It is not a bounded prediction cost.
Caller-supplied success flags cannot bypass this rule.

Calibration clock error and overhead sensitivity also lack verified bounds.
Local-speedup uncertainty has not yet been propagated into the two-point prediction-error gate.
The analyzer retains all eight block outcomes but publishes no block prediction or series prediction statistics.
`passed` and `predictionAccuracyEstablished` remain false. The runner returns failure after saving its diagnostic result.
`collectionComplete` means all declared case records produced structurally valid costs with successful process exits.
It does not establish prediction eligibility or accuracy.

See [the procedure](../How_Tos/run-matched-prediction.md), [the measurement model](../Explanations/matched-prediction.md), and [calibration limits](../Explanations/calibration-sensitivity.md).

## Observed execution

The first declared matrix ran all 32 cases on Linux x64. Five treatments failed capacity or debt gates.
All treatment cases remained prediction-ineligible, including the three with successful process exits.
Sixteen portable analyzer checks passed on Windows and Linux. See [the retained evidence](../../reliability/features/native-signal/matched-prediction.md).
