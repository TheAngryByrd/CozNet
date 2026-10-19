# Target-local spin transaction profile

Evidence: `artifacts/target-local-spintransaction/20261019T183747700-877bf50a`.

`TargetLocalSpinTransactionVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `spin-transaction-profile` process and seven `plain-spin-transaction` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms with a latency progress point. Worker 0 begins a transaction, runs `Work.selected 40`, and enqueues it in a `TargetQueue` of 4 items. Worker 1 dequeues it, spinning up to 1 ms before it blocks, runs `Work.peer 8`, and ends it. The prediction is `1 - (VL / V) / (VL0 / V0)`, where VL sums the virtual latency of the transactions, each its wall latency minus the global delay added during it and at least 0, and V counts the departures. The real gain is `1 - W / W0`, where W is the mean wall latency.
The criteria are in [the spin queue plan](../../../lode/plans/target-local-spin-queue.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 19.53% | 21.44% | 2.73 pp | 3.55 pp | Pass |
| `selected-50` | 5 / 5 | 40.62% | 42.62% | 1.00 pp | 2.84 pp | Pass |
| `selected-75` | 5 / 5 | 55.43% | 61.84% | 5.44 pp | 9.44 pp | Fail |
| `peer-25` | 5 / 5 | 10.08% | 5.67% | 4.99 pp | 6.94 pp | Pass |
| `peer-50` | 5 / 5 | 9.10% | 9.97% | 1.45 pp | 2.69 pp | Pass |
| `peer-75` | 5 / 5 | 13.11% | 14.60% | 1.95 pp | 5.12 pp | Pass |

The producer sets the rate at every point, so the latency of a transaction is about the sum of both parts. The model gives `Work.selected` 20.8%, 41.7%, and 62.5%, and `Work.peer` 4.2%, 8.3%, and 12.5%. The model leaves out the queue handoff and any queue wait.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 1.003, 0.960 |
| `selected-50` | 1.009, 0.915 |
| `selected-75` | 0.998, 0.790 |
| `peer-25` | 0.995, 1.135 |
| `peer-50` | 1.001, 1.122 |
| `peer-75` | 0.996, 1.153 |

The median work ratio by run order in the process is order 0: 1.005, 1.000; order 1: 1.000, 1.000; order 2: 1.000, 1.000; order 3: 0.998, 1.103; order 4: 1.003, 1.000; order 5: 0.987, 0.930; order 6: 1.009, 0.968. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Queue handoff

The handoff of an item is the time from just before its enqueue to the return of the dequeue that takes it. The table gives the median over valid blocks of the mean handoff, in the profile experiment and in the real arm. The baseline row compares the profile baseline with the plain original arm, and it is the only row that compares equal measures. At a speedup above 0, a profiled handoff also includes the delay that the producer pays in `Enqueue`. At every point, the handoff also includes the time that an item waits in the queue.

| Point | Median mean handoff, profile | Median mean handoff, real arm |
| --- | ---: | ---: |
| baseline | 7.0 us | 2.3 us |
| `selected-25` | 7.6 us | 1.5 us |
| `selected-50` | 6.4 us | 1.5 us |
| `selected-75` | 55.5 us | 2.2 us |
| `peer-25` | 82.9 us | 1.9 us |
| `peer-50` | 47.1 us | 1.9 us |
| `peer-75` | 57.2 us | 1.8 us |

The second value is the signed difference between the dequeue return and the enqueue return of the same item, as a mean. It can be negative, because the consumer can take an item before the producer reads its time after `Enqueue`. The table gives the median over valid blocks of the baseline values.

| Arm | Mean enqueue time | Mean dequeue return minus enqueue return | Share of spin takes |
| --- | ---: | ---: | ---: |
| Profile baseline | 2.5 us | 4.4 us | 100.0% |
| Plain original | 1.0 us | 1.3 us | 100.0% |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -0.17 pp | 0.46 pp | -2.96 pp | 6.94 pp | 1.45 pp | 1.95 pp |
| 2 | Yes | -2.73 pp | -2.66 pp | -3.53 pp | 2.57 pp | -2.63 pp | -5.12 pp |
| 3 | Yes | -3.55 pp | -1.00 pp | -9.44 pp | 4.99 pp | -0.64 pp | 2.09 pp |
| 4 | Yes | 0.68 pp | -0.58 pp | -5.44 pp | 6.81 pp | 0.78 pp | -1.72 pp |
| 5 | Yes | -3.16 pp | -2.84 pp | -6.34 pp | 4.68 pp | -2.69 pp | -1.47 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T124806158-79f85b15` | First spin transaction profile run | Fail. `peer-25` median 5.34 points and `peer-75` median 6.92 points. The three `selected` points pass. |
| `20261019T131601688-284620fc` | Handoff parts | Fail. `peer-75` median 7.29 points. The other five points pass. |
| `20261019T150209127-dbb260bc` | Worker pinning | Fail. `peer-25` median 9.56 points and `peer-50` median 5.06 points. |
| `20261019T175441808-251c8262` | Matched plain warm-up | Fail. `peer-25` median 9.22 points, `selected-50` one block of 25.47 points, and `peer-50` median 6.25 points. The other three points pass. |
| `20261019T183747700-877bf50a` | Payment before each progress stamp | Fail. Points selected-75 fail, or a run-level criterion fails. |

## Limits

A latency experiment counts each transaction that ends, from the first work start to the last finish. The consumer drains the queue after the stop, so each transaction that begins also ends.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetQueue` with one producer and one consumer, with a latency progress point. CLR safety outside this workload is not established.
