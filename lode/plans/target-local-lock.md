# Target-local lock

## Problem

Only `TargetBarrier` and `TargetQueue` follow the wake rule. A program that guards shared state with a lock cannot be profiled.

## Decision

`TargetLock` in `src/CozNet/TargetLocalProfiler.fs` is a non-recursive mutual-exclusion lock that uses the existing wait protocol. The rule and the bounds do not change.
- `Exit` posts and stores the token as the release token, and then frees the lock. Only the thread that holds the lock can call `Exit`.
- `Enter` takes a free lock without a wait. Otherwise it calls `WaitBegin`, waits for the lock, reads the release token, and calls `WaitEnd` with that token.
- Only the holder can write the release token, and the waiter reads it after it takes the lock. So the token is always that of the release that freed the lock, for any number of threads.
- A timeout or a cancellation skips no delay. The run abort token cancels a wait. A second `Enter` by the holder raises `LockRecursionException`.
- The wait helpers move unchanged from `TargetQueue` into an internal `TargetWait` module that both types use.

## Qualification

The workload `lock` runs two workers for 120 ms in each experiment. Each worker repeats one cycle: `Work.peer 20` outside the lock, then `Work.selected 40` inside it, then one visit.
The lock sets the rate while the work inside it is at least the work outside it. So the model gives `Work.selected` 25%, 50%, and 62.5% at speedups 25, 50, and 75. `Work.peer` gains 0%.
An optimized arm reduces the rounds inside the lock to 30, 20, and 10, or the rounds outside it to 15, 10, and 5. The criteria use the measured gains, not the model values.
The sample modes are `lock-profile` and `plain-lock`. `VerifyTargetLocalProfile.ps1 -Workload lock` and the build target `TargetLocalLockVerify` run the suite.

## Acceptance criteria

These criteria are fixed before the first run. They add to criteria 1 to 59 and change none.

60. Each lock profile record holds the fixed design of criterion 38, the warm-up of criterion 53, and the seed and rotation of criterion 56.
    Each passed experiment has at least 100 visits and `EffectiveNs` equal to `ElapsedNs` minus `GlobalNs`. Each written prediction equals the computed one.
61. Each plain lock record has mode `plain-lock`, the rounds of its arm, a 120 ms duration, and at least 100 visits.
62. Each of the six points has 5 valid blocks, a median absolute difference of at most 5 percentage points, and each block within 10. The positions are balanced as in criterion 57.
63. After the commit, every source hash in each new manifest equals the SHA-256 of the committed blob. Each suite runs once on the reviewed tree, and no run is repeated to replace a failure.

See [target-local queue](target-local-queue.md), [target-local rotation](target-local-rotation.md), and [target-local status](target-local-status.md).
