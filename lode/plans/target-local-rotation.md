# Target-local rotated experiment order

## Problem

Each profile process runs one block, and its design seed is the run seed plus the block number. So each point holds a fixed position in every run.
In the pipeline runs, `peer-50` runs first in four of five blocks. Its error cannot be separated from its position. See [the warm-up plan](target-local-warmup.md).

## Decision

`TargetCausalDesign.tryCreate` takes a `rotation`. The driver shuffles the experiments once with the seed, and each block rotates that base order left by `rotation + block - 1` places.
Before this change, each block drew a new shuffle from one random stream. So at rotation 0, the order of block 1 of a design with a given seed stays the same, and later blocks change.
A rotation must be from 0 to the experiment count of a block minus 1, or `tryCreate` returns `InvalidRotation`.
The profile runners now pass the same seed to every block and the rotation `block - 1`. The five blocks then use one base order and five rotations of it.
So each point runs at five different positions, one in each block. The design, the bounds, and the prediction rule do not change.
Seven positions over five blocks cannot be fully balanced. With seed 20261018, the base order is `peer-50`, `selected-25`, `selected-75`, `peer-75`, baseline, `peer-25`, `selected-50`.
- `peer-25` and `selected-50` never run first, and `peer-50` takes positions 0, 6, 5, 4, and 3. So `peer-50` still holds both the first and the last position once.
- The baseline runs first in block 5, so every point of block 5 uses a first-position baseline.
- Effects of position and of the preceding experiment stay possible. `positionsBalanced` checks only that each point has five distinct positions.

The analyzer also closes the two accepted risks of the warm-up milestone:
- A run passes only when the warm-up of each block passes. A warm-up runs at speedup 0, so it pays no delay, and its failure is a failure of the run.
- The report by run order skips each experiment that failed or has no complete thread record. A block whose baseline failed or is incomplete still drops out of the report.

## Acceptance criteria

These criteria are fixed before the first run. They add to criteria 1 to 55. Criterion 56 replaces the seed rule of criteria 42 and 46 for runs after this change.

56. Each profile record holds its seed, which equals the run seed, and its rotation, which equals the block number minus 1.
57. Across the five blocks of a run, each point runs at five different positions. The analyzer reports this as `positionsBalanced`, and a run passes only when it is true.
58. A run passes only when the warm-up of each block passes.
59. After the commit, every source hash in each new manifest equals the SHA-256 of the committed blob. Each suite runs once on the reviewed tree, and no run is repeated to replace a failure.

See [target-local warm-up](target-local-warmup.md) and [target-local status](target-local-status.md).
