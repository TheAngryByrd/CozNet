# Target-local transaction handoff

## Finding

Transaction run `20261019T113356248-f1cbfddc` fails two points of [the transaction delay plan](target-local-transaction-delay.md), and the median prediction is above the median real gain at all six points.
In each block, the mean wall latency of the plain original arm is 986 to 1054 microseconds, and that of the profile baseline is 847 to 884 microseconds.
At `selected-25`, the absolute latency reductions are close: about 200 microseconds of virtual latency in the profile and about 200 microseconds of wall latency in the real arms. So the gap in the baseline latency is consistent with the bias of the predictions.
Hypothesis, not shown: the queue handoff, from an enqueue to the dequeue that returns it, takes longer in the plain arms. A busy host can wake a blocked consumer sooner than an idle one.

## Decision

The `transaction` workload records the handoff of each item: the time from just before `Enqueue` to the return of the `Dequeue` that takes it. This includes any wait for a free space, the time that the item waits in the queue, and the wake of the consumer. The consumer reads its time at once after `Dequeue` returns.
At a speedup above 0, a profiled handoff also includes the delay that the producer pays in `Enqueue`, so only the baseline compares equal measures. The same timing code in both arms does not show that it costs the same in both.
- Each plain transaction record holds `handoffs` and `handoffNs`, the count and the sum of the handoffs.
- Each transaction profile record holds `queueStats`, one entry for the warm-up and for each experiment, in that order, with the same two values.
- The analyzer reports, for each point and for the baseline, the mean handoff in the profile and in the real arm.
The workload, the arms, the rule, the bounds, and the criteria of the earlier plans do not change. Criteria 87 and 88 apply to the new runs as before.

## Acceptance criteria

These criteria are fixed before the first run. They add to criteria 1 to 89 and change none.

90. Each transaction profile record holds `queueStats` with one entry for the warm-up and each experiment, and each plain transaction record holds `handoffs` and `handoffNs`.
    In each passed experiment and each plain record, the handoff count equals the departures. A block without them, or with another count, is invalid.
91. The status reports the median mean handoff of the profile baseline and of the plain original arm, and states whether the gap is close to the gap in mean wall latency. It states a cause only if the records show it.
92. After the commit, every source hash in each new manifest equals the SHA-256 of the committed blob. Each suite runs once on the reviewed tree, and no run is repeated to replace a failure.

See [target-local transaction delay](target-local-transaction-delay.md) and [target-local status](target-local-status.md).
