# Target-local GC windows

## Finding before this change

Four current-design forced-GC checks each fail 1 of 10 runs by the 2 ms debt bound. The records do not show when each forced collection ran.
In run 8 of `20261019T001542478-b4d2cdb6`, the peer has no event for 6.5 ms before its failure, and it has 5.3 ms of residual run-queue delay.
In the same interval, the selected thread journals a sample every 1 ms, from 32.89 ms to 38.03 ms. A blocking collection suspends both workers.
So that pattern does not fit one collection that suspends both workers for the whole gap. It does not exclude a collection that waits for the suspension.
The new windows test this for each later failure.

## Decision

In `profile-gc` mode, the sample reads `Stopwatch` before and after each forced `GC.Collect` call. It writes the pairs to `gcWindows`, in order.
The window array has room for 1024 pairs and is allocated before the run. `gcWindowsDropped` counts each later collection, so no window is lost silently.
The sample also records `GC.GetTotalPauseDuration` before and after the run, and writes the difference as `gcPauseNs`, and `Stopwatch.Frequency` as `stopwatchFrequency`.
The pause duration also counts collections that the sample does not force. So it is a report, not a gate.
On Linux, `Stopwatch` and the event times both use `CLOCK_MONOTONIC` in nanoseconds when the frequency is 1000000000.
No profiler library code changes. The windows come from the monitor thread, outside the workers.

The analyzer `native/AnalyzeTargetGc.py` reads each `gc-N.json` and writes `gc-summary.json`. The transition runner adds its result to `passed`.
For each debt-exceeded failure, it reports the windows that overlap the interval from the sample before the failing sample of that worker to the failure.
If the failing sample is the first sample of that worker, the interval starts at the native start of that worker.
For each worker, it reports the largest interval between two of its events and the window time inside that interval.
A window also holds any time that the monitor thread is descheduled inside the call. So the overlap is an upper bound on the collection time in an interval.
An incomplete record reports no windows, because its windows can be missing or out of order.
A failure in the closing settlement can come without a new sample. Its interval then starts one sample earlier than for a failure at a sample.

The cpuset option is deferred. `--cpuset-cpus` restricts the whole container, so the monitor and the GC threads would share the chosen CPUs with the workers.
Per-worker affinity needs a library hook and a change to the public API. That is a decision for the user.

## Acceptance criteria

These criteria are fixed before the first run after this change. They add to criteria 1 to 29 and change none.

30. Each forced-GC record holds `stopwatchFrequency` 1000000000, `gcWindowsDropped` 0, and one window for each forced collection.
    Each window ends at or after its begin. Each window begins at or after the previous end, and at or after the earliest `workStartedNs`.
    The analyzer reports this as `gcWindowsComplete`, and the transition check passes only when it is true for all 10 runs.
31. For each debt-exceeded failure in a forced-GC record, the analyzer reports the overlapping windows and their total overlap.
32. For each forced-GC record, the analyzer reports the sum of the window lengths and the `gcPauseNs` difference.
33. After the commit, every source hash in each new manifest equals the SHA-256 of the committed blob. Each suite runs once on the reviewed tree, and no run is repeated to replace a failure.

See [target-local transitions](target-local-transitions.md) and [target-local status](target-local-status.md).
