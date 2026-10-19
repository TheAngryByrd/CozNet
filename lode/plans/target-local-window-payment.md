# Target-local window payment

## Finding

Before [the progress settlement](target-local-progress-settlement.md), the profile virtual latency of `selected-75` was below the real latency. After it, the virtual latency is far above the real latency, and the profile predicts too little gain in five `transaction` runs at 1 ms.

These are medians over the blocks whose profile `selected` 75% experiment passed, in microseconds for each transaction:

| Run | Plain `selected-75` latency | Profile `selected` 75% virtual latency |
| --- | ---: | ---: |
| `20261019T175316141-ec3885bd`, before the change | 351.9 | 323.6 |
| `20261019T183627302-c5843c4c`, 4 blocks | 323.7 | 429.4 |
| `20261019T191349654-efea55a8`, 4 blocks | 342.6 | 415.8 |
| `20261019T193820084-e379d1a4` | 323.0 | 422.9 |
| `20261019T195335859-ea098418` | 318.0 | 462.7 |
| `20261019T202333518-e70c6219` | 313.2 | 410.3 |

So the change moved the virtual latency from 28 microseconds below the real latency to 73 to 145 microseconds above it.

- The virtual latency of a transaction is its wall time minus the global delay added in its window.
- A child payment lengthens the window that holds it. If a window holds a payment for delay that was added before the window, its virtual latency grows by that payment.
- The records hold only the total payment of each thread. They cannot show which payments fall inside which windows.
- A child payment is journaled with its measured duration and a time read after the wait returns. A wake skip has no wall time.

## Hypotheses

- H12, payment from outside the window: after the change, the windows of the `selected` 75% experiments hold more paid delay than the global delay added in them. The difference makes the virtual latency too long.
- H13, other cause: the paid delay in each window is close to the global delay added in it, and the extra virtual latency has another source, such as queue waits that the real program does not have.

## Decision

Each thread record of a latency experiment gains `WindowPaymentNs`.
- For each transaction, the profile adds the overlap of each child payment of the thread, from its journal time minus its measured duration to its journal time, with the window from the begin to the end of the transaction.
- This interval is an estimate. A preemption between the end of the wait and the time read moves it later, and it can then fall in a later window.
- `WindowPaymentNs` is the sum over the transactions. A payment that overlaps two windows counts in both. In the `selected` experiments the consumer makes most payments, and a later transaction waits for the consumer, so a consumer payment can lengthen both windows. The producer can also pay, for example 13.7 microseconds in block 4 of `selected` 75% in run `20261019T202333518-e70c6219`. A payment off the dependency path of a transaction also counts, so the sum can be above 0 when the virtual latency is correct.
- `TargetCausalThread` is public, so code that constructs it must set the new field. A record without transactions has 0.
- The global delay added in the windows is `LatencyNs` minus `VirtualLatencyNs` when `NegativeTransactions` is 0.
- The computation runs after the experiment, from the journal and the transaction list, so it adds no work to the experiment.
- The workload, the rule, the bounds, the period, and the criteria of the earlier plans do not change.

## Acceptance criteria

These criteria are fixed before the first run. They add to criteria 1 to 129 and change none.

130. A unit test shows that a payment inside a window counts in full, a payment across a window edge counts only its overlap, a payment outside every window counts 0, and a payment in two windows counts in both.
131. On the reviewed tree, the `transaction` suite runs once. The run is retained, and criterion 88 applies to it.
132. The test of H12 uses the passed `selected` experiments with no negative transaction. For each one, the excess is the sum of `WindowPaymentNs` of both threads minus the added global delay, for each transaction.
     After the change, the signed medians of the `selected` points are -9.70 to 1.77 points at 25%, -4.61 to -1.50 at 50%, and -17.22 to -10.48 at 75%. So 25% and 50% are the control, although they do not pass every run. The contrast is the median excess at 75% minus the larger of the median excesses at 25% and 50%.
     H12 is supported if the contrast is at least 50 microseconds, about half of the extra virtual latency. H12 is rejected if the contrast is at most 20 microseconds. Any other result, or fewer than three experiments at a speedup, is not decisive.
     The contrast measures extra paid delay in the windows. It does not by itself show how much of that delay lengthens a transaction.
     The median excess at each speedup, including 0, is reported. In each case this line of work stops after the one run.
133. After the commit, every source hash in the new manifest equals the SHA-256 of the committed blob. No run is repeated to replace a failure.

See [target-local progress settlement](target-local-progress-settlement.md), [target-local wake after idle](target-local-wake-idle.md), and [target-local status](target-local-status.md).
