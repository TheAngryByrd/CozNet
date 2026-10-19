# Target-local semaphore profile

Evidence: `artifacts/target-local-semaphore/20261019T131809935-75def747`.

`TargetLocalSemaphoreVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `semaphore-profile` process and seven `plain-semaphore` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. Two workers take turns through two `TargetSemaphore` objects. Worker 0 runs `Work.selected 24` and releases worker 1, which runs `Work.peer 12`, visits the progress point, and releases worker 0.
The criteria are in [the semaphore plan](../../../lode/plans/target-local-semaphore.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run passes.**

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 14.98% | 17.86% | 4.85 pp | 6.29 pp | Pass |
| `selected-50` | 5 / 5 | 29.89% | 33.77% | 4.08 pp | 6.73 pp | Pass |
| `selected-75` | 5 / 5 | 44.63% | 49.12% | 4.91 pp | 5.30 pp | Pass |
| `peer-25` | 5 / 5 | 8.74% | 8.00% | 0.33 pp | 3.63 pp | Pass |
| `peer-50` | 5 / 5 | 14.29% | 15.04% | 3.58 pp | 7.57 pp | Pass |
| `peer-75` | 5 / 5 | 19.76% | 23.01% | 4.27 pp | 8.41 pp | Pass |

No work runs in parallel, so the model gives `Work.selected` 16.7%, 33.3%, and 50%, and `Work.peer` 8.3%, 16.7%, and 25%. The model leaves out the two handoffs of each visit.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.992, 0.992 |
| `selected-50` | 1.001, 0.996 |
| `selected-75` | 0.985, 0.985 |
| `peer-25` | 0.984, 0.984 |
| `peer-50` | 0.998, 0.997 |
| `peer-75` | 1.018, 1.018 |

The median work ratio by run order in the process is order 0: 0.989, 0.989; order 1: 0.999, 1.000; order 2: 1.000, 1.000; order 3: 0.990, 0.989; order 4: 1.000, 1.000; order 5: 0.998, 0.997; order 6: 1.001, 0.996. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -0.59 pp | -4.25 pp | 0.02 pp | -0.33 pp | -1.22 pp | 3.35 pp |
| 2 | Yes | -6.29 pp | -3.10 pp | -4.67 pp | 0.20 pp | -4.18 pp | -4.32 pp |
| 3 | Yes | -5.26 pp | -6.73 pp | -5.09 pp | -3.63 pp | -7.57 pp | -8.41 pp |
| 4 | Yes | 4.85 pp | -2.91 pp | -4.91 pp | -0.33 pp | -0.39 pp | 0.82 pp |
| 5 | Yes | -3.52 pp | -4.08 pp | -5.30 pp | 2.68 pp | 3.58 pp | -4.27 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T082323819-903a6cef` | First semaphore profile run | Fail. `selected-75` has one block of -10.61 points, and `peer-75` median 5.24 points. |
| `20261019T090345985-b2474637` | Failure and GC records | Pass |
| `20261019T093837289-67cf3f9e` | Failure context | Fail. `selected-50` median 5.09 points and `selected-75` median 5.41 points. |
| `20261019T110212880-744018ed` | Latency progress points | Fail. Five points fail: four by one block each, and `peer-50` by two blocks. |
| `20261019T113503667-76dc9109` | Per-transaction virtual latency | Fail. `selected-50` median 5.43 points and `selected-75` median 6.21 points. |
| `20261019T121226146-ac4f2bea` | Transaction queue handoff | Fail. `selected-50` median 6.60 points and `selected-75` median 5.27 points. |
| `20261019T125015330-52253802` | Spin before a queue wait | Fail. `selected-50` median 5.51 points. |
| `20261019T131809935-75def747` | Handoff parts | Pass |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetSemaphore` with one releasing thread and one waiting thread for each semaphore. CLR safety outside this workload is not established.
