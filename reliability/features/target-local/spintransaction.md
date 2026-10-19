# Target-local spin transaction profile

Evidence: `artifacts/target-local-spintransaction/20261019T131601688-284620fc`.

`TargetLocalSpinTransactionVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `spin-transaction-profile` process and seven `plain-spin-transaction` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms with a latency progress point. Worker 0 begins a transaction, runs `Work.selected 40`, and enqueues it in a `TargetQueue` of 4 items. Worker 1 dequeues it, spinning up to 1 ms before it blocks, runs `Work.peer 8`, and ends it. The prediction is `1 - (VL / V) / (VL0 / V0)`, where VL sums the virtual latency of the transactions, each its wall latency minus the global delay added during it and at least 0, and V counts the departures. The real gain is `1 - W / W0`, where W is the mean wall latency.
The criteria are in [the spin queue plan](../../../lode/plans/target-local-spin-queue.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points peer-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 23.76% | 20.06% | 2.38 pp | 7.18 pp | Pass |
| `selected-50` | 5 / 5 | 42.68% | 41.49% | 1.83 pp | 8.05 pp | Pass |
| `selected-75` | 5 / 5 | 60.34% | 62.97% | 4.64 pp | 5.95 pp | Pass |
| `peer-25` | 5 / 5 | 6.61% | 5.35% | 1.80 pp | 8.03 pp | Pass |
| `peer-50` | 5 / 5 | 5.27% | 9.07% | 3.45 pp | 8.63 pp | Pass |
| `peer-75` | 5 / 5 | 7.32% | 12.87% | 7.29 pp | 14.67 pp | Fail |

The producer sets the rate at every point, so the latency of a transaction is about the sum of both parts. The model gives `Work.selected` 20.8%, 41.7%, and 62.5%, and `Work.peer` 4.2%, 8.3%, and 12.5%. The model leaves out the queue handoff and any queue wait.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.987, 0.935 |
| `selected-50` | 0.987, 0.880 |
| `selected-75` | 0.998, 0.818 |
| `peer-25` | 1.010, 1.089 |
| `peer-50` | 0.997, 1.046 |
| `peer-75` | 0.991, 1.048 |

The median work ratio by run order in the process is order 0: 0.984, 1.000; order 1: 1.000, 1.000; order 2: 1.000, 1.000; order 3: 1.000, 1.048; order 4: 1.000, 1.000; order 5: 0.982, 0.935; order 6: 0.997, 0.928. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Queue handoff

The handoff of an item is the time from just before its enqueue to the return of the dequeue that takes it. The table gives the median over valid blocks of the mean handoff, in the profile experiment and in the real arm. The baseline row compares the profile baseline with the plain original arm, and it is the only row that compares equal measures. At a speedup above 0, a profiled handoff also includes the delay that the producer pays in `Enqueue`. At every point, the handoff also includes the time that an item waits in the queue.

| Point | Median mean handoff, profile | Median mean handoff, real arm |
| --- | ---: | ---: |
| baseline | 6.9 us | 110.3 us |
| `selected-25` | 4.8 us | 87.5 us |
| `selected-50` | 5.8 us | 67.8 us |
| `selected-75` | 43.0 us | 44.3 us |
| `peer-25` | 47.7 us | 110.2 us |
| `peer-50` | 34.1 us | 108.7 us |
| `peer-75` | 29.9 us | 95.8 us |

The second value is the signed difference between the dequeue return and the enqueue return of the same item, as a mean. It can be negative, because the consumer can take an item before the producer reads its time after `Enqueue`. The table gives the median over valid blocks of the baseline values.

| Arm | Mean enqueue time | Mean dequeue return minus enqueue return | Share of spin takes |
| --- | ---: | ---: | ---: |
| Profile baseline | 1.5 us | 4.5 us | 99.4% |
| Plain original | 33.6 us | 76.7 us | 96.4% |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | 4.47 pp | 8.05 pp | -4.64 pp | 1.17 pp | 3.45 pp | -5.55 pp |
| 2 | Yes | -0.04 pp | 0.01 pp | -5.02 pp | -1.80 pp | -8.63 pp | -7.29 pp |
| 3 | Yes | 2.38 pp | -0.32 pp | -2.32 pp | 3.71 pp | -2.42 pp | -6.74 pp |
| 4 | Yes | 7.18 pp | 2.35 pp | 5.95 pp | 8.03 pp | 2.55 pp | 14.67 pp |
| 5 | Yes | 0.92 pp | -1.83 pp | 4.53 pp | -1.37 pp | -6.55 pp | -12.32 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T124806158-79f85b15` | First spin transaction profile run | Fail. `peer-25` median 5.34 points and `peer-75` median 6.92 points. The three `selected` points pass. |
| `20261019T131601688-284620fc` | Handoff parts | Fail. Points peer-75 fail, or a run-level criterion fails. |

## Limits

A latency experiment counts each transaction that ends, from the first work start to the last finish. The consumer drains the queue after the stop, so each transaction that begins also ends.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetQueue` with one producer and one consumer, with a latency progress point. CLR safety outside this workload is not established.
