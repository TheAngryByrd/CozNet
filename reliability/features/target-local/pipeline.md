# Target-local pipeline profile

Evidence: `artifacts/target-local-pipeline/20261019T070437097-c99cf6c4`.

`TargetLocalPipelineVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `pipeline-profile` process and seven `plain-pipeline` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. A producer repeats `Work.selected 40` and puts one item into a `TargetQueue` of 4 items. A consumer takes each item, runs `Work.peer 20`, and visits the progress point.
The criteria are in [the queue plan](../../../lode/plans/target-local-queue.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points peer-50 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 25.48% | 24.06% | 0.91 pp | 7.10 pp | Pass |
| `selected-50` | 5 / 5 | 48.97% | 49.71% | 1.54 pp | 3.41 pp | Pass |
| `selected-75` | 5 / 5 | 50.33% | 47.64% | 2.34 pp | 3.53 pp | Pass |
| `peer-25` | 5 / 5 | -0.44% | -0.98% | 3.27 pp | 9.96 pp | Pass |
| `peer-50` | 5 / 5 | -0.57% | 0.28% | 2.70 pp | 10.02 pp | Fail |
| `peer-75` | 5 / 5 | -0.08% | 1.23% | 2.99 pp | 5.85 pp | Pass |

The producer is the bottleneck, so the model gives `Work.selected` 25%, 50%, and 50%, and `Work.peer` 0% at each speedup. Above 50%, the consumer sets the rate.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.986, 0.864 |
| `selected-50` | 1.003, 0.525 |
| `selected-75` | 1.211, 0.497 |
| `peer-25` | 1.004, 1.123 |
| `peer-50` | 1.004, 1.261 |
| `peer-75` | 1.004, 1.379 |

The median work ratio by run order in the process is order 0: 1.002, 1.000; order 1: 1.000, 1.000; order 2: 1.005, 1.000; order 3: 1.004, 1.123; order 4: 0.986, 1.000; order 5: 1.035, 0.851; order 6: 1.003, 0.864. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | 0.54 pp | -1.54 pp | -2.34 pp | -4.35 pp | -2.70 pp | -4.14 pp |
| 2 | Yes | 1.99 pp | -2.86 pp | 0.43 pp | -0.14 pp | -0.17 pp | -2.99 pp |
| 3 | Yes | 0.80 pp | 1.05 pp | 3.53 pp | 0.54 pp | -0.55 pp | -1.45 pp |
| 4 | Yes | 7.10 pp | 3.41 pp | 3.16 pp | 9.96 pp | 10.02 pp | 5.85 pp |
| 5 | Yes | -0.91 pp | -1.39 pp | 1.24 pp | 3.27 pp | -4.19 pp | -1.37 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T040306290-64463c6b` | First pipeline profile run | Fail. `peer-50` median 6.76 points. Across the `peer` points, 13 of 15 block predictions are below their actual gains. |
| `20261019T043536804-514995d6` | Per-thread experiment diagnostics | Fail. `peer-75` has one block of 14.82 points. The `peer-50` prediction tracks the producer work ratio. |
| `20261019T050900839-0a049f8a` | Profile warm-up experiment | Fail. `peer-50` median 6.01 points. The order-0 producer work ratio stays 1.041 with the warm-up. |
| `20261019T060421721-4a882c0a` | Rotated experiment order and warm-up gate | Pass. `peer-50` median 4.92 points. |
| `20261019T062807524-d72126fc` | TargetLock and lock profile | Pass |
| `20261019T070437097-c99cf6c4` | Lock handoff counts | Fail. Points peer-50 fail, or a run-level criterion fails. |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetQueue` with one producer and one consumer. CLR safety outside this workload is not established.
