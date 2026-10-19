# Target-local spin transaction profile

Evidence: `artifacts/target-local-spintransaction/20261019T150209127-dbb260bc`.

`TargetLocalSpinTransactionVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `spin-transaction-profile` process and seven `plain-spin-transaction` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms with a latency progress point. Worker 0 begins a transaction, runs `Work.selected 40`, and enqueues it in a `TargetQueue` of 4 items. Worker 1 dequeues it, spinning up to 1 ms before it blocks, runs `Work.peer 8`, and ends it. The prediction is `1 - (VL / V) / (VL0 / V0)`, where VL sums the virtual latency of the transactions, each its wall latency minus the global delay added during it and at least 0, and V counts the departures. The real gain is `1 - W / W0`, where W is the mean wall latency.
The criteria are in [the spin queue plan](../../../lode/plans/target-local-spin-queue.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points peer-25, peer-50 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 24.34% | 21.22% | 4.27 pp | 9.32 pp | Pass |
| `selected-50` | 5 / 5 | 43.80% | 40.86% | 3.68 pp | 5.40 pp | Pass |
| `selected-75` | 5 / 5 | 61.80% | 61.53% | 1.62 pp | 6.81 pp | Pass |
| `peer-25` | 5 / 5 | 12.52% | 3.04% | 9.56 pp | 13.11 pp | Fail |
| `peer-50` | 5 / 5 | 10.67% | 5.47% | 5.06 pp | 13.15 pp | Fail |
| `peer-75` | 5 / 5 | 15.45% | 12.43% | 2.54 pp | 9.57 pp | Pass |

The producer sets the rate at every point, so the latency of a transaction is about the sum of both parts. The model gives `Work.selected` 20.8%, 41.7%, and 62.5%, and `Work.peer` 4.2%, 8.3%, and 12.5%. The model leaves out the queue handoff and any queue wait.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.998, 0.943 |
| `selected-50` | 0.983, 0.877 |
| `selected-75` | 0.971, 0.764 |
| `peer-25` | 0.982, 1.104 |
| `peer-50` | 0.991, 1.106 |
| `peer-75` | 0.989, 1.171 |

The median work ratio by run order in the process is order 0: 0.989, 1.000; order 1: 0.994, 1.000; order 2: 0.983, 1.000; order 3: 0.983, 1.104; order 4: 1.019, 1.000; order 5: 0.991, 0.957; order 6: 0.981, 0.921. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Queue handoff

The handoff of an item is the time from just before its enqueue to the return of the dequeue that takes it. The table gives the median over valid blocks of the mean handoff, in the profile experiment and in the real arm. The baseline row compares the profile baseline with the plain original arm, and it is the only row that compares equal measures. At a speedup above 0, a profiled handoff also includes the delay that the producer pays in `Enqueue`. At every point, the handoff also includes the time that an item waits in the queue.

| Point | Median mean handoff, profile | Median mean handoff, real arm |
| --- | ---: | ---: |
| baseline | 8.9 us | 117.8 us |
| `selected-25` | 8.0 us | 92.0 us |
| `selected-50` | 8.5 us | 75.3 us |
| `selected-75` | 67.7 us | 52.6 us |
| `peer-25` | 82.1 us | 123.8 us |
| `peer-50` | 46.5 us | 123.4 us |
| `peer-75` | 70.9 us | 105.9 us |

The second value is the signed difference between the dequeue return and the enqueue return of the same item, as a mean. It can be negative, because the consumer can take an item before the producer reads its time after `Enqueue`. The table gives the median over valid blocks of the baseline values.

| Arm | Mean enqueue time | Mean dequeue return minus enqueue return | Share of spin takes |
| --- | ---: | ---: | ---: |
| Profile baseline | 2.6 us | 6.8 us | 98.8% |
| Plain original | 34.7 us | 82.6 us | 96.2% |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | 9.32 pp | 5.40 pp | 0.28 pp | 13.11 pp | 13.15 pp | 9.57 pp |
| 2 | Yes | -4.27 pp | -4.83 pp | -1.62 pp | -8.81 pp | -5.06 pp | 0.89 pp |
| 3 | Yes | 6.68 pp | 3.38 pp | -0.53 pp | 13.07 pp | 1.54 pp | 2.54 pp |
| 4 | Yes | -0.07 pp | -0.44 pp | -4.50 pp | 6.11 pp | 1.22 pp | 0.58 pp |
| 5 | Yes | 1.16 pp | 3.68 pp | 6.81 pp | 9.56 pp | 9.19 pp | 6.55 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T124806158-79f85b15` | First spin transaction profile run | Fail. `peer-25` median 5.34 points and `peer-75` median 6.92 points. The three `selected` points pass. |
| `20261019T131601688-284620fc` | Handoff parts | Fail. `peer-75` median 7.29 points. The other five points pass. |
| `20261019T150209127-dbb260bc` | Worker pinning | Fail. Points peer-25, peer-50 fail, or a run-level criterion fails. |

## Limits

A latency experiment counts each transaction that ends, from the first work start to the last finish. The consumer drains the queue after the stop, so each transaction that begins also ends.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetQueue` with one producer and one consumer, with a latency progress point. CLR safety outside this workload is not established.
