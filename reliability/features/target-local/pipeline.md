# Target-local pipeline profile

Evidence: `artifacts/target-local-pipeline/20261019T043536804-514995d6`.

`TargetLocalPipelineVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `pipeline-profile` process and seven `plain-pipeline` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. A producer repeats `Work.selected 40` and puts one item into a `TargetQueue` of 4 items. A consumer takes each item, runs `Work.peer 20`, and visits the progress point.
The criteria are in [the queue plan](../../../lode/plans/target-local-queue.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points peer-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 24.71% | 24.80% | 1.65 pp | 2.60 pp | Pass |
| `selected-50` | 5 / 5 | 49.61% | 49.75% | 0.61 pp | 1.28 pp | Pass |
| `selected-75` | 5 / 5 | 49.58% | 47.70% | 2.61 pp | 4.46 pp | Pass |
| `peer-25` | 5 / 5 | 0.25% | -0.41% | 2.64 pp | 4.45 pp | Pass |
| `peer-50` | 5 / 5 | -4.03% | 0.43% | 2.99 pp | 5.35 pp | Pass |
| `peer-75` | 5 / 5 | 1.64% | -0.05% | 4.90 pp | 14.82 pp | Fail |

The producer is the bottleneck, so the model gives `Work.selected` 25%, 50%, and 50%, and `Work.peer` 0% at each speedup. Above 50%, the consumer sets the rate.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.991, 0.870 |
| `selected-50` | 0.977, 0.517 |
| `selected-75` | 1.196, 0.504 |
| `peer-25` | 0.997, 1.119 |
| `peer-50` | 1.043, 1.302 |
| `peer-75` | 0.982, 1.327 |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | 1.65 pp | 1.01 pp | 2.11 pp | 4.45 pp | -5.35 pp | 3.78 pp |
| 2 | Yes | 2.60 pp | 0.25 pp | 4.05 pp | 1.60 pp | -2.96 pp | 3.14 pp |
| 3 | Yes | -0.83 pp | -1.28 pp | 4.46 pp | -2.64 pp | -4.35 pp | -5.68 pp |
| 4 | Yes | -2.47 pp | -0.61 pp | 1.31 pp | -3.88 pp | -2.99 pp | -14.82 pp |
| 5 | Yes | 1.65 pp | 0.04 pp | 2.61 pp | 1.86 pp | 0.06 pp | 4.90 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T040306290-64463c6b` | First pipeline profile run | Fail. `peer-50` median 6.76 points. Across the `peer` points, 13 of 15 block predictions are below their actual gains. |
| `20261019T043536804-514995d6` | Per-thread experiment diagnostics | Fail. Points peer-75 fail, or a run-level criterion fails. |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetQueue` with one producer and one consumer. CLR safety outside this workload is not established.
