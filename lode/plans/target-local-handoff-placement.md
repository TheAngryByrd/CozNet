# Target-local handoff placement

## Finding

Long queue handoffs move the `transaction` results in plain and profiled processes. See [the sample period section of the status](target-local-status.md#sample-period).
- In run `20261019T191349654-efea55a8`, four plain arms have mean handoffs of 232 to 312 microseconds. The other 31 plain arms have 11.0 to 192.0 microseconds, and 27 of them are below 100.
- Each slow arm ran after a passed warm-up, and the plain processes do not run the profiler.
- In run `20261019T175316141-ec3885bd`, one profile baseline has a mean handoff of 178.1 microseconds, against 37.5 to 49.6 in the other baselines.
- The records hold only the sum and the count of the handoffs of each process. So they cannot show whether a long mean comes from a few very long handoffs or from a longer handoff in every transaction, and they cannot show what the long handoffs share.

The plain arms do not pin their workers, so the scheduler places the two threads. The producer does not block while the queue has space.

## Hypotheses

- H7, shared CPU: a handoff is long when the consumer runs on the CPU of the producer after the dequeue. The consumer must then wait until the scheduler preempts the producer.
  A long wake that ends with a move of the consumer to another CPU is a different form. The records of this plan cannot test it.
- H8, wake latency: a handoff is long for a reason that does not depend on the placement of the two threads, such as a late wake of an idle CPU or a delay of the virtual CPU by the host.
- H9, process state: most long handoffs of a slow process share one placement class. The counts of a process cannot show whether that placement persists over time.

## Decision

Each transaction handoff records its time and the placement of its two threads, in plain and profiled processes alike.
- The producer reads its CPU with `sched_getcpu` just before `Enqueue` and puts it in the queue item. The consumer reads its CPU just after `Dequeue` returns.
- The placement is `sameCpu`, `sameCore` (two CPUs with the same `physical_package_id` and `core_id` in `/sys/devices/system/cpu`), `otherCore`, or `unknown` when a CPU or its topology cannot be read.
- The handoff time goes into one of five classes: below 50, 50 to below 150, 150 to below 500, 500 to below 2000, and at least 2000 microseconds.
- `QueueHandoffs` keeps a count for each class and placement. Each plain transaction record and each `queueStats` entry of a profile record holds them as `handoffPlacement`.
- The plain and the profiled arms run the same code. It adds two `sched_getcpu` calls, a classification, and one counter to each transaction, outside the timed handoff span. This changes the transaction work slightly, and the records do not show that the cost is equal in both arms.
- Each transaction mode reads the CPU topology before it starts its workers, so the read is outside the timed work.
- The two CPU reads bracket the handoff. A thread can move between the wake and a read, so a placement is the relation of these two end points, not of the wake.
- The `spintransaction` records also hold the counts, but a spinning consumer is often not woken, so this plan does not use them.
- The analysis uses the plain arms and the speedup 0 experiments of the profiles. The other experiments pay delay inside the timed handoff, because `Dequeue` posts before it returns.
- The workload, the rule, the bounds, the period, and the criteria of the earlier plans do not change.

## Acceptance criteria

These criteria are fixed before the first run. They add to criteria 1 to 116 and change none.

117. A unit check of the analyzer shows that a transaction record is invalid when its `handoffPlacement` counts do not add up to its handoffs. Records without the field stay valid.
118. On the reviewed tree, the `transaction` suite runs once with the default period. The run is retained, and criterion 88 applies to it.
119. The test of H7 uses the long handoffs, of at least 150 microseconds, and the short handoffs, below 50 microseconds, of the plain arms and the speedup 0 experiments, pooled. The shares count only handoffs with a placement other than `unknown`.
     With at least 30 such long handoffs and at least 30 such short handoffs, H7 is supported if at least 80% of the long handoffs and at most 20% of the short handoffs are `sameCpu`. H7 is rejected if at most 50% of the long handoffs are `sameCpu`.
     A rejection applies to H7 as written. It does not exclude a long wake that ends with a move. Any other result, or fewer than 30 such long or short handoffs, is not decisive. In each case this line of work stops after the one run.
120. The share of each placement class for the long handoffs of each slow arm, with a mean handoff of at least 150 microseconds, is reported as the test of H9.
121. After the commit, every source hash in the new manifest equals the SHA-256 of the committed blob. No run is repeated to replace a failure.

See [target-local handoff parts](target-local-handoff-parts.md), [target-local worker pinning](target-local-worker-pinning.md), and [target-local status](target-local-status.md).
