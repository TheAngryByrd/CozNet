# Target-local pipeline profile

Evidence: `artifacts/target-local-pipeline/20261019T060421721-4a882c0a`.

`TargetLocalPipelineVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `pipeline-profile` process and seven `plain-pipeline` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. A producer repeats `Work.selected 40` and puts one item into a `TargetQueue` of 4 items. A consumer takes each item, runs `Work.peer 20`, and visits the progress point.
The criteria are in [the queue plan](../../../lode/plans/target-local-queue.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run passes.**

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 25.27% | 24.69% | 1.63 pp | 3.82 pp | Pass |
| `selected-50` | 5 / 5 | 50.47% | 50.44% | 0.78 pp | 5.69 pp | Pass |
| `selected-75` | 5 / 5 | 49.28% | 49.18% | 0.58 pp | 4.10 pp | Pass |
| `peer-25` | 5 / 5 | 0.64% | 0.62% | 2.17 pp | 5.16 pp | Pass |
| `peer-50` | 5 / 5 | -0.25% | 4.63% | 4.92 pp | 9.44 pp | Pass |
| `peer-75` | 5 / 5 | -0.03% | 0.90% | 1.09 pp | 3.69 pp | Pass |

The producer is the bottleneck, so the model gives `Work.selected` 25%, 50%, and 50%, and `Work.peer` 0% at each speedup. Above 50%, the consumer sets the rate.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.990, 0.860 |
| `selected-50` | 0.974, 0.512 |
| `selected-75` | 1.229, 0.507 |
| `peer-25` | 0.993, 1.121 |
| `peer-50` | 1.001, 1.250 |
| `peer-75` | 1.002, 1.379 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 1.013, 1.000; order 2: 1.003, 1.000; order 3: 0.999, 1.123; order 4: 0.990, 1.000; order 5: 0.994, 0.862; order 6: 1.002, 0.883. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -1.04 pp | -0.78 pp | 0.24 pp | -4.65 pp | -7.26 pp | -0.57 pp |
| 2 | Yes | 2.93 pp | 0.92 pp | 0.11 pp | 5.16 pp | 4.03 pp | -0.43 pp |
| 3 | Yes | -1.63 pp | -0.72 pp | -4.10 pp | -0.08 pp | 1.98 pp | -2.57 pp |
| 4 | Yes | 3.82 pp | 0.08 pp | 0.58 pp | -1.94 pp | -9.44 pp | 1.09 pp |
| 5 | Yes | -1.26 pp | -5.69 pp | 1.22 pp | -2.17 pp | -4.92 pp | -3.69 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T040306290-64463c6b` | First pipeline profile run | Fail. `peer-50` median 6.76 points. Across the `peer` points, 13 of 15 block predictions are below their actual gains. |
| `20261019T043536804-514995d6` | Per-thread experiment diagnostics | Fail. `peer-75` has one block of 14.82 points. The `peer-50` prediction tracks the producer work ratio. |
| `20261019T050900839-0a049f8a` | Profile warm-up experiment | Fail. `peer-50` median 6.01 points. The order-0 producer work ratio stays 1.041 with the warm-up. |
| `20261019T060421721-4a882c0a` | Rotated experiment order and warm-up gate | Pass |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetQueue` with one producer and one consumer. CLR safety outside this workload is not established.
