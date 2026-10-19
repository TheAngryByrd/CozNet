# Target-local semaphore profile

Evidence: `artifacts/target-local-semaphore/20261019T090345985-b2474637`.

`TargetLocalSemaphoreVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `semaphore-profile` process and seven `plain-semaphore` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. Two workers take turns through two `TargetSemaphore` objects. Worker 0 runs `Work.selected 24` and releases worker 1, which runs `Work.peer 12`, visits the progress point, and releases worker 0.
The criteria are in [the semaphore plan](../../../lode/plans/target-local-semaphore.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run passes.**

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 15.20% | 18.22% | 3.14 pp | 6.15 pp | Pass |
| `selected-50` | 5 / 5 | 29.35% | 32.92% | 2.49 pp | 5.54 pp | Pass |
| `selected-75` | 5 / 5 | 45.41% | 48.08% | 1.68 pp | 9.91 pp | Pass |
| `peer-25` | 5 / 5 | 7.38% | 6.70% | 2.06 pp | 3.17 pp | Pass |
| `peer-50` | 5 / 5 | 14.26% | 14.93% | 4.34 pp | 6.56 pp | Pass |
| `peer-75` | 5 / 5 | 19.57% | 21.73% | 3.59 pp | 7.59 pp | Pass |

No work runs in parallel, so the model gives `Work.selected` 16.7%, 33.3%, and 50%, and `Work.peer` 8.3%, 16.7%, and 25%. The model leaves out the two handoffs of each visit.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.995, 0.996 |
| `selected-50` | 1.011, 1.011 |
| `selected-75` | 0.991, 0.991 |
| `peer-25` | 0.999, 0.999 |
| `peer-50` | 1.018, 1.018 |
| `peer-75` | 1.020, 1.020 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 1.000, 1.000; order 2: 1.002, 1.002; order 3: 1.018, 1.018; order 4: 0.998, 0.998; order 5: 0.995, 0.993; order 6: 1.011, 1.011. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -6.15 pp | -2.49 pp | -1.52 pp | -0.48 pp | -6.56 pp | -7.59 pp |
| 2 | Yes | -1.10 pp | -5.40 pp | -4.62 pp | -2.47 pp | -6.55 pp | 3.79 pp |
| 3 | Yes | -3.23 pp | -2.16 pp | -9.91 pp | -0.95 pp | 4.34 pp | -2.16 pp |
| 4 | Yes | 0.30 pp | -0.62 pp | -1.68 pp | 2.06 pp | 0.34 pp | 3.59 pp |
| 5 | Yes | -3.14 pp | -5.54 pp | -0.05 pp | 3.17 pp | 1.79 pp | -2.33 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T082323819-903a6cef` | First semaphore profile run | Fail. `selected-75` has one block of -10.61 points, and `peer-75` median 5.24 points. |
| `20261019T090345985-b2474637` | Failure and GC records | Pass |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetSemaphore` with one releasing thread and one waiting thread for each semaphore. CLR safety outside this workload is not established.
