# Target-local spin before a lock wait

## Finding before this change

Lock run `20261019T070331580-523cc380` gives a median handoff share of 7.40% in the profile and 2.18% in the real optimization at `selected-50`.
Development smoke runs, not retained, gave three blocks for each spin:
- With a 200 microsecond spin, the profile share was 5.5% to 5.9%, and `selected-50` predicted 39.5% to 42.9% against real gains of 48.5% to 50.4%.
- With a 1 ms spin, the profile share was 2.8% to 4.4%, and `selected-50` predicted 45.2% to 47.7% against real gains of 49.3% to 51.8%.
So the knee error fell as the handoff share fell. This supports, but does not show, the handoff hypothesis of [the handoff plan](target-local-lock-handoff.md).

## Decision

`TargetLock(spin)` spins up to `spin` inside its wait before it blocks. The spin must be from 0 to 1 ms. The spin and the block share one timeout budget: the block gets only the part of the timeout that the spin did not use. With a spin of 0, the block gets the whole timeout. `TargetLock()` keeps a spin of 0, so its behavior does not change.
The spin runs between `WaitBegin` and `WaitEnd`, so a spinning waiter is blocked for the ledger, as a sleeping waiter is. The wake rule and the bounds do not change.
A spin uses CPU time while the waiter waits. The shared wait helper takes the spin, and `TargetQueue` passes 0.

The `lock` suite stays unchanged and keeps a spin of 0, so the knee error stays measured.
A new `spinlock` suite runs the same lock workload with a 1 ms spin in both the profile and the plain arms.
The sample modes are `spin-lock-profile` and `plain-spin-lock`. `VerifyTargetLocalProfile.ps1 -Workload spinlock` and the build target `TargetLocalSpinLockVerify` run it.

## Acceptance criteria

These criteria are fixed before the first run. They add to criteria 1 to 66 and change none.

67. Each spin lock profile record holds mode `spin-lock-profile`, the fixed design, the warm-up, the seed and rotation, and the handoff counts of criteria 60 and 64.
    Each plain spin lock record has mode `plain-spin-lock` in place of `plain-lock`, follows the other rules of criterion 61, and holds the handoff counts.
68. Each of the six spin lock points has 5 valid blocks, a median absolute difference of at most 5 percentage points, and each block within 10. The positions are balanced.
69. The analyzer reports the median handoff shares for each spin lock point, as for the lock suite.
70. After the commit, every source hash in each new manifest equals the SHA-256 of the committed blob. Each suite runs once on the reviewed tree, and no run is repeated to replace a failure.

See [target-local lock](target-local-lock.md), [target-local lock handoff](target-local-lock-handoff.md), and [target-local status](target-local-status.md).
