# Target-local pipeline profile

Evidence: `artifacts/target-local-pipeline/20261019T040306290-64463c6b`.

`TargetLocalPipelineVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `pipeline-profile` process and seven `plain-pipeline` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. A producer repeats `Work.selected 40` and puts one item into a `TargetQueue` of 4 items. A consumer takes each item, runs `Work.peer 20`, and visits the progress point.
The criteria are in [the queue plan](../../../lode/plans/target-local-queue.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points peer-50 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 23.84% | 26.50% | 3.04 pp | 4.78 pp | Pass |
| `selected-50` | 5 / 5 | 49.72% | 49.95% | 1.28 pp | 2.31 pp | Pass |
| `selected-75` | 5 / 5 | 48.62% | 49.71% | 1.28 pp | 2.15 pp | Pass |
| `peer-25` | 5 / 5 | -0.70% | 0.97% | 2.98 pp | 6.96 pp | Pass |
| `peer-50` | 5 / 5 | -3.91% | 2.85% | 6.76 pp | 7.87 pp | Fail |
| `peer-75` | 5 / 5 | -2.53% | 1.22% | 2.82 pp | 9.71 pp | Pass |

The producer is the bottleneck, so the model gives `Work.selected` 25%, 50%, and 50%, and `Work.peer` 0% at each speedup. Above 50%, the consumer sets the rate.

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | 1.46 pp | 0.79 pp | -0.50 pp | 6.96 pp | -5.40 pp | -2.82 pp |
| 2 | Yes | -4.39 pp | -2.31 pp | -1.60 pp | -1.01 pp | -6.47 pp | -1.73 pp |
| 3 | Yes | -2.65 pp | 1.28 pp | 1.28 pp | 2.98 pp | -6.76 pp | -0.23 pp |
| 4 | Yes | -3.04 pp | -2.03 pp | -2.15 pp | -5.53 pp | -7.24 pp | -9.71 pp |
| 5 | Yes | -4.78 pp | -0.30 pp | 0.55 pp | -0.19 pp | -7.87 pp | -3.18 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T040306290-64463c6b` | First pipeline profile run | Fail. Points peer-50 fail, or a run-level criterion fails. |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetQueue` with one producer and one consumer. CLR safety outside this workload is not established.
