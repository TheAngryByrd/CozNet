# Target-local spin transaction profile

Evidence: `artifacts/target-local-spintransaction/20261019T175441808-251c8262`.

`TargetLocalSpinTransactionVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `spin-transaction-profile` process and seven `plain-spin-transaction` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms with a latency progress point. Worker 0 begins a transaction, runs `Work.selected 40`, and enqueues it in a `TargetQueue` of 4 items. Worker 1 dequeues it, spinning up to 1 ms before it blocks, runs `Work.peer 8`, and ends it. The prediction is `1 - (VL / V) / (VL0 / V0)`, where VL sums the virtual latency of the transactions, each its wall latency minus the global delay added during it and at least 0, and V counts the departures. The real gain is `1 - W / W0`, where W is the mean wall latency.
The criteria are in [the spin queue plan](../../../lode/plans/target-local-spin-queue.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-50, peer-25, peer-50 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 20.31% | 20.62% | 2.41 pp | 4.84 pp | Pass |
| `selected-50` | 5 / 5 | 45.30% | 41.34% | 4.56 pp | 25.47 pp | Fail |
| `selected-75` | 5 / 5 | 57.22% | 61.34% | 4.14 pp | 4.53 pp | Pass |
| `peer-25` | 5 / 5 | 9.53% | 1.60% | 9.22 pp | 11.10 pp | Fail |
| `peer-50` | 5 / 5 | 10.34% | 5.40% | 6.25 pp | 74.27 pp | Fail |
| `peer-75` | 5 / 5 | 11.54% | 12.84% | 2.67 pp | 6.02 pp | Pass |

The producer sets the rate at every point, so the latency of a transaction is about the sum of both parts. The model gives `Work.selected` 20.8%, 41.7%, and 62.5%, and `Work.peer` 4.2%, 8.3%, and 12.5%. The model leaves out the queue handoff and any queue wait.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 1.000, 0.964 |
| `selected-50` | 1.007, 0.907 |
| `selected-75` | 1.011, 0.797 |
| `peer-25` | 1.000, 1.127 |
| `peer-50` | 0.988, 1.106 |
| `peer-75` | 1.013, 1.182 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 1.004, 1.000; order 2: 1.000, 1.000; order 3: 1.000, 1.111; order 4: 0.999, 1.000; order 5: 1.007, 0.998; order 6: 1.021, 0.978. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Queue handoff

The handoff of an item is the time from just before its enqueue to the return of the dequeue that takes it. The table gives the median over valid blocks of the mean handoff, in the profile experiment and in the real arm. The baseline row compares the profile baseline with the plain original arm, and it is the only row that compares equal measures. At a speedup above 0, a profiled handoff also includes the delay that the producer pays in `Enqueue`. At every point, the handoff also includes the time that an item waits in the queue.

| Point | Median mean handoff, profile | Median mean handoff, real arm |
| --- | ---: | ---: |
| baseline | 7.9 us | 1.7 us |
| `selected-25` | 7.8 us | 2.0 us |
| `selected-50` | 7.7 us | 2.3 us |
| `selected-75` | 48.8 us | 7.0 us |
| `peer-25` | 24.9 us | 4.3 us |
| `peer-50` | 46.4 us | 3.8 us |
| `peer-75` | 62.6 us | 3.2 us |

The second value is the signed difference between the dequeue return and the enqueue return of the same item, as a mean. It can be negative, because the consumer can take an item before the producer reads its time after `Enqueue`. The table gives the median over valid blocks of the baseline values.

| Arm | Mean enqueue time | Mean dequeue return minus enqueue return | Share of spin takes |
| --- | ---: | ---: | ---: |
| Profile baseline | 2.6 us | 5.3 us | 99.4% |
| Plain original | 0.8 us | 0.9 us | 100.0% |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | 4.62 pp | -0.28 pp | -4.29 pp | 11.01 pp | 3.70 pp | 6.02 pp |
| 2 | Yes | 0.81 pp | 4.88 pp | 4.53 pp | 7.13 pp | -0.16 pp | -1.29 pp |
| 3 | Yes | -0.37 pp | 4.56 pp | 0.21 pp | 5.96 pp | 6.25 pp | -1.36 pp |
| 4 | Yes | -4.84 pp | 25.47 pp | -4.14 pp | 11.10 pp | 15.47 pp | 2.67 pp |
| 5 | Yes | -2.41 pp | -2.56 pp | -3.40 pp | 9.22 pp | 74.27 pp | -3.88 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T124806158-79f85b15` | First spin transaction profile run | Fail. `peer-25` median 5.34 points and `peer-75` median 6.92 points. The three `selected` points pass. |
| `20261019T131601688-284620fc` | Handoff parts | Fail. `peer-75` median 7.29 points. The other five points pass. |
| `20261019T150209127-dbb260bc` | Worker pinning | Fail. `peer-25` median 9.56 points and `peer-50` median 5.06 points. |
| `20261019T175441808-251c8262` | Matched plain warm-up | Fail. Points selected-50, peer-25, peer-50 fail, or a run-level criterion fails. |

## Limits

A latency experiment counts each transaction that ends, from the first work start to the last finish. The consumer drains the queue after the stop, so each transaction that begins also ends.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetQueue` with one producer and one consumer, with a latency progress point. CLR safety outside this workload is not established.
