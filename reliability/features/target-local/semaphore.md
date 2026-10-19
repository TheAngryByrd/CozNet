# Target-local semaphore profile

Evidence: `artifacts/target-local-semaphore/20261019T110212880-744018ed`.

`TargetLocalSemaphoreVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `semaphore-profile` process and seven `plain-semaphore` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. Two workers take turns through two `TargetSemaphore` objects. Worker 0 runs `Work.selected 24` and releases worker 1, which runs `Work.peer 12`, visits the progress point, and releases worker 0.
The criteria are in [the semaphore plan](../../../lode/plans/target-local-semaphore.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-25, selected-50, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 15.36% | 15.84% | 1.53 pp | 11.10 pp | Fail |
| `selected-50` | 5 / 5 | 30.23% | 32.11% | 3.80 pp | 26.19 pp | Fail |
| `selected-75` | 5 / 5 | 44.04% | 46.90% | 3.21 pp | 9.12 pp | Pass |
| `peer-25` | 5 / 5 | 5.87% | 7.69% | 2.40 pp | 10.92 pp | Fail |
| `peer-50` | 5 / 5 | 15.25% | 14.93% | 1.82 pp | 33.29 pp | Fail |
| `peer-75` | 5 / 5 | 18.85% | 22.28% | 3.95 pp | 11.22 pp | Fail |

No work runs in parallel, so the model gives `Work.selected` 16.7%, 33.3%, and 50%, and `Work.peer` 8.3%, 16.7%, and 25%. The model leaves out the two handoffs of each visit.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.995, 0.995 |
| `selected-50` | 0.978, 0.978 |
| `selected-75` | 0.994, 0.993 |
| `peer-25` | 1.020, 1.020 |
| `peer-50` | 0.993, 0.993 |
| `peer-75` | 1.021, 1.021 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 1.021, 1.022; order 2: 1.025, 1.025; order 3: 1.000, 1.000; order 4: 0.995, 0.991; order 5: 0.994, 0.993; order 6: 0.987, 0.987. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -0.25 pp | 0.97 pp | -6.52 pp | -0.56 pp | -1.82 pp | 3.95 pp |
| 2 | Yes | 4.12 pp | -4.20 pp | -3.21 pp | 3.94 pp | 1.64 pp | 2.95 pp |
| 3 | Yes | -11.10 pp | -26.19 pp | -9.12 pp | -10.92 pp | -33.29 pp | -11.22 pp |
| 4 | Yes | 0.24 pp | 3.80 pp | -0.52 pp | -2.40 pp | 19.36 pp | -0.19 pp |
| 5 | Yes | -1.53 pp | -1.53 pp | -2.85 pp | -1.73 pp | -0.82 pp | -6.87 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T082323819-903a6cef` | First semaphore profile run | Fail. `selected-75` has one block of -10.61 points, and `peer-75` median 5.24 points. |
| `20261019T090345985-b2474637` | Failure and GC records | Pass |
| `20261019T093837289-67cf3f9e` | Failure context | Fail. `selected-50` median 5.09 points and `selected-75` median 5.41 points. |
| `20261019T110212880-744018ed` | Latency progress points | Fail. Points selected-25, selected-50, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetSemaphore` with one releasing thread and one waiting thread for each semaphore. CLR safety outside this workload is not established.
