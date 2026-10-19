# Target-local profile warm-up

## Finding before this change

In pipeline run `20261019T043536804-514995d6`, the first experiments of the profile processes have a higher producer time.
Their producer time outside measured payment, for each visit, has a median of 1.043 times the block baseline. Orders 1 to 5 have medians of 0.997 to 1.000, and order 6 has 1.053.
The design seed of a block is the run seed plus the block number, so the order of the experiments is the same in every run.
`peer-50` runs first in blocks 1, 2, 3, and 5, so four of the five order-0 values come from `peer-50`. The two pipeline runs predict -3.9% and -4.0% at its median.
In block 4, `peer-75` runs first. That block has the largest `peer-75` difference, -14.82 points.
The records do not separate the order from the point. In block 4, `peer-50` runs at order 5 with a ratio of 1.048. In block 5, it runs first with a ratio of 0.994.
Because the seed is fixed for each block, the same point holds the same order in every run. So repeated runs are not independent evidence of an order effect.

A development diagnostic, not retained, found that the time between child payments is about 0.05 ms for each experiment. Its time for each item outside payment did not rise at `peer` 50%.

Hypothesis, not shown: the first experiment of a process carries a cost that later experiments do not. Payment overhead at `peer` 50% is not excluded.

## Decision

`TargetCausalProfile.run` runs one warm-up experiment at speedup 0 with the first candidate before the first block.
The result keeps it as `Warmup`. No prediction uses it, and its failure does not invalidate a block.
Its full record stays in the profile output, and the analyzer and the page report whether it passed.
A warm-up at speedup 0 does not exercise child payment. If the hypothesis holds, the order-0 ratio of criterion 54 comes near the other orders.
The warm-up does not change the design, the order of the blocks, or any bound.

## Acceptance criteria

These criteria are fixed before the first run. They add to criteria 1 to 52 and change none.

53. Each latency, throughput, and pipeline profile record holds one complete warm-up experiment at speedup 0, apart from its seven design experiments.
    The analyzer reports `warmupPassed` for each block.
54. For each run order, the analyzer reports the median work ratio over all points and blocks, so a later run can check whether order 0 still differs.
    The seed confound stays, so this check is descriptive.
55. After the commit, every source hash in each new manifest equals the SHA-256 of the committed blob. Each suite runs once on the reviewed tree, and no run is repeated to replace a failure.

See [target-local experiment diagnostics](target-local-experiment-diagnostics.md) and [target-local status](target-local-status.md).
