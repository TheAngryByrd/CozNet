# Target-local lock profile

Evidence: `artifacts/target-local-lock/20261019T082429851-cb3998b5`.

`TargetLocalLockVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `lock-profile` process and seven `plain-lock` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. Two workers each repeat `Work.peer 20` outside a `TargetLock`, then `Work.selected 40` inside it, and then visit the progress point.
The criteria are in [the lock plan](../../../lode/plans/target-local-lock.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-50, selected-75, peer-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 27.08% | 27.68% | 3.93 pp | 6.14 pp | Pass |
| `selected-50` | 5 / 5 | 46.86% | 52.49% | 5.62 pp | 7.16 pp | Fail |
| `selected-75` | 5 / 5 | 69.20% | 64.57% | 5.17 pp | 12.83 pp | Fail |
| `peer-25` | 5 / 5 | 1.30% | 0.32% | 2.27 pp | 7.27 pp | Pass |
| `peer-50` | 5 / 5 | -1.13% | 0.70% | 2.82 pp | 9.01 pp | Pass |
| `peer-75` | 5 / 5 | -2.32% | 0.44% | 5.55 pp | 7.90 pp | Fail |

The lock sets the rate, so the model gives `Work.selected` 25%, 50%, and 62.5%, and `Work.peer` 0% at each speedup. At 75%, the work outside the lock sets the rate.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.920, 0.914 |
| `selected-50` | 0.922, 0.923 |
| `selected-75` | 0.932, 0.937 |
| `peer-25` | 1.047, 1.046 |
| `peer-50` | 1.120, 1.126 |
| `peer-75` | 1.176, 1.160 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 0.973, 0.968; order 2: 1.000, 1.000; order 3: 1.000, 1.000; order 4: 1.000, 1.000; order 5: 0.944, 0.914; order 6: 1.009, 0.997. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Lock handoff

The handoff share is the time from a release to the acquire of a waiting thread, divided by the elapsed time. The share counts only handoffs to a thread that waited. See [the handoff plan](../../../lode/plans/target-local-lock-handoff.md) for the hypothesis that these handoffs cause the knee error.

| Point | Median handoff share, profile | Median handoff share, real optimization |
| --- | ---: | ---: |
| `selected-25` | 5.47% | 4.32% |
| `selected-50` | 8.25% | 1.46% |
| `selected-75` | 5.51% | 0.62% |
| `peer-25` | 5.36% | 6.98% |
| `peer-50` | 5.11% | 5.97% |
| `peer-75` | 4.18% | 5.68% |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -6.14 pp | -3.32 pp | 0.22 pp | -2.46 pp | -3.51 pp | -7.32 pp |
| 2 | Yes | -0.42 pp | -5.67 pp | 5.17 pp | 1.17 pp | -1.83 pp | -1.68 pp |
| 3 | Yes | -3.93 pp | -7.16 pp | 4.62 pp | -1.98 pp | -2.82 pp | -1.62 pp |
| 4 | Yes | 0.32 pp | -5.62 pp | 12.83 pp | 2.27 pp | -0.88 pp | -5.55 pp |
| 5 | Yes | 5.54 pp | 0.45 pp | 7.31 pp | 7.27 pp | 9.01 pp | 7.90 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T062708436-df83442b` | First lock profile run | Fail. `selected-50` median 9.13 points, all five blocks below the actual gain. `peer-25` has one block of 11.87 points. |
| `20261019T070331580-523cc380` | Lock handoff counts | Fail. Blocks 1 and 2 are invalid: `selected` at 50% breaks the 2 ms debt bound. In the three valid blocks, `selected-50` predicts 48.17% against 51.79%. The handoff share at `selected-50` is 7.40% in the profile and 2.18% in the real optimization. |
| `20261019T074433206-6d45795e` | Spin before a lock wait | Fail. `selected-50` median 7.89 points, all five blocks below the actual gain. `selected-75` median 5.07 points, with one block of 10.51 points. |
| `20261019T082429851-cb3998b5` | TargetSemaphore and semaphore profile | Fail. Points selected-50, selected-75, peer-75 fail, or a run-level criterion fails. |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetLock`. CLR safety outside this workload is not established.
