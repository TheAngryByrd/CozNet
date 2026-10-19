# Target-local spin transaction profile

Evidence: `artifacts/target-local-spintransaction/20261019T124806158-79f85b15`.

`TargetLocalSpinTransactionVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `spin-transaction-profile` process and seven `plain-spin-transaction` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms with a latency progress point. Worker 0 begins a transaction, runs `Work.selected 40`, and enqueues it in a `TargetQueue` of 4 items. Worker 1 dequeues it, spinning up to 1 ms before it blocks, runs `Work.peer 8`, and ends it. The prediction is `1 - (VL / V) / (VL0 / V0)`, where VL sums the virtual latency of the transactions, each its wall latency minus the global delay added during it and at least 0, and V counts the departures. The real gain is `1 - W / W0`, where W is the mean wall latency.
The criteria are in [the spin queue plan](../../../lode/plans/target-local-spin-queue.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points peer-25, peer-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 21.35% | 18.38% | 4.85 pp | 6.82 pp | Pass |
| `selected-50` | 5 / 5 | 44.31% | 40.48% | 4.16 pp | 7.96 pp | Pass |
| `selected-75` | 5 / 5 | 63.93% | 62.41% | 1.57 pp | 9.27 pp | Pass |
| `peer-25` | 5 / 5 | 3.81% | 5.40% | 5.34 pp | 7.28 pp | Fail |
| `peer-50` | 5 / 5 | 4.36% | 6.76% | 2.40 pp | 8.57 pp | Pass |
| `peer-75` | 5 / 5 | 4.56% | 11.18% | 6.92 pp | 12.47 pp | Fail |

The producer sets the rate at every point, so the latency of a transaction is about the sum of both parts. The model gives `Work.selected` 20.8%, 41.7%, and 62.5%, and `Work.peer` 4.2%, 8.3%, and 12.5%. The model leaves out the queue handoff and any queue wait.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.999, 0.932 |
| `selected-50` | 0.991, 0.875 |
| `selected-75` | 0.989, 0.870 |
| `peer-25` | 1.000, 1.043 |
| `peer-50` | 1.012, 1.060 |
| `peer-75` | 1.009, 1.084 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 1.000, 1.000; order 2: 1.000, 0.982; order 3: 1.000, 1.010; order 4: 0.999, 0.974; order 5: 0.989, 0.921; order 6: 0.999, 0.987. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Queue handoff

The handoff of an item is the time from just before its enqueue to the return of the dequeue that takes it. The table gives the median over valid blocks of the mean handoff, in the profile experiment and in the real arm. The baseline row compares the profile baseline with the plain original arm, and it is the only row that compares equal measures. At a speedup above 0, a profiled handoff also includes the delay that the producer pays in `Enqueue`. At every point, the handoff also includes the time that an item waits in the queue.

| Point | Median mean handoff, profile | Median mean handoff, real arm |
| --- | ---: | ---: |
| baseline | 6.4 us | 104.7 us |
| `selected-25` | 4.8 us | 90.1 us |
| `selected-50` | 5.3 us | 72.3 us |
| `selected-75` | 36.0 us | 45.1 us |
| `peer-25` | 24.3 us | 105.8 us |
| `peer-50` | 35.0 us | 102.7 us |
| `peer-75` | 38.0 us | 97.9 us |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -2.61 pp | 1.38 pp | 9.27 pp | -2.71 pp | -2.40 pp | -12.47 pp |
| 2 | Yes | 5.03 pp | 4.16 pp | 1.27 pp | 7.28 pp | 1.20 pp | -7.69 pp |
| 3 | Yes | 4.85 pp | -1.19 pp | -1.57 pp | -5.70 pp | -8.57 pp | -6.92 pp |
| 4 | Yes | 6.82 pp | 6.96 pp | 6.01 pp | 5.34 pp | 0.93 pp | -1.27 pp |
| 5 | Yes | 4.73 pp | 7.96 pp | 0.38 pp | -2.87 pp | -3.82 pp | -5.68 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T124806158-79f85b15` | Spin before a queue wait | Fail. Points peer-25, peer-75 fail, or a run-level criterion fails. |

## Limits

A latency experiment counts each transaction that ends, from the first work start to the last finish. The consumer drains the queue after the stop, so each transaction that begins also ends.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetQueue` with one producer and one consumer, with a latency progress point. CLR safety outside this workload is not established.
