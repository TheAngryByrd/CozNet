# Target-local start trace

## Finding before this change

Prediction run `20261018T233840885-22a9d378` fails `selected-dominant` block 5. The peer starts work 5.3 ms after the selected thread, with 8.3 microseconds of start run-queue delay.
The peer has 73 deliveries, 73 journaled samples, and 78.15 ms of task-clock time. So about 5 periods of task-clock time produced no recorded sample.
Each delivery is journaled, so the dispatch path drops nothing. The perf event sets `exclude_kernel`, so an overflow in kernel mode records no sample.
This supports, but does not show, kernel-mode time between the native start and the work start of the peer.
`coz_target_start` stamps its start time, enables the perf event, and sets the signal mask. The records do not show which part of the interval from that stamp to the work start is unsampled.

## Decision

Coz gives a new thread the local delay of its parent at creation, and the thread then keeps any debt that it collects (`profiler::start_thread` in `libcoz/profiler.cpp`).
In CozNet, a worker that is on a CPU but unsampled after its native start also keeps its debt, as in Coz. Coz has no debt bound, so it would pay that debt.
CozNet rejects a run whose debt passes 2 ms. So this failure is legitimate under the current CozNet rules, and its debt accounting matches Coz.
The account start does not move to the work start, because that would remove delay that was published while the worker was runnable. The bounds do not change.
If the trace shows profiler work in the start path, the remedy is to move that work before the native start.

Each worker now records three start points: after `Start` returns, before the start barrier, and after the start barrier returns.
Each point holds the `Stopwatch` time, the on-CPU time from `CLOCK_THREAD_CPUTIME_ID`, the run-queue delay from `/proc/thread-self/schedstat`, and the thread counters from `getrusage(RUSAGE_THREAD)`.
The counters are minor faults, major faults, voluntary context switches, and involuntary context switches. These counts are exact. The trace does not use the tick-scaled user and system times.
The worker writes each point into an array allocated at registration, and it records one point at registration so that the read code is compiled before the start.
The second and third points supply the existing `StartRunDelayNs` reads. The first point adds one read, and each point adds one `clock_gettime` and one `getrusage` call, all before `StartedTicks`.
For each segment, the analyzer gives the wall time, the on-CPU time, the run-queue delay, and the remaining time off the CPU, with the counter changes.
The reads in one point are sequential. A preemption between them moves time between segments, and this trace does not bound that error. The time off the CPU can then be negative.
The first segment, from the native start stamp to the first point, includes the perf event enable and the signal mask change.

The failure appeared in 1 of 60 profile arms, so the next runs may not show it again. In that case, the evidence pages show the largest segments of all arms and no startup failure.

## Acceptance criteria

These criteria are fixed before the first run after this change. They add to criteria 1 to 21 and change none.

22. Each thread of each profile arm in the prediction and barrier suites records three start points.
    Their times must not decrease, the first point must not precede `startedNs`, and the last point must not follow `workStartedNs`.
    The counters must be readable and must not decrease. The analyzer reports this as `startTraceComplete`, and a run passes only when it is true.
23. For each debt-exceeded failure before the first wake of its thread, the analyzer reports each start segment of that thread with its counter changes.
24. For each suite run, the analyzer reports the largest wall time of each start segment across all profile arms.
25. After the commit, every source hash in each new manifest equals the SHA-256 of the committed blob. Each suite runs once on the reviewed tree, and no run is repeated to replace a failure.

Revision: the first runs on this design read the on-CPU time from schedstat field 1. For a running thread, that field changes only at scheduler events, so it lags.
In forced-GC run `20261019T001542478-b4d2cdb6`, it gives 3.94 ms on a CPU in a 0.77 ms setup segment. The on-CPU time now comes from `CLOCK_THREAD_CPUTIME_ID`.
The runs before this revision are kept as runs of the earlier tree.

See [target-local start delay](target-local-start-delay.md) and [target-local status](target-local-status.md).
