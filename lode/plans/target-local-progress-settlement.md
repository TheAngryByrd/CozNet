# Target-local progress settlement

## Finding

The matched plain warm-up removes the plain handoff gap, but the `transaction` suite still fails all six points. The failures have two separate parts.

### Bias of the `selected` points

The signed difference of a point is its predicted gain minus its actual gain, in percentage points. These are the medians over the five blocks of each unpinned `transaction` run with `VirtualLatencyNs`:

| Run | `selected-25` | `selected-50` | `selected-75` | `peer-25` | `peer-50` | `peer-75` |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `20261019T113356248-f1cbfddc` | 5.0 | 7.1 | 0.3 | 1.3 | 0.5 | 1.1 |
| `20261019T121118409-e589636a` | 7.6 | 10.1 | 2.3 | 2.0 | 1.7 | 1.5 |
| `20261019T124914850-b10c2f47` | 4.0 | 8.2 | 2.0 | 0.4 | -4.6 | -1.2 |
| `20261019T131709144-4aa0a3ef` | 7.3 | 7.8 | 2.6 | -0.1 | 1.6 | -1.6 |
| `20261019T150326902-04963faa` | 4.0 | 6.0 | 3.9 | 2.9 | -1.4 | 0.0 |
| `20261019T175316141-ec3885bd` | 4.3 | 6.4 | 4.6 | 3.2 | 0.1 | 1.6 |

- The `selected` predictions are above the actual gains in every run, before and after the warm-up change. The medians of the six runs are 4.65, 7.45, and 2.45 points.
- The `peer` medians stay between -4.6 and 3.2 points, with no common sign.
- The actual gains put the `selected` work at about 80% of the transaction latency. The median predictions of about 24, 47.5, and 65.5% are near the speedups of 25, 50, and 75%.

### Dispersion between blocks

Each block has one baseline experiment, so one slow baseline moves all six points of its block.
- In run `20261019T175316141-ec3885bd`, the baseline of block 3 is 18.1% above the median baseline. Its queue handoff takes 29.6 ms in total, against 6.3 ms and 7.5 ms in the baselines of blocks 1 and 4. It has no collection.
- If the median baseline replaces it, the six differences of block 3 fall from 12.0 to 16.3 points to -1.3 to 11.3 points.
- Run `20261019T150326902-04963faa` has a similar baseline in block 1, with a mean handoff of 181.8 microseconds.
- The five warm plain `original` arms of the latest run have mean handoffs of 33.3 to 42.9 microseconds. Five arms are too few to compare the rate of long handoffs.
- Lock block 1 has no single shared outlier. Its plain `original` is 5% slow, its baseline 2% fast, and its `selected` experiments slower than in the other blocks.

This plan does not change the block design.

## Hypotheses

- H1, deferred payment at the end: the `peer` thread calls `counter.End` and then `Dequeue`. `Dequeue` calls `coz_target_post`, which catches up the debt of the thread.
  So debt that the `peer` accrues during its work for transaction k is paid after the end of k, unless a sample handler pays it first. The virtual latency of k subtracts all global delay of its window, but its wall time does not include that payment.
  The `selected` thread calls `Enqueue`, and so `coz_target_post`, before its next `Begin`. So H1 predicts a bias for `selected` experiments and none for `peer` experiments. The records show this.
- H2, overlap off the dependency path: the window of k includes the `selected` work of k + 1. H1 is the form of this overlap that the profiler can change, because the delay of that work falls on the `peer` thread inside the window.
- H3, a fault in the actual gains: the actual gains follow one share of about 80% at each speedup, and the `peer` points agree. So H3 does not explain the bias.

A test of H1 from the thread summaries: if all `peer` payment falls after the end, the bias equals the `peer` payment for each transaction, over the baseline virtual latency for each transaction.
- At 25%, the payment is 4.6 to 5.3 points and the bias 4.0 to 7.6 points in the six runs.
- At 50%, the payment is 9.4 to 11.6 points and the bias 6.0 to 10.1 points.
- At 75%, the payment is about 21 points and the bias 0.3 to 4.6 points.

So the records are consistent with H1 at 25%. At 50% and 75%, H1 needs part of the payment to fall inside the window, and the summaries cannot locate payments. The records show the order of the calls, but they do not establish H1.

## Decision

A thread pays its debt before it stamps a transaction begin or end.
- `TargetProgressCounter.Begin` and `TargetProgressCounter.End` call `TargetWait.post` before they read the clock. `post` catches up the debt of the calling thread, as before a queue release.
- `post` also raises the posted maximum of the experiment, and the counter discards the returned token. This adds a post event to the journal, as an `Enqueue` with no waiter does.
- Without a target thread, `post` does nothing, so the plain arms do not change.
- In this workload, the next call of the `peer` thread after `End` is already a `post`. So the change moves an existing payment a few instructions earlier.
- `Visit` does not settle, so the throughput, pipeline, lock, and semaphore workloads do not change.
- Upstream Coz catches up before it unblocks other threads. Its progress points also catch up on macOS, but not on Linux, where its sample handler already applies delays and a second application collapsed throughput.
- A CozNet catch-up pays only the debt that the thread still owes, so a second catch-up does not apply delay twice.
- Coz computes latency from counts of begins and ends. The CozNet rule uses the time of each stamp, so it depends on where a payment falls relative to the stamps.
- `TargetProgressCounter` is public. A caller that stamps a transaction far from a settlement now pays its debt at the stamp. A caller that stamps inside a lock lengthens the critical section by that payment.
- Accepted risk: each profiled transaction makes two more native calls, and the plain arms make none. The `End` call falls inside the window of its transaction, and the `Begin` call falls before it. The `Begin` call pays little in this workload, because `Enqueue` has just posted. The run reports the baseline handoff and latency against the prior run.
- The prediction formula, the rule, the bounds, and the criteria of the earlier plans do not change.

## Acceptance criteria

These criteria are fixed before the first run. They add to criteria 1 to 108 and change none.

109. A unit test shows that `Begin` and `End` settle before they read the clock, and that a counter without a target thread stamps as before.
110. The test of H1: in one `transaction` run on the reviewed tree, the median signed difference over valid blocks is between -2.3 and 2.3 points for `selected-25`, and between -3.7 and 3.7 points for `selected-50`. These limits are half of the medians of the six prior runs, rounded down.
     Each `peer` point keeps a median signed difference between -5 and 5 points.
     If a `selected` limit fails, this line of work stops, and the bias stays recorded as unexplained. That result alone does not show that H1 is false.
111. The `transaction` and `spintransaction` suites run once each on the reviewed tree. Criterion 88 does not change, and each failure is retained. The `spintransaction` medians are reported without a limit. The other suites do not call `Begin` or `End`, so they do not run again.
112. After the commit, every source hash in each new manifest equals the SHA-256 of the committed blob. No run is repeated to replace a failure.

See [target-local transaction delay](target-local-transaction-delay.md), [target-local plain warm-up](target-local-plain-warmup.md), and [target-local status](target-local-status.md).
