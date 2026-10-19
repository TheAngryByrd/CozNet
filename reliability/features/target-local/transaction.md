# Target-local transaction profile

Evidence: `artifacts/target-local-transaction/20261019T175316141-ec3885bd`.

`TargetLocalTransactionVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `transaction-profile` process and seven `plain-transaction` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms with a latency progress point. Worker 0 begins a transaction, runs `Work.selected 40`, and enqueues it in a `TargetQueue` of 4 items. Worker 1 dequeues it, runs `Work.peer 8`, and ends it. The prediction is `1 - (VL / V) / (VL0 / V0)`, where VL sums the virtual latency of the transactions, each its wall latency minus the global delay added during it and at least 0, and V counts the departures. The real gain is `1 - W / W0`, where W is the mean wall latency.
The criteria are in [the latency point plan](../../../lode/plans/target-local-transactions.md) and [the transaction delay plan](../../../lode/plans/target-local-transaction-delay.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 23.71% | 19.93% | 4.34 pp | 15.75 pp | Fail |
| `selected-50` | 5 / 5 | 47.78% | 40.83% | 6.37 pp | 12.01 pp | Fail |
| `selected-75` | 5 / 5 | 63.41% | 59.31% | 4.62 pp | 16.26 pp | Fail |
| `peer-25` | 5 / 5 | 4.51% | 4.75% | 4.83 pp | 13.88 pp | Fail |
| `peer-50` | 5 / 5 | 7.31% | 8.07% | 6.50 pp | 14.23 pp | Fail |
| `peer-75` | 5 / 5 | 10.62% | 10.88% | 4.50 pp | 12.56 pp | Fail |

The producer sets the rate at every point, so the latency of a transaction is about the sum of both parts. The model gives `Work.selected` 20.8%, 41.7%, and 62.5%, and `Work.peer` 4.2%, 8.3%, and 12.5%. The model leaves out the queue handoff and any queue wait.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 1.011, 0.948 |
| `selected-50` | 0.999, 0.879 |
| `selected-75` | 0.997, 0.688 |
| `peer-25` | 1.000, 1.053 |
| `peer-50` | 1.001, 1.100 |
| `peer-75` | 0.998, 1.143 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 0.999, 1.000; order 2: 1.000, 1.000; order 3: 1.000, 1.031; order 4: 1.000, 1.000; order 5: 1.010, 0.948; order 6: 0.994, 0.926. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Queue handoff

The handoff of an item is the time from just before its enqueue to the return of the dequeue that takes it. The table gives the median over valid blocks of the mean handoff, in the profile experiment and in the real arm. The baseline row compares the profile baseline with the plain original arm, and it is the only row that compares equal measures. At a speedup above 0, a profiled handoff also includes the delay that the producer pays in `Enqueue`. At every point, the handoff also includes the time that an item waits in the queue.

| Point | Median mean handoff, profile | Median mean handoff, real arm |
| --- | ---: | ---: |
| baseline | 46.7 us | 35.9 us |
| `selected-25` | 51.7 us | 34.4 us |
| `selected-50` | 56.2 us | 25.3 us |
| `selected-75` | 174.1 us | 30.8 us |
| `peer-25` | 59.2 us | 37.0 us |
| `peer-50` | 76.7 us | 38.4 us |
| `peer-75` | 96.4 us | 38.0 us |

The second value is the signed difference between the dequeue return and the enqueue return of the same item, as a mean. It can be negative, because the consumer can take an item before the producer reads its time after `Enqueue`. The table gives the median over valid blocks of the baseline values.

| Arm | Mean enqueue time | Mean dequeue return minus enqueue return | Share of spin takes |
| --- | ---: | ---: | ---: |
| Profile baseline | 13.3 us | 33.6 us | 0.0% |
| Plain original | 12.7 us | 24.4 us | 0.0% |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | 0.42 pp | 9.99 pp | 12.99 pp | -2.62 pp | 0.15 pp | 10.09 pp |
| 2 | Yes | 6.50 pp | -3.69 pp | 2.01 pp | 3.16 pp | 6.50 pp | 1.57 pp |
| 3 | Yes | 15.75 pp | 12.01 pp | 16.26 pp | 13.88 pp | 14.23 pp | 12.56 pp |
| 4 | Yes | 4.34 pp | 6.37 pp | 4.10 pp | 8.43 pp | -0.76 pp | -2.52 pp |
| 5 | Yes | -1.74 pp | 4.70 pp | 4.62 pp | -4.83 pp | -8.18 pp | -4.50 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T110105861-414dba20` | First transaction profile run | Fail. All six points fail. The real gains follow the model, but the `peer` predictions stay near 5% at each speedup. |
| `20261019T113356248-f1cbfddc` | Per-transaction virtual latency | Fail. `selected-50` median 7.09 points and `peer-25` median 5.47 points. The other four points pass. |
| `20261019T121118409-e589636a` | Transaction queue handoff | Fail. `selected-25` median 7.58 points and `selected-50` median 10.07 points. The other four points pass. |
| `20261019T124914850-b10c2f47` | Spin before a queue wait | Fail. `selected-25` one block of 27.61 points, `selected-50` median 8.32 points, and `peer-50` one block of -12.59 points. |
| `20261019T131709144-4aa0a3ef` | Handoff parts | Fail. `selected-25` median 7.25 points and `selected-50` median 7.76 points. |
| `20261019T150326902-04963faa` | Worker pinning | Fail. Five points fail. Only `selected-75` passes. |
| `20261019T175316141-ec3885bd` | Matched plain warm-up | Fail. Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. |

## Limits

A latency experiment counts each transaction that ends, from the first work start to the last finish. The consumer drains the queue after the stop, so each transaction that begins also ends.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetQueue` with one producer and one consumer, with a latency progress point. CLR safety outside this workload is not established.
