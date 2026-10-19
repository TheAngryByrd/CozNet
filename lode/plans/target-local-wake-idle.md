# Target-local wake after idle

## Finding

After [the progress settlement](target-local-progress-settlement.md), `selected-75` predicts too little gain in each `transaction` run at 1 ms: -11.83, -10.92, and -10.48 points.

In run `20261019T193820084-e379d1a4`, these are medians over the five blocks, in microseconds for each transaction:

| Arm | Latency | Period | Handoff |
| --- | ---: | ---: | ---: |
| Plain `original` | 849.6 | 686.8 | 42.5 |
| Plain `selected-75` | 323.0 | 176.6 | 9.7 |
| Profile baseline | 875.6 | 703.8 | 45.7 |
| Profile `selected` at 75%, virtual | 422.9 | 183.5 | not comparable |

- The period is the elapsed time for each transaction. The virtual period of the profile is its effective time for each transaction.
- The profile predicts the period of the plain `selected-75` arm within 7 microseconds, but it predicts a latency that is 99.9 microseconds longer.
- The handoff of a profile experiment with delay holds paid delay, because `Dequeue` posts before it returns. So it cannot be compared with a plain handoff.

The plain handoff changes with the period:

| Run | `original` | `selected-25` | `selected-50` | `selected-75` | `peer-75` |
| --- | ---: | ---: | ---: | ---: | ---: |
| `20261019T175316141-ec3885bd` | 36 | 34 | 25 | 31 | 38 |
| `20261019T183627302-c5843c4c` | 38 | 33 | 26 | 12 | 38 |
| `20261019T191349654-efea55a8` | 41 | 42 | 29 | 13 | 41 |
| `20261019T193820084-e379d1a4` | 43 | 36 | 20 | 10 | 42 |

The values are medians over the five blocks, in microseconds. The `selected` arms have shorter periods, so their consumer idles for a shorter time before each item. The `peer` arms keep the period of `original`.

In run `20261019T175316141-ec3885bd`, the plain `selected-75` handoff is 31 microseconds, close to the 36 of `original`. The plain arms did not change between that run and `20261019T183627302-c5843c4c`, because the settlement acts only on target threads.
So the same plain program gave a long handoff in one run and a short handoff in three. This is evidence for variation between runs or for H11.

In `spintransaction` run `20261019T183747700-877bf50a`, where the consumer spins for up to 1 ms before it sleeps, `selected-75` has a signed median of -5.44 points.

## Hypotheses

- H10, wake after idle: the time from an enqueue to the dequeue of a sleeping consumer grows with the time that the consumer was idle before the enqueue.
  A virtual speedup does not shorten the real idle time of the consumer, so the profile keeps the long wake of `original`. In the real `selected-75` program the wake is short, and the shorter wake also reduces queueing.
- H11, other cause: the wake time does not depend on the idle time, and the short plain handoff of `selected-75` comes from another difference between the arms.

H10 does not need a cause in the host. It describes a relation that the sample can measure in each process.

## Decision

Each transaction handoff also records the idle time of the consumer before the item.
- The consumer reads the clock before it calls `Dequeue`. The idle time is the producer clock read before `Enqueue` minus that consumer read. When the enqueue started before the call, the class is `ready`.
- This idle time is a proxy. A producer pause after its clock read can class a blocked dequeue as `ready`, and a preemption of the consumer counts as idle time. The records do not show whether the consumer slept.
- The idle classes are `ready`, below 100, 100 to below 400, and at least 400 microseconds.
- The handoff classes are below 10, 10 to below 25, 25 to below 50, 50 to below 150, and at least 150 microseconds.
- `QueueHandoffs` counts each pair of classes. Each plain transaction record and each `queueStats` entry holds the counts as `handoffIdle`.
- The added work is one clock read and one counter for each transaction, in plain and profiled arms alike. When the producer clock read comes before the consumer read, as in the `ready` case, the consumer read falls inside the measured handoff. The records do not show that the cost is equal in both arms.
- The analysis uses only the plain arms of the `transaction` suite, because a profile handoff with delay holds paid delay.
- The workload, the rule, the bounds, the period, and the criteria of the earlier plans do not change.

## Acceptance criteria

These criteria are fixed before the first run. They add to criteria 1 to 121 and change none.

122. A unit check of the analyzer shows that a transaction record is invalid when its `handoffIdle` counts do not add up to its handoffs. Records without the field stay valid.
123. On the reviewed tree, the `transaction` suite runs once with the default period. The run is retained, and criterion 88 applies to it.
124. The test of H10 pools the plain arms of that run. A slow handoff is one of at least 25 microseconds.
     With at least 200 handoffs in each of the classes below 100 and at least 400 microseconds of idle time, H10 is supported if the share of slow handoffs after at least 400 microseconds of idle time is at least twice the share after less than 100.
     If the share after less than 100 microseconds is 0, H10 is supported when the other share is above 0, and the result is not decisive when both are 0.
     H10 is rejected if that ratio is at most 1.25. Any other result, or fewer than 200 handoffs in a class, is not decisive. In each case this line of work stops after the one run.
     The idle time of a pooled handoff depends mostly on the period of its arm. So a supported result shows only an association across arms, and it does not exclude H11.
125. The same shares for the plain `original` and `selected-75` arms alone are reported. For each plain arm with at least 200 handoffs in both classes, the ratio within that arm is also reported.
126. After the commit, every source hash in the new manifest equals the SHA-256 of the committed blob. No run is repeated to replace a failure.

See [target-local handoff placement](target-local-handoff-placement.md), [target-local sample period](target-local-sample-period.md), and [target-local status](target-local-status.md).
