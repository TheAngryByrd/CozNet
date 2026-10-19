# Target-local semaphore profile

Evidence: `artifacts/target-local-semaphore/20261019T082323819-903a6cef`.

`TargetLocalSemaphoreVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `semaphore-profile` process and seven `plain-semaphore` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. Two workers take turns through two `TargetSemaphore` objects. Worker 0 runs `Work.selected 24` and releases worker 1, which runs `Work.peer 12`, visits the progress point, and releases worker 0.
The criteria are in [the semaphore plan](../../../lode/plans/target-local-semaphore.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-75, peer-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 14.99% | 16.62% | 1.99 pp | 3.51 pp | Pass |
| `selected-50` | 5 / 5 | 30.07% | 32.62% | 2.55 pp | 5.21 pp | Pass |
| `selected-75` | 5 / 5 | 45.65% | 48.39% | 1.58 pp | 10.61 pp | Fail |
| `peer-25` | 5 / 5 | 5.27% | 7.37% | 2.21 pp | 3.27 pp | Pass |
| `peer-50` | 5 / 5 | 14.04% | 14.58% | 1.87 pp | 3.72 pp | Pass |
| `peer-75` | 5 / 5 | 22.60% | 21.53% | 5.24 pp | 7.06 pp | Fail |

No work runs in parallel, so the model gives `Work.selected` 16.7%, 33.3%, and 50%, and `Work.peer` 8.3%, 16.7%, and 25%. The model leaves out the two handoffs of each visit.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.992, 0.989 |
| `selected-50` | 0.998, 0.998 |
| `selected-75` | 0.976, 0.976 |
| `peer-25` | 1.020, 1.021 |
| `peer-50` | 1.007, 1.006 |
| `peer-75` | 1.005, 1.015 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 0.994, 0.994; order 2: 1.013, 1.013; order 3: 1.000, 1.015; order 4: 1.000, 1.000; order 5: 0.992, 0.990; order 6: 1.028, 1.029. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | 0.93 pp | -1.16 pp | -4.94 pp | 3.27 pp | 3.72 pp | 5.24 pp |
| 2 | Yes | -3.12 pp | -2.55 pp | -0.59 pp | -2.21 pp | -1.03 pp | -7.06 pp |
| 3 | Yes | 3.51 pp | 1.93 pp | -0.79 pp | 0.38 pp | 1.87 pp | 6.85 pp |
| 4 | Yes | -1.99 pp | -4.50 pp | -10.61 pp | -2.52 pp | -2.70 pp | -2.04 pp |
| 5 | Yes | -1.68 pp | -5.21 pp | -1.58 pp | -1.94 pp | 0.52 pp | -4.62 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T082323819-903a6cef` | TargetSemaphore and semaphore profile | Fail. Points selected-75, peer-75 fail, or a run-level criterion fails. |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetSemaphore` with one releasing thread and one waiting thread for each semaphore. CLR safety outside this workload is not established.
