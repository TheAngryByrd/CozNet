# Target-local pipeline profile

Evidence: `artifacts/target-local-pipeline/20261019T110512150-9b6ceb9a`.

`TargetLocalPipelineVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `pipeline-profile` process and seven `plain-pipeline` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. A producer repeats `Work.selected 40` and puts one item into a `TargetQueue` of 4 items. A consumer takes each item, runs `Work.peer 20`, and visits the progress point.
The criteria are in [the queue plan](../../../lode/plans/target-local-queue.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run passes.**

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 23.83% | 24.81% | 0.99 pp | 2.74 pp | Pass |
| `selected-50` | 5 / 5 | 49.20% | 49.98% | 0.54 pp | 1.46 pp | Pass |
| `selected-75` | 5 / 5 | 48.84% | 49.08% | 1.45 pp | 3.57 pp | Pass |
| `peer-25` | 5 / 5 | 0.98% | 1.66% | 1.07 pp | 6.77 pp | Pass |
| `peer-50` | 5 / 5 | 0.21% | -0.11% | 2.86 pp | 3.51 pp | Pass |
| `peer-75` | 5 / 5 | -0.09% | 0.62% | 2.76 pp | 5.71 pp | Pass |

The producer is the bottleneck, so the model gives `Work.selected` 25%, 50%, and 50%, and `Work.peer` 0% at each speedup. Above 50%, the consumer sets the rate.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 1.002, 0.871 |
| `selected-50` | 0.994, 0.515 |
| `selected-75` | 1.236, 0.511 |
| `peer-25` | 0.990, 1.104 |
| `peer-50` | 0.997, 1.239 |
| `peer-75` | 1.000, 1.387 |

The median work ratio by run order in the process is order 0: 1.034, 1.000; order 1: 1.000, 1.000; order 2: 1.007, 1.000; order 3: 1.000, 1.104; order 4: 0.997, 1.000; order 5: 1.002, 0.871; order 6: 0.997, 0.869. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -2.74 pp | -0.61 pp | -0.30 pp | 1.01 pp | 3.51 pp | -2.05 pp |
| 2 | Yes | -2.32 pp | -1.46 pp | -3.57 pp | -4.47 pp | -2.86 pp | -2.76 pp |
| 3 | Yes | -0.98 pp | -0.54 pp | -0.70 pp | -0.67 pp | 0.16 pp | -0.36 pp |
| 4 | Yes | 0.99 pp | -0.28 pp | 2.46 pp | 6.77 pp | 0.63 pp | -5.71 pp |
| 5 | Yes | 0.49 pp | 0.47 pp | 1.45 pp | -1.07 pp | -3.36 pp | -3.03 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T040306290-64463c6b` | First pipeline profile run | Fail. `peer-50` median 6.76 points. Across the `peer` points, 13 of 15 block predictions are below their actual gains. |
| `20261019T043536804-514995d6` | Per-thread experiment diagnostics | Fail. `peer-75` has one block of 14.82 points. The `peer-50` prediction tracks the producer work ratio. |
| `20261019T050900839-0a049f8a` | Profile warm-up experiment | Fail. `peer-50` median 6.01 points. The order-0 producer work ratio stays 1.041 with the warm-up. |
| `20261019T060421721-4a882c0a` | Rotated experiment order and warm-up gate | Pass. `peer-50` median 4.92 points. |
| `20261019T062807524-d72126fc` | TargetLock and lock profile | Pass |
| `20261019T070437097-c99cf6c4` | Lock handoff counts | Fail. `peer-50` has one block of 10.02 points. Its median difference is 2.70 points. |
| `20261019T074639406-cf96acce` | Spin before a lock wait | Pass |
| `20261019T082629301-c4292b43` | TargetSemaphore and semaphore profile | Fail. `peer-25` has one block of 10.11 points. |
| `20261019T090700489-b82b1791` | Failure and GC records | Pass |
| `20261019T094141941-f8894fe3` | Failure context | Fail. Block 5 is invalid from a debt failure after a wake with a skip of 0, and `peer-50` has one block of -11.12 points. |
| `20261019T110512150-9b6ceb9a` | Latency progress points | Pass |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetQueue` with one producer and one consumer. CLR safety outside this workload is not established.
