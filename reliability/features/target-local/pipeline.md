# Target-local pipeline profile

Evidence: `artifacts/target-local-pipeline/20261019T062807524-d72126fc`.

`TargetLocalPipelineVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `pipeline-profile` process and seven `plain-pipeline` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. A producer repeats `Work.selected 40` and puts one item into a `TargetQueue` of 4 items. A consumer takes each item, runs `Work.peer 20`, and visits the progress point.
The criteria are in [the queue plan](../../../lode/plans/target-local-queue.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run passes.**

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 25.49% | 24.18% | 2.50 pp | 2.97 pp | Pass |
| `selected-50` | 5 / 5 | 49.78% | 50.69% | 0.71 pp | 1.18 pp | Pass |
| `selected-75` | 5 / 5 | 50.30% | 47.78% | 2.56 pp | 3.82 pp | Pass |
| `peer-25` | 5 / 5 | -0.12% | 0.42% | 3.18 pp | 5.07 pp | Pass |
| `peer-50` | 5 / 5 | -0.30% | 2.03% | 2.03 pp | 5.05 pp | Pass |
| `peer-75` | 5 / 5 | -1.20% | 1.72% | 2.92 pp | 5.20 pp | Pass |

The producer is the bottleneck, so the model gives `Work.selected` 25%, 50%, and 50%, and `Work.peer` 0% at each speedup. Above 50%, the consumer sets the rate.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.988, 0.866 |
| `selected-50` | 0.977, 0.518 |
| `selected-75` | 1.202, 0.497 |
| `peer-25` | 1.000, 1.120 |
| `peer-50` | 1.002, 1.247 |
| `peer-75` | 1.010, 1.379 |

The median work ratio by run order in the process is order 0: 1.017, 1.000; order 1: 1.000, 1.000; order 2: 1.000, 1.000; order 3: 1.000, 1.090; order 4: 0.995, 1.000; order 5: 1.015, 0.883; order 6: 1.010, 0.834. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | 2.50 pp | -0.90 pp | 2.56 pp | -3.18 pp | -5.05 pp | -5.20 pp |
| 2 | Yes | 2.52 pp | 0.42 pp | 3.82 pp | 2.68 pp | 0.75 pp | 1.18 pp |
| 3 | Yes | 2.43 pp | -0.71 pp | 2.42 pp | 4.36 pp | -2.57 pp | -3.08 pp |
| 4 | Yes | -2.97 pp | 0.03 pp | -2.82 pp | -1.55 pp | -2.03 pp | -1.52 pp |
| 5 | Yes | -0.26 pp | -1.18 pp | -0.99 pp | 5.07 pp | -1.72 pp | -2.92 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T040306290-64463c6b` | First pipeline profile run | Fail. `peer-50` median 6.76 points. Across the `peer` points, 13 of 15 block predictions are below their actual gains. |
| `20261019T043536804-514995d6` | Per-thread experiment diagnostics | Fail. `peer-75` has one block of 14.82 points. The `peer-50` prediction tracks the producer work ratio. |
| `20261019T050900839-0a049f8a` | Profile warm-up experiment | Fail. `peer-50` median 6.01 points. The order-0 producer work ratio stays 1.041 with the warm-up. |
| `20261019T060421721-4a882c0a` | Rotated experiment order and warm-up gate | Pass. `peer-50` median 4.92 points. |
| `20261019T062807524-d72126fc` | TargetLock and lock profile | Pass |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetQueue` with one producer and one consumer. CLR safety outside this workload is not established.
