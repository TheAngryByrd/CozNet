# Target-local pipeline profile

Evidence: `artifacts/target-local-pipeline/20261019T050900839-0a049f8a`.

`TargetLocalPipelineVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `pipeline-profile` process and seven `plain-pipeline` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. A producer repeats `Work.selected 40` and puts one item into a `TargetQueue` of 4 items. A consumer takes each item, runs `Work.peer 20`, and visits the progress point.
The criteria are in [the queue plan](../../../lode/plans/target-local-queue.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points peer-50 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 24.77% | 23.45% | 1.15 pp | 3.44 pp | Pass |
| `selected-50` | 5 / 5 | 49.65% | 50.41% | 1.23 pp | 2.25 pp | Pass |
| `selected-75` | 5 / 5 | 49.45% | 48.53% | 1.32 pp | 2.50 pp | Pass |
| `peer-25` | 5 / 5 | -0.12% | -3.89% | 3.68 pp | 5.40 pp | Pass |
| `peer-50` | 5 / 5 | -4.17% | -1.95% | 6.01 pp | 8.99 pp | Fail |
| `peer-75` | 5 / 5 | -1.29% | -2.06% | 3.56 pp | 5.35 pp | Pass |

The producer is the bottleneck, so the model gives `Work.selected` 25%, 50%, and 50%, and `Work.peer` 0% at each speedup. Above 50%, the consumer sets the rate.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.992, 0.857 |
| `selected-50` | 0.992, 0.520 |
| `selected-75` | 1.230, 0.505 |
| `peer-25` | 1.001, 1.122 |
| `peer-50` | 1.041, 1.288 |
| `peer-75` | 1.011, 1.371 |

The median work ratio by run order in the process is order 0: 1.041, 1.295; order 1: 1.000, 0.544; order 2: 0.999, 1.125; order 3: 0.989, 1.000; order 4: 0.992, 0.520; order 5: 1.000, 0.886; order 6: 1.043, 1.000. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | 3.44 pp | -1.23 pp | 1.59 pp | 3.90 pp | -3.15 pp | -5.01 pp |
| 2 | Yes | -0.12 pp | 2.25 pp | -2.50 pp | 2.59 pp | -3.44 pp | 2.03 pp |
| 3 | Yes | 1.22 pp | -1.86 pp | -1.10 pp | -0.63 pp | -6.01 pp | 0.25 pp |
| 4 | Yes | -1.14 pp | -0.21 pp | 0.41 pp | 3.68 pp | 8.99 pp | 5.35 pp |
| 5 | Yes | 1.15 pp | -0.57 pp | 1.32 pp | 5.40 pp | 8.42 pp | 3.56 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T040306290-64463c6b` | First pipeline profile run | Fail. `peer-50` median 6.76 points. Across the `peer` points, 13 of 15 block predictions are below their actual gains. |
| `20261019T043536804-514995d6` | Per-thread experiment diagnostics | Fail. `peer-75` has one block of 14.82 points. The `peer-50` prediction tracks the producer work ratio. |
| `20261019T050900839-0a049f8a` | Profile warm-up experiment | Fail. Points peer-50 fail, or a run-level criterion fails. |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetQueue` with one producer and one consumer. CLR safety outside this workload is not established.
