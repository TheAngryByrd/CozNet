# Target-local lock profile

Evidence: `artifacts/target-local-lock/20261019T074433206-6d45795e`.

`TargetLocalLockVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `lock-profile` process and seven `plain-lock` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. Two workers each repeat `Work.peer 20` outside a `TargetLock`, then `Work.selected 40` inside it, and then visit the progress point.
The criteria are in [the lock plan](../../../lode/plans/target-local-lock.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-50, selected-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 25.45% | 26.27% | 3.39 pp | 5.81 pp | Pass |
| `selected-50` | 5 / 5 | 43.94% | 51.74% | 7.89 pp | 9.08 pp | Fail |
| `selected-75` | 5 / 5 | 65.15% | 64.42% | 5.07 pp | 10.51 pp | Fail |
| `peer-25` | 5 / 5 | 1.13% | -0.05% | 2.39 pp | 4.48 pp | Pass |
| `peer-50` | 5 / 5 | -0.39% | 0.22% | 2.50 pp | 7.18 pp | Pass |
| `peer-75` | 5 / 5 | 0.01% | 2.58% | 1.54 pp | 5.85 pp | Pass |

The lock sets the rate, so the model gives `Work.selected` 25%, 50%, and 62.5%, and `Work.peer` 0% at each speedup. At 75%, the work outside the lock sets the rate.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.939, 0.927 |
| `selected-50` | 0.919, 0.940 |
| `selected-75` | 0.971, 0.958 |
| `peer-25` | 1.033, 1.026 |
| `peer-50` | 1.113, 1.114 |
| `peer-75` | 1.149, 1.157 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 1.000, 1.000; order 2: 1.014, 0.999; order 3: 1.023, 1.026; order 4: 1.000, 1.000; order 5: 0.971, 0.950; order 6: 0.959, 0.958. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Lock handoff

The handoff share is the time from a release to the acquire of a waiting thread, divided by the elapsed time. The share counts only handoffs to a thread that waited. See [the handoff plan](../../../lode/plans/target-local-lock-handoff.md) for the hypothesis that these handoffs cause the knee error.

| Point | Median handoff share, profile | Median handoff share, real optimization |
| --- | ---: | ---: |
| `selected-25` | 6.10% | 4.48% |
| `selected-50` | 7.97% | 1.22% |
| `selected-75` | 5.45% | 0.58% |
| `peer-25` | 5.48% | 6.25% |
| `peer-50` | 5.17% | 6.12% |
| `peer-75` | 4.51% | 5.96% |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -5.81 pp | -9.08 pp | -5.44 pp | 0.37 pp | -2.50 pp | -4.63 pp |
| 2 | Yes | -0.53 pp | -7.25 pp | 10.51 pp | 4.48 pp | 2.74 pp | -1.54 pp |
| 3 | Yes | -0.57 pp | -7.89 pp | -2.63 pp | 2.39 pp | -0.62 pp | -0.83 pp |
| 4 | Yes | -4.95 pp | -8.72 pp | -0.66 pp | 2.44 pp | -7.18 pp | -5.85 pp |
| 5 | Yes | -3.39 pp | -3.97 pp | 5.07 pp | 0.59 pp | 0.11 pp | 0.96 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T062708436-df83442b` | First lock profile run | Fail. `selected-50` median 9.13 points, all five blocks below the actual gain. `peer-25` has one block of 11.87 points. |
| `20261019T070331580-523cc380` | Lock handoff counts | Fail. Blocks 1 and 2 are invalid: `selected` at 50% breaks the 2 ms debt bound. In the three valid blocks, `selected-50` predicts 48.17% against 51.79%. The handoff share at `selected-50` is 7.40% in the profile and 2.18% in the real optimization. |
| `20261019T074433206-6d45795e` | Spin before a lock wait | Fail. Points selected-50, selected-75 fail, or a run-level criterion fails. |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetLock`. CLR safety outside this workload is not established.
