# Target-local failure records

## Problem

A causal profile experiment that breaks the 2 ms debt bound keeps only the replay error, for example "Event 834 records ledger failure 1".
The record does not name the worker, the debt, or the time of the failure. So the cause of a failure without large run-queue delay cannot be examined.
Latency run `20261019T082828297-eb8d2196` has such a failure. For thread 1, wall time minus counted CPU time is 1.16 ms, with no run-queue delay.
Hypothesis, not shown: a thread that the runtime suspends for a garbage collection is not runnable, so its suspension adds no run-queue delay, and it cannot pay debt during the suspension.

## Decision

Each `TargetCausalExperiment` gains two diagnostics. They change no rule, bound, or criterion.
- `Failures` lists each `Failure` event of the journal: the worker, the failure detail, the debt amount, the time from the first journal event, and the time from the previous event of the same worker in sequence order, with that event kind. A failure without a time has null times.
- `Collections` and `GcPauseNs` give the change of `GC.CollectionCount(0)` and of `GC.GetTotalPauseDuration()` across the experiment. They count all threads of the process.
The analyzer copies the failures and the GC values of each invalid block into the summary. The evidence page lists them.

## Acceptance criteria

These criteria are fixed before the first run. They add to criteria 1 to 74 and change none.

75. Each experiment record of each profile suite holds `Failures`, `Collections`, and `GcPauseNs`. The library builds `Failures` from every `Failure` event of the journal. The records do not keep the journal, so the unit tests of criterion 76 check this.
76. Unit tests show that the failure list keeps the worker, the detail, the debt, and both times of each `Failure` event, and that an experiment without a `Failure` event has an empty list.
77. For each invalid block from a debt failure in a run on the reviewed tree, the status names the worker, the debt, the event gap before the failure, and the GC values of that experiment. It states a cause only if the records show it.
78. After the commit, every source hash in each new manifest equals the SHA-256 of the committed blob. Each suite runs once on the reviewed tree, and no run is repeated to replace a failure.

See [target-local GC windows](target-local-gc-windows.md), [target-local wait delay](target-local-wait-delay.md), and [target-local status](target-local-status.md).
