# Target-local handoff parts

## Finding

In transaction run `20261019T124914850-b10c2f47`, the median mean handoff is 150.2 microseconds in the plain original arm and 49.7 in the profile baseline.
In spin transaction run `20261019T124806158-79f85b15`, a consumer spin of up to 1 ms cuts them to 104.7 and 6.4 microseconds. So a gap of about 98 microseconds stays, and its cause is not known.
The plain records do not count the dequeues that take their item in a spin, and a handoff is one interval from just before `Enqueue` to the return of `Dequeue`.

## Decision

The transaction workloads split each handoff and count the spin takes. The workload, the arms, the rule, and the bounds do not change.
- The producer also records the time from just before each `Enqueue` call to its return. This covers its wait for a space, the post, and the release of the item.
- `TargetQueue.SpunDequeues` counts the dequeues that take their item in a spin. It is public, so the sample can record it.
- Each plain transaction record and each `queueStats` entry gain `enqueues`, `enqueueNs`, and `spunDequeues`. The consumer counts a spin take for an item only when `SpunDequeues` rises across its `Dequeue`, so the end-of-run item does not count.
- The analyzer reports, for the baseline of each block, the mean enqueue time, the mean signed difference between the dequeue return and the enqueue return, and the share of spin takes, in the profile and in the plain original arm.
The signed difference is not a separate interval. It holds the queue residence and the wake or the spin of the consumer when the producer reads its time first. The consumer can take the item before the producer reads its time after `Enqueue`, so the value can be negative for one item and in the mean. A preemption of the producer there moves time from the second value to the first.
The spin share shows how many takes came in a spin. It does not show how long the consumer spun, or how long a blocked take waited for a CPU.

## Acceptance criteria

These criteria are fixed before the first run. They add to criteria 1 to 97 and change none.

98. Each transaction and spin transaction record holds `enqueues`, `enqueueNs`, and `spunDequeues` in each plain record and each `queueStats` entry. In each passed experiment and each plain record, the enqueue count equals the handoff count. A block without them is invalid.
99. A unit test shows that `SpunDequeues` counts a take in a spin and not a take without a wait.
100. The status reports, for both transaction suites on the same tree, the median of these baseline values in the profile and in the plain original arm: the mean enqueue time, the mean signed difference between the dequeue return and the enqueue return, and the share of spin takes. It states a cause only if the records show it.
101. After the commit, every source hash in each new manifest equals the SHA-256 of the committed blob. Each suite runs once on the reviewed tree, and no run is repeated to replace a failure.

See [target-local spin queue](target-local-spin-queue.md), [target-local transaction handoff](target-local-transaction-handoff.md), and [target-local status](target-local-status.md).
