# Target-local transaction profile

Evidence: `artifacts/target-local-transaction/20261019T113356248-f1cbfddc`.

`TargetLocalTransactionVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `transaction-profile` process and seven `plain-transaction` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms with a latency progress point. Worker 0 begins a transaction, runs `Work.selected 40`, and enqueues it in a `TargetQueue` of 4 items. Worker 1 dequeues it, runs `Work.peer 8`, and ends it. The prediction is `1 - (VL / V) / (VL0 / V0)`, where VL sums the virtual latency of the transactions, each its wall latency minus the global delay added during it and at least 0, and V counts the departures. The real gain is `1 - W / W0`, where W is the mean wall latency.
The criteria are in [the latency point plan](../../../lode/plans/target-local-transactions.md) and [the transaction delay plan](../../../lode/plans/target-local-transaction-delay.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-50, peer-25 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 23.77% | 19.31% | 4.97 pp | 7.74 pp | Pass |
| `selected-50` | 5 / 5 | 45.30% | 40.09% | 7.09 pp | 13.57 pp | Fail |
| `selected-75` | 5 / 5 | 63.99% | 62.90% | 0.99 pp | 4.66 pp | Pass |
| `peer-25` | 5 / 5 | 5.50% | 2.90% | 5.47 pp | 10.85 pp | Fail |
| `peer-50` | 5 / 5 | 8.25% | 6.66% | 0.45 pp | 7.74 pp | Pass |
| `peer-75` | 5 / 5 | 10.20% | 8.86% | 2.29 pp | 5.07 pp | Pass |

The producer sets the rate at every point, so the latency of a transaction is about the sum of both parts. The model gives `Work.selected` 20.8%, 41.7%, and 62.5%, and `Work.peer` 4.2%, 8.3%, and 12.5%. The model leaves out the queue handoff and any queue wait.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 1.014, 0.951 |
| `selected-50` | 0.982, 0.846 |
| `selected-75` | 0.978, 0.718 |
| `peer-25` | 0.988, 1.036 |
| `peer-50` | 0.988, 1.072 |
| `peer-75` | 1.025, 1.168 |

The median work ratio by run order in the process is order 0: 0.991, 1.000; order 1: 1.014, 1.000; order 2: 1.016, 1.000; order 3: 1.000, 1.000; order 4: 0.988, 1.000; order 5: 0.978, 0.998; order 6: 0.966, 0.889. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | 3.97 pp | 7.09 pp | 0.32 pp | 1.35 pp | 0.20 pp | 5.07 pp |
| 2 | Yes | 6.33 pp | 13.57 pp | -0.99 pp | 10.85 pp | 6.70 pp | -1.41 pp |
| 3 | Yes | 7.74 pp | 4.08 pp | 4.66 pp | 5.71 pp | 7.74 pp | 1.14 pp |
| 4 | Yes | 4.97 pp | 7.44 pp | 0.15 pp | -5.47 pp | 0.45 pp | 2.29 pp |
| 5 | Yes | -0.40 pp | 0.75 pp | 2.95 pp | 0.18 pp | 0.22 pp | -3.13 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T110105861-414dba20` | First transaction profile run | Fail. All six points fail. The real gains follow the model, but the `peer` predictions stay near 5% at each speedup. |
| `20261019T113356248-f1cbfddc` | Per-transaction virtual latency | Fail. Points selected-50, peer-25 fail, or a run-level criterion fails. |

## Limits

A latency experiment counts each transaction that ends, from the first work start to the last finish. The consumer drains the queue after the stop, so each transaction that begins also ends.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetQueue` with one producer and one consumer, with a latency progress point. CLR safety outside this workload is not established.
