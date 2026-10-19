# Target-local start setup

## Finding before this change

In prediction run `20261019T002411461-b833589f` and barrier run `20261019T002531212-f435da11`, the `setup` start segment has a median of 0.70 ms per worker.
Its largest value is 0.98 ms, its median on-CPU share is 64%, and 60 of the 120 segments have a major fault.
That segment runs after the native start, so sampling is on. It holds only `TargetCurrent.Set`, `TargetCurrent.Track`, and one trace point.
Both methods are compiled when a worker first calls them, and the runs disable tiered compilation. The records do not show which part of the segment is compilation.

Barrier run `20261019T002531212-f435da11` fails criterion 14 with every block within the limits. Its 300 wait values are all zero.
The same handle gives a whole-run run-queue delay above zero in 27 of 60 threads, so the reads are not stale.

## Decision

Each worker now calls `TargetCurrent.Set` and `TargetCurrent.Track` before `coz_target_start`. After the native start, a worker records one trace point, checks the start result, and records a second point.
A worker that cannot start clears its `TargetCurrent` state after the start barrier, as before. No ledger call can happen before the work starts, because only `Work` uses `TargetCurrent`.
This change moves profiler work out of the sampled interval. It does not change the debt rule, the bounds, or the measured span.

Criterion 14 detects a stale `pread`. A stale read returns the same bytes, so every difference on that handle is zero.
The whole-run delay and the wait values come from the same handle in each worker. So, for barrier runs, criterion 14 is replaced by criterion 27, which counts both.
The failed run stays a failure of criterion 14. Criterion 27 applies only to runs after this change.

## Acceptance criteria

These criteria are fixed before the first run after this change. They add to criteria 1 to 25. Criterion 27 replaces criterion 14 for barrier runs.

26. Across all profile arms of the prediction and barrier suites, the median `setup` segment is below 0.05 ms and the largest is below 0.5 ms.
    The analyzer reports this as `startSetupWithinLimit`, and a run passes only when it is true.
    Basis: after this change, the segment holds one result check and one trace point, which take microseconds. The median limit is far above that cost.
    The largest limit is below the earlier median of 0.70 ms and allows one preemption shorter than 0.5 ms. A longer preemption in any segment fails the run.
27. In a barrier run, the sum of all readable wait values and all readable whole-run run-queue delays of the profile arms is above zero. The analyzer reports this as `readerFresh`.
28. After the commit, every source hash in each new manifest equals the SHA-256 of the committed blob.
29. Each suite runs once on the tree that the reviews check. No run is repeated to replace a failure.

See [target-local start trace](target-local-start-trace.md) and [target-local status](target-local-status.md).
