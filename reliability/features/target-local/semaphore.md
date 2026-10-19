# Target-local semaphore profile

Evidence: `artifacts/target-local-semaphore/20261019T113503667-76dc9109`.

`TargetLocalSemaphoreVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `semaphore-profile` process and seven `plain-semaphore` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. Two workers take turns through two `TargetSemaphore` objects. Worker 0 runs `Work.selected 24` and releases worker 1, which runs `Work.peer 12`, visits the progress point, and releases worker 0.
The criteria are in [the semaphore plan](../../../lode/plans/target-local-semaphore.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-50, selected-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 12.75% | 16.61% | 4.86 pp | 8.97 pp | Pass |
| `selected-50` | 5 / 5 | 28.80% | 34.50% | 5.43 pp | 6.60 pp | Fail |
| `selected-75` | 5 / 5 | 42.97% | 48.99% | 6.21 pp | 9.11 pp | Fail |
| `peer-25` | 5 / 5 | 6.93% | 7.52% | 2.13 pp | 5.14 pp | Pass |
| `peer-50` | 5 / 5 | 14.10% | 16.56% | 2.91 pp | 4.19 pp | Pass |
| `peer-75` | 5 / 5 | 22.12% | 23.37% | 3.11 pp | 5.69 pp | Pass |

No work runs in parallel, so the model gives `Work.selected` 16.7%, 33.3%, and 50%, and `Work.peer` 8.3%, 16.7%, and 25%. The model leaves out the two handoffs of each visit.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 1.029, 1.029 |
| `selected-50` | 1.010, 1.011 |
| `selected-75` | 1.005, 1.005 |
| `peer-25` | 1.014, 1.014 |
| `peer-50` | 1.015, 1.015 |
| `peer-75` | 1.017, 1.021 |

The median work ratio by run order in the process is order 0: 1.011, 1.024; order 1: 1.009, 1.009; order 2: 1.011, 1.011; order 3: 1.003, 1.004; order 4: 1.004, 1.005; order 5: 1.024, 1.024; order 6: 1.015, 1.015. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -4.86 pp | -5.67 pp | -3.86 pp | -5.14 pp | -4.19 pp | -2.58 pp |
| 2 | Yes | -4.71 pp | -5.43 pp | -6.41 pp | 0.52 pp | 0.56 pp | 1.43 pp |
| 3 | Yes | -0.65 pp | -4.85 pp | -3.50 pp | 1.20 pp | 2.91 pp | -3.11 pp |
| 4 | Yes | -8.97 pp | -4.01 pp | -6.21 pp | 2.13 pp | -0.37 pp | 5.69 pp |
| 5 | Yes | -6.32 pp | -6.60 pp | -9.11 pp | -2.13 pp | -3.30 pp | -5.53 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T082323819-903a6cef` | First semaphore profile run | Fail. `selected-75` has one block of -10.61 points, and `peer-75` median 5.24 points. |
| `20261019T090345985-b2474637` | Failure and GC records | Pass |
| `20261019T093837289-67cf3f9e` | Failure context | Fail. `selected-50` median 5.09 points and `selected-75` median 5.41 points. |
| `20261019T110212880-744018ed` | Latency progress points | Fail. Five points fail: four by one block each, and `peer-50` by two blocks. |
| `20261019T113503667-76dc9109` | Per-transaction virtual latency | Fail. Points selected-50, selected-75 fail, or a run-level criterion fails. |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetSemaphore` with one releasing thread and one waiting thread for each semaphore. CLR safety outside this workload is not established.
