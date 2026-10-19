# Target-local semaphore profile

Evidence: `artifacts/target-local-semaphore/20261019T121226146-ac4f2bea`.

`TargetLocalSemaphoreVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `semaphore-profile` process and seven `plain-semaphore` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. Two workers take turns through two `TargetSemaphore` objects. Worker 0 runs `Work.selected 24` and releases worker 1, which runs `Work.peer 12`, visits the progress point, and releases worker 0.
The criteria are in [the semaphore plan](../../../lode/plans/target-local-semaphore.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-50, selected-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 15.13% | 15.89% | 2.98 pp | 5.53 pp | Pass |
| `selected-50` | 5 / 5 | 29.70% | 35.54% | 6.60 pp | 8.14 pp | Fail |
| `selected-75` | 5 / 5 | 46.03% | 50.57% | 5.27 pp | 6.73 pp | Fail |
| `peer-25` | 5 / 5 | 7.27% | 8.97% | 1.70 pp | 3.00 pp | Pass |
| `peer-50` | 5 / 5 | 12.73% | 16.89% | 4.32 pp | 5.56 pp | Pass |
| `peer-75` | 5 / 5 | 21.87% | 24.10% | 2.68 pp | 5.53 pp | Pass |

No work runs in parallel, so the model gives `Work.selected` 16.7%, 33.3%, and 50%, and `Work.peer` 8.3%, 16.7%, and 25%. The model leaves out the two handoffs of each visit.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 1.000, 1.000 |
| `selected-50` | 1.004, 0.996 |
| `selected-75` | 0.991, 0.992 |
| `peer-25` | 1.005, 1.004 |
| `peer-50` | 1.018, 1.018 |
| `peer-75` | 1.007, 1.003 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 1.000, 1.000; order 2: 1.004, 1.000; order 3: 1.005, 1.004; order 4: 1.015, 1.015; order 5: 0.992, 0.992; order 6: 0.987, 0.985. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -3.06 pp | -8.14 pp | -3.07 pp | -1.25 pp | -5.56 pp | -5.53 pp |
| 2 | Yes | 2.38 pp | -1.84 pp | -6.73 pp | -2.18 pp | 4.32 pp | -0.05 pp |
| 3 | Yes | -5.53 pp | -6.60 pp | -5.58 pp | -0.97 pp | -5.08 pp | -2.68 pp |
| 4 | Yes | -2.98 pp | -2.76 pp | -2.57 pp | -1.70 pp | -4.16 pp | -4.11 pp |
| 5 | Yes | -0.76 pp | -6.87 pp | -5.27 pp | -3.00 pp | -4.17 pp | -2.27 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T082323819-903a6cef` | First semaphore profile run | Fail. `selected-75` has one block of -10.61 points, and `peer-75` median 5.24 points. |
| `20261019T090345985-b2474637` | Failure and GC records | Pass |
| `20261019T093837289-67cf3f9e` | Failure context | Fail. `selected-50` median 5.09 points and `selected-75` median 5.41 points. |
| `20261019T110212880-744018ed` | Latency progress points | Fail. Five points fail: four by one block each, and `peer-50` by two blocks. |
| `20261019T113503667-76dc9109` | Per-transaction virtual latency | Fail. `selected-50` median 5.43 points and `selected-75` median 6.21 points. |
| `20261019T121226146-ac4f2bea` | Transaction queue handoff | Fail. Points selected-50, selected-75 fail, or a run-level criterion fails. |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetSemaphore` with one releasing thread and one waiting thread for each semaphore. CLR safety outside this workload is not established.
