# Target-local wait delay and source hashes

## Decision

A runnable thread that waits for a CPU keeps its debt. If the debt passes the 2 ms bound, the run fails.
Rationale: the profiler cannot pay delay for a thread that does not run. Forgiving that debt would remove real delay from the prediction.
The remedy is the host: one profiled thread for each physical core on an idle host. The bounds do not change.

To place a debt failure after a wake, each barrier wait records its own run-queue delay.
Each worker opens `/proc/thread-self/schedstat` once at registration. Each later read is a `pread` at offset 0 into a buffer allocated before the run.
`TargetBarrier` reads the counter before `WaitBegin` and after `WaitEnd`. The difference is the run-queue delay of that wait, including the time from the release to the wake.
`TargetThreadOutcome.WaitRunDelayNs` holds one value per completed wait, in order. A failed read records -1.
A thread keeps at most 1024 values. `WaitRunDelaysDropped` counts each later wait, so no value is lost silently.
The whole-run `RunDelayNs` now uses the same reader. The worker copies its wait values after its finish time, outside the measurement.

Measurement change: each wait adds two `pread` calls inside the timed window of the profile arms only. Ten phases add about 20 reads to a run of more than 100 ms.
Expected result: the profile-zero overhead ratio stays in its earlier range. The pass rules for prediction accuracy do not change.

## Source hashes

The prediction, barrier, and transition runners hash each text source after they replace CRLF with LF.
Git stores these files with LF, so a manifest hash equals the SHA-256 of the committed blob. Each manifest records `sourceHash`.
Manifests before this change hash the working-tree bytes, and some of those files had CRLF line endings.

## Acceptance criteria

These criteria are fixed before the first run after this change. They add to the existing criteria and change none.

13. Each profile arm in the prediction and barrier suites records one wait value for each wake event of each thread, with zero dropped.
    The analyzer reports this as `waitDiagnosticsComplete`, and a run passes only when it is true.
14. In a barrier run, the sum of all readable wait values is above zero. A stale `pread` would return the same bytes and give zero.
15. For each debt-exceeded failure after a wake, the analyzer reports the wait's run-queue delay, the interval from the post to the wake, and the interval from the wake to the failure.
16. After the commit, every source hash in each new manifest equals the SHA-256 of the committed blob, except for files that changed after the run.
17. Each suite runs once on the tree that the reviews check. No run is repeated to replace a failure.

Revision: after the first run on this design, review moved the copy of the wait values after the finish time,
removed an allocation from the parser, and limited criterion 15 to debt-exceeded failures and to the post of a dependency wake.
The runs before this revision are kept as failures of the earlier tree.

See [target-local status](target-local-status.md) and [target-local lifecycle](target-local-lifecycle.md).
