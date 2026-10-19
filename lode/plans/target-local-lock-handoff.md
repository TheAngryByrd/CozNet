# Target-local lock handoff

## Finding before this change

Lock run `20261019T062708436-df83442b` fails `selected-50` by a median of 9.13 points. All five blocks predict 5.2 to 13.0 points below the real gain.
A development trace, not retained, timed each lock acquire and release in two rounds of three runs:

| Run | Lock idle | Waiting for each worker | Entries that blocked |
| --- | ---: | ---: | --- |
| Profile at speedup 0 | 8.7 to 9.8 ms | 34 to 38 ms | 155 of 155 and 164 of 167 |
| Profile with `Work.selected` at 50% | 11.5 to 11.6 ms | 35 to 37 ms | 148 of 157 and 152 of 159 |
| Real optimization at 50% | 2.8 to 4.4 ms | 3 to 4 ms | 47 of 340 and 70 of 337 |

In the virtual speedup, the waiter still blocks at almost every entry. A real optimization at the knee rarely blocks, and its lock idle time is lower.
The trace gives a lock idle share of about 9.6% in the profile and 2.3% to 3.7% in the real optimization, so the excess is about 6 to 7% of the elapsed time.
The retained run gives a gap of 9.4 points at the median, 42.62% against 52.02%.

Hypothesis, not shown: each handoff to a blocked waiter leaves the lock idle until the waiter runs. That idle time is wall time, not inserted delay, so the virtual speedup keeps it.
The waiter skips the delay of its wait, as the wake rule requires, so it keeps arriving early.
This change measures the handoff time in each retained experiment. A result where the profile handoff share does not exceed the real share at the knee refutes the hypothesis.
The wake rule and the bounds do not change.

## Decision

`TargetLock` counts `Entries`, `WaitedEntries`, and `HandoffNs`. A waited entry adds the time from the release that freed the lock to its own acquire.
The counts change only while the thread holds the lock. The release time comes from one `Stopwatch` read in `Exit`.
The acquire time of a waited `Enter` comes from one read directly after its wait succeeds, before the native wait end, so native work does not add to it.
The plain lock record holds the counts of its lock. The lock profile record holds the counts of the lock of each experiment, in run order, after the warm-up.
The analyzer reports, for each point, the median handoff share `HandoffNs / ElapsedNs` of the profile experiment and of the plain arm. The share report does not change any pass rule. Criterion 64 makes a lock block without the counts invalid.

## Acceptance criteria

These criteria are fixed before the first run. They add to criteria 1 to 63 and change none.

64. Each plain lock record holds `entries`, `waitedEntries`, and `handoffNs`. Each lock profile record holds one such entry for the warm-up and for each experiment.
    A lock block without these counts is invalid.
65. For each lock point, the analyzer reports the median handoff share of the profile experiment and of the plain arm in `summary.json`.
66. After the commit, every source hash in each new manifest equals the SHA-256 of the committed blob. Each suite runs once on the reviewed tree, and no run is repeated to replace a failure.

See [target-local lock](target-local-lock.md) and [target-local status](target-local-status.md).
