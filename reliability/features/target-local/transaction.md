# Target-local transaction profile

Evidence: `artifacts/target-local-transaction/20261019T110105861-414dba20`.

`TargetLocalTransactionVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `transaction-profile` process and seven `plain-transaction` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms with a latency progress point. Worker 0 begins a transaction, runs `Work.selected 40`, and enqueues it in a `TargetQueue` of 4 items. Worker 1 dequeues it, runs `Work.peer 8`, and ends it. The prediction is `1 - (W * E / T) / (W0 * E0 / T0)`, where W is the mean wall latency, E the effective time, and T the elapsed time. The real gain is `1 - W / W0`.
The criteria are in [the latency point plan](../../../lode/plans/target-local-transactions.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 27.91% | 20.13% | 5.43 pp | 21.30 pp | Fail |
| `selected-50` | 5 / 5 | 51.48% | 41.96% | 10.19 pp | 15.28 pp | Fail |
| `selected-75` | 5 / 5 | 69.96% | 63.09% | 7.24 pp | 15.64 pp | Fail |
| `peer-25` | 5 / 5 | 5.12% | 5.11% | 7.25 pp | 19.88 pp | Fail |
| `peer-50` | 5 / 5 | 4.67% | 8.69% | 9.25 pp | 14.42 pp | Fail |
| `peer-75` | 5 / 5 | 4.51% | 13.40% | 12.06 pp | 14.97 pp | Fail |

The producer sets the rate at every point, so the latency of a transaction is about the sum of both parts. The model gives `Work.selected` 20.8%, 41.7%, and 62.5%, and `Work.peer` 4.2%, 8.3%, and 12.5%. The model leaves out the queue handoff and any queue wait.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.970, 0.903 |
| `selected-50` | 0.982, 0.867 |
| `selected-75` | 0.993, 0.741 |
| `peer-25` | 0.979, 1.020 |
| `peer-50` | 0.998, 1.102 |
| `peer-75` | 0.982, 1.123 |

The median work ratio by run order in the process is order 0: 0.998, 1.000; order 1: 0.993, 1.000; order 2: 0.982, 1.000; order 3: 0.979, 1.020; order 4: 1.000, 1.000; order 5: 0.994, 0.912; order 6: 0.971, 0.903. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | 21.30 pp | 15.28 pp | 15.64 pp | 19.88 pp | 14.42 pp | 12.06 pp |
| 2 | Yes | 5.43 pp | 6.91 pp | 5.39 pp | -5.43 pp | -9.25 pp | -9.66 pp |
| 3 | Yes | 17.28 pp | 13.11 pp | 5.06 pp | 8.33 pp | 9.46 pp | 6.52 pp |
| 4 | Yes | 4.45 pp | 10.19 pp | 8.76 pp | 7.25 pp | -3.52 pp | -13.86 pp |
| 5 | Yes | -3.14 pp | 2.40 pp | 7.24 pp | -5.17 pp | -9.11 pp | -14.97 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T110105861-414dba20` | Latency progress points | Fail. Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. |

## Limits

A latency experiment counts each transaction that ends, from the first work start to the last finish. The consumer drains the queue after the stop, so each transaction that begins also ends.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetQueue` with one producer and one consumer, with a latency progress point. CLR safety outside this workload is not established.
