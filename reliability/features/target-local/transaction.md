# Target-local transaction profile

Evidence: `artifacts/target-local-transaction/20261019T121118409-e589636a`.

`TargetLocalTransactionVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `transaction-profile` process and seven `plain-transaction` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms with a latency progress point. Worker 0 begins a transaction, runs `Work.selected 40`, and enqueues it in a `TargetQueue` of 4 items. Worker 1 dequeues it, runs `Work.peer 8`, and ends it. The prediction is `1 - (VL / V) / (VL0 / V0)`, where VL sums the virtual latency of the transactions, each its wall latency minus the global delay added during it and at least 0, and V counts the departures. The real gain is `1 - W / W0`, where W is the mean wall latency.
The criteria are in [the latency point plan](../../../lode/plans/target-local-transactions.md) and [the transaction delay plan](../../../lode/plans/target-local-transaction-delay.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-25, selected-50 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 25.31% | 18.79% | 7.58 pp | 8.97 pp | Fail |
| `selected-50` | 5 / 5 | 50.01% | 40.69% | 10.07 pp | 13.28 pp | Fail |
| `selected-75` | 5 / 5 | 65.58% | 63.11% | 3.29 pp | 8.42 pp | Pass |
| `peer-25` | 5 / 5 | 4.40% | 3.04% | 2.02 pp | 3.77 pp | Pass |
| `peer-50` | 5 / 5 | 7.75% | 5.23% | 4.48 pp | 5.84 pp | Pass |
| `peer-75` | 5 / 5 | 11.59% | 9.29% | 1.46 pp | 5.21 pp | Pass |

The producer sets the rate at every point, so the latency of a transaction is about the sum of both parts. The model gives `Work.selected` 20.8%, 41.7%, and 62.5%, and `Work.peer` 4.2%, 8.3%, and 12.5%. The model leaves out the queue handoff and any queue wait.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.998, 0.919 |
| `selected-50` | 0.993, 0.846 |
| `selected-75` | 1.012, 0.748 |
| `peer-25` | 0.998, 1.046 |
| `peer-50` | 0.996, 1.091 |
| `peer-75` | 1.002, 1.141 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 1.000, 1.000; order 2: 1.000, 1.000; order 3: 1.000, 1.031; order 4: 1.000, 1.000; order 5: 0.991, 0.919; order 6: 0.996, 0.893. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Queue handoff

The handoff of an item is the time from just before its enqueue to the return of the dequeue that takes it. The table gives the median over valid blocks of the mean handoff, in the profile experiment and in the real arm. The baseline row compares the profile baseline with the plain original arm, and it is the only row that compares equal measures. At a speedup above 0, a profiled handoff also includes the delay that the producer pays in `Enqueue`. At every point, the handoff also includes the time that an item waits in the queue.

| Point | Median mean handoff, profile | Median mean handoff, real arm |
| --- | ---: | ---: |
| baseline | 51.3 us | 205.6 us |
| `selected-25` | 55.1 us | 180.0 us |
| `selected-50` | 60.0 us | 121.8 us |
| `selected-75` | 131.5 us | 69.3 us |
| `peer-25` | 61.2 us | 204.1 us |
| `peer-50` | 81.3 us | 210.0 us |
| `peer-75` | 97.1 us | 207.0 us |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -1.16 pp | 2.41 pp | 1.42 pp | -0.38 pp | 0.64 pp | 0.84 pp |
| 2 | Yes | 8.97 pp | 6.32 pp | 2.29 pp | -0.60 pp | 1.69 pp | 3.92 pp |
| 3 | Yes | 7.58 pp | 11.05 pp | -8.42 pp | 3.77 pp | 5.84 pp | 1.46 pp |
| 4 | Yes | 8.50 pp | 13.28 pp | 3.29 pp | 2.96 pp | 5.37 pp | 5.21 pp |
| 5 | Yes | 4.46 pp | 10.07 pp | 4.08 pp | 2.02 pp | -4.48 pp | 0.07 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T110105861-414dba20` | First transaction profile run | Fail. All six points fail. The real gains follow the model, but the `peer` predictions stay near 5% at each speedup. |
| `20261019T113356248-f1cbfddc` | Per-transaction virtual latency | Fail. `selected-50` median 7.09 points and `peer-25` median 5.47 points. The other four points pass. |
| `20261019T121118409-e589636a` | Transaction queue handoff | Fail. Points selected-25, selected-50 fail, or a run-level criterion fails. |

## Limits

A latency experiment counts each transaction that ends, from the first work start to the last finish. The consumer drains the queue after the stop, so each transaction that begins also ends.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetQueue` with one producer and one consumer, with a latency progress point. CLR safety outside this workload is not established.
