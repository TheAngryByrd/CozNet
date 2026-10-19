# Target-local semaphore

## Problem

Only `TargetBarrier`, `TargetQueue`, and `TargetLock` follow the wake rule. A program that signals another thread without a queue or a lock, as with an event or a condition variable, cannot be profiled.

## Decision

`TargetSemaphore(initialCount)` in `src/CozNet/TargetLocalProfiler.fs` is a counting semaphore that uses the existing wait protocol. The rule and the bounds do not change.
- `Release` posts, puts the token at the end of a FIFO list, and then adds one permit. Any thread can call `Release`.
- `Wait` takes a free permit without a wait. Otherwise it calls `WaitBegin`, waits for a permit, takes the oldest token, and calls `WaitEnd` with that token.
- A token always comes from a release that happened before the wait ends. With one releasing thread and one waiting thread, the token is that of the release that added the permit.
  With more releasing or waiting threads, the pairing follows the FIFO order, as for `TargetQueue`, and a skip can be too large or too small.
- An initial permit has no token. A waiter that waited takes a release token first.
- A timeout or a cancellation skips no delay. The run abort token cancels a wait. `initialCount` must be 0 or more. `Release` raises `SemaphoreFullException` at `Int32.MaxValue` permits, before it posts.

## Qualification

The workload `semaphore` runs two workers for 120 ms in each experiment. They take turns through two semaphores that start at 0:
- Worker 0 repeats `Work.selected 24`, releases the peer semaphore, and waits on its own semaphore.
- Worker 1 waits on its own semaphore, runs `Work.peer 12`, visits, and releases the selected semaphore.
So no work runs in parallel, and each visit holds two handoffs. The period is the sum of both parts.
So the model gives `Work.selected` 16.7%, 33.3%, and 50% at speedups 25, 50, and 75, and `Work.peer` 8.3%, 16.7%, and 25%.
The model leaves out the two handoffs of each visit, so the real gains can be lower.
An optimized arm reduces the selected rounds to 18, 12, and 6, or the peer rounds to 9, 6, and 3. The criteria use the measured gains, not the model values.
When the counter stops, worker 0 ends its loop and releases the peer semaphore once more with a done flag set, so worker 1 also ends.
The sample modes are `semaphore-profile` and `plain-semaphore`. `VerifyTargetLocalProfile.ps1 -Workload semaphore` and the build target `TargetLocalSemaphoreVerify` run the suite.

## Acceptance criteria

These criteria are fixed before the first run. They add to criteria 1 to 70 and change none.

71. Each semaphore profile record has mode `semaphore-profile` and follows the other rules of criterion 60. Each plain semaphore record has mode `plain-semaphore` in place of `plain-lock` and follows the other rules of criterion 61.
72. Each of the six semaphore points has 5 valid blocks, a median absolute difference of at most 5 percentage points, and each block within 10. The positions are balanced as in criterion 57.
73. Unit tests show that concurrent releases and waits lose and duplicate no permit, that a timeout or a cancellation returns false and takes no permit, and that `initialCount` below 0 raises `ArgumentOutOfRangeException`.
    Unit tests run on threads without a profiler, so they do not check tokens. The replay checks of criterion 60 check the wake records of each profile experiment.
74. After the commit, every source hash in each new manifest equals the SHA-256 of the committed blob. Each suite runs once on the reviewed tree, and no run is repeated to replace a failure.

See [target-local lock](target-local-lock.md), [target-local queue](target-local-queue.md), and [target-local status](target-local-status.md).
