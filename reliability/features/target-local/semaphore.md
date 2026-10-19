# Target-local semaphore profile

Evidence: `artifacts/target-local-semaphore/20261019T093837289-67cf3f9e`.

`TargetLocalSemaphoreVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `semaphore-profile` process and seven `plain-semaphore` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. Two workers take turns through two `TargetSemaphore` objects. Worker 0 runs `Work.selected 24` and releases worker 1, which runs `Work.peer 12`, visits the progress point, and releases worker 0.
The criteria are in [the semaphore plan](../../../lode/plans/target-local-semaphore.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-50, selected-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 15.27% | 17.84% | 3.83 pp | 9.13 pp | Pass |
| `selected-50` | 5 / 5 | 29.10% | 34.19% | 5.09 pp | 7.12 pp | Fail |
| `selected-75` | 5 / 5 | 44.57% | 48.94% | 5.41 pp | 5.86 pp | Fail |
| `peer-25` | 5 / 5 | 6.77% | 9.71% | 2.46 pp | 6.14 pp | Pass |
| `peer-50` | 5 / 5 | 14.44% | 16.46% | 3.36 pp | 7.31 pp | Pass |
| `peer-75` | 5 / 5 | 21.87% | 22.94% | 1.31 pp | 4.57 pp | Pass |

No work runs in parallel, so the model gives `Work.selected` 16.7%, 33.3%, and 50%, and `Work.peer` 8.3%, 16.7%, and 25%. The model leaves out the two handoffs of each visit.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 1.003, 1.003 |
| `selected-50` | 1.006, 1.006 |
| `selected-75` | 0.997, 0.996 |
| `peer-25` | 1.008, 1.008 |
| `peer-50` | 1.009, 1.009 |
| `peer-75` | 1.002, 1.008 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 1.002, 1.002; order 2: 1.000, 1.008; order 3: 1.009, 1.009; order 4: 0.997, 0.997; order 5: 1.004, 1.009; order 6: 1.006, 1.006. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | 3.83 pp | -6.15 pp | -4.37 pp | -2.15 pp | 5.98 pp | -0.89 pp |
| 2 | Yes | 1.02 pp | -2.88 pp | -5.45 pp | 2.46 pp | 3.36 pp | -1.07 pp |
| 3 | Yes | -9.03 pp | -7.12 pp | -5.41 pp | 0.83 pp | -2.02 pp | -4.57 pp |
| 4 | Yes | -9.13 pp | -4.31 pp | -5.86 pp | -6.14 pp | -7.31 pp | -1.31 pp |
| 5 | Yes | -1.32 pp | -5.09 pp | -2.10 pp | -5.05 pp | -1.67 pp | 2.06 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T082323819-903a6cef` | First semaphore profile run | Fail. `selected-75` has one block of -10.61 points, and `peer-75` median 5.24 points. |
| `20261019T090345985-b2474637` | Failure and GC records | Pass |
| `20261019T093837289-67cf3f9e` | Failure context | Fail. Points selected-50, selected-75 fail, or a run-level criterion fails. |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetSemaphore` with one releasing thread and one waiting thread for each semaphore. CLR safety outside this workload is not established.
