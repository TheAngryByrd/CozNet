# Target-local experiment diagnostics

## Problem

Pipeline run `20261019T040306290-64463c6b` fails `peer-50`. Across the three `peer` points, 13 of 15 block predictions are below their actual gains.
The profile records keep only the effective time and the visits of each experiment, so they cannot show where the time goes.

A development diagnostic outside the qualification runs ran three rounds of single pipeline experiments. It is not retained evidence.
- At `peer` 50%, the producer paid 22.5 to 24.7 ms in child payments and did 139 to 141 items. At baseline, it did 168 to 177 items.
- Its predictions for `peer` 50% were +3.1%, +2.0%, and -0.9%, so it did not repeat the negative bias.
- Plain pipeline runs gave real `peer` 50% gains from -8.0% to +4.1% on SMT siblings and from -6.2% to +4.1% on separate cores. The SMT test cannot separate an effect from that spread.

## Decision

Each `TargetCausalExperiment` now holds `Threads`, with one `TargetCausalThread` for each worker. A thread keeps its CPU time, wall time, handler time, measured child payment, wake skip, samples, wakes, and run-queue delay.
The analyzer reports, for each point and each worker, the median ratio of `(WallNs - PaymentNs) / Visits` to the same value of the block baseline.
`WallNs` runs from the work start to the end of the native finish, so it contains every child payment, including the closing payment.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio. So the ratio is a coarse report, and it alone does not show faster work.
The report does not change any pass rule.

## Acceptance criteria

These criteria are fixed before the first run. They add to criteria 1 to 49 and change none.

50. Each passed experiment in each latency, throughput, and pipeline profile record holds two `Threads` entries, one for each worker.
51. For each point, the analyzer reports the median work ratio of each worker in `summary.json`.
52. After the commit, every source hash in each new manifest equals the SHA-256 of the committed blob. Each suite runs once on the reviewed tree, and no run is repeated to replace a failure.

See [target-local queue](target-local-queue.md) and [target-local status](target-local-status.md).
