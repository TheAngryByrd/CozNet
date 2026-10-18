# Target-local start run-queue delay

## Decision

Two captured debt failures occur at the first sample of a thread, before its first wake: barrier runs `cd99aa09` and `73afdb27`.
The wait records cannot place these failures, because no wait precedes them.

Each worker now reads its run-queue counter once more, directly after the start barrier returns and before its `StartedTicks` read.
`TargetThreadOutcome.StartRunDelayNs` is that read minus the read before the start barrier. It holds the run-queue delay of the whole interval between the two reads.
That interval includes any preemption before the barrier release and the wake latency after it.
A failed read records -1, as for `RunDelayNs`, and fails criterion 18.
The read is outside the `StartedTicks` to `FinishedTicks` span of its own worker. Both profile arms do the same read.
`ElapsedNs` starts at the earliest `StartedTicks`, so the read of a later worker can overlap the work of an earlier worker. One `pread` costs microseconds.

The run-queue delay between the work start and the first sample is not read directly. The signal handler path stays unchanged, so the 5 ms handler bound keeps its current evidence.
The residual run-queue delay, `RunDelayNs` minus `StartRunDelayNs` minus the wait values, is an upper bound on the run-queue delay during work outside the waits.

The sample period is 1 ms of task-clock time, counted from the native start. The native start is before the start barrier, and a barrier spin uses task-clock time.
So the first sample of a thread can come before its work start.

The samples write each thread's `StartedTicks` as `workStartedNs`, with the native `startedNs` and `finishedNs`.
On Linux, `Stopwatch` and the event times both use `CLOCK_MONOTONIC` in nanoseconds. The analyzer checks the order of the three times for each thread.
That check fails the run for a clock offset larger than the native span of a thread. A smaller offset can pass the check and move the reported start times.

## Acceptance criteria

These criteria are fixed before the first run after this change. They add to criteria 1 to 17 and change none.

18. Each thread of each profile arm in the prediction and barrier suites records `workStartedNs`, `startedNs`, `finishedNs`, and `startRunDelayNs`.
    The values must satisfy `startedNs <= workStartedNs <= finishedNs` and `0 <= startRunDelayNs <= runDelayNs`.
    The analyzer reports this as `startDiagnosticsComplete`, and a run passes only when it is true. A profile record without threads or events fails this criterion.
19. For each debt-exceeded failure before the first wake of its thread, the analyzer reports `startRunDelayNs`, the interval from the work start to the failure,
    the first sample time of each thread, and the residual run-queue delay of that thread.
20. After the commit, every source hash in each new manifest equals the SHA-256 of the committed blob, except for files that changed after the run.
21. Each suite runs once on the tree that the reviews check. No run is repeated to replace a failure.

The build target `TargetPredictionAnalysisChecks` runs `native/TargetPredictionAnalysisChecks.py`, which checks the analyzer rules of criteria 18 and 19.

See [target-local wait delay](target-local-wait-delay.md) and [target-local status](target-local-status.md).
