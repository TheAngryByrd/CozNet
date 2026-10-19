# Target-local lock profile

Evidence: `artifacts/target-local-lock/20261019T070331580-523cc380`.

`TargetLocalLockVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `lock-profile` process and seven `plain-lock` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. Two workers each repeat `Work.peer 20` outside a `TargetLock`, then `Work.selected 40` inside it, and then visit the progress point.
The criteria are in [the lock plan](../../../lode/plans/target-local-lock.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 3 / 5 | 26.07% | 26.21% | 2.21 pp | 4.47 pp | Fail |
| `selected-50` | 3 / 5 | 48.17% | 51.79% | 3.22 pp | 6.28 pp | Fail |
| `selected-75` | 3 / 5 | 66.49% | 64.97% | 2.58 pp | 3.55 pp | Fail |
| `peer-25` | 3 / 5 | 3.20% | 1.32% | 2.40 pp | 6.35 pp | Fail |
| `peer-50` | 3 / 5 | 3.48% | -0.02% | 2.30 pp | 4.68 pp | Fail |
| `peer-75` | 3 / 5 | 3.81% | 2.02% | 1.79 pp | 3.96 pp | Fail |

The lock sets the rate, so the model gives `Work.selected` 25%, 50%, and 62.5%, and `Work.peer` 0% at each speedup. At 75%, the work outside the lock sets the rate.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.920, 0.937 |
| `selected-50` | 0.872, 0.924 |
| `selected-75` | 0.914, 0.943 |
| `peer-25` | 1.021, 1.023 |
| `peer-50` | 1.057, 1.089 |
| `peer-75` | 1.154, 1.082 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 0.997, 0.986; order 2: 1.004, 1.000; order 3: 1.021, 1.023; order 4: 0.994, 1.000; order 5: 1.003, 1.044; order 6: 1.026, 1.004. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Lock handoff

The handoff share is the time from a release to the acquire of a waiting thread, divided by the elapsed time. The share counts only handoffs to a thread that waited. See [the handoff plan](../../../lode/plans/target-local-lock-handoff.md) for the hypothesis that these handoffs cause the knee error.

| Point | Median handoff share, profile | Median handoff share, real optimization |
| --- | ---: | ---: |
| `selected-25` | 5.49% | 5.54% |
| `selected-50` | 7.40% | 2.18% |
| `selected-75` | 5.99% | 1.47% |
| `peer-25` | 5.88% | 7.33% |
| `peer-50` | 4.90% | 6.93% |
| `peer-75` | 4.61% | 6.29% |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | No: profile exit 1; profile not passed | -4.68 pp | n/a | -0.89 pp | -7.77 pp | -4.76 pp | -1.17 pp |
| 2 | No: profile exit 1; profile not passed | -0.07 pp | n/a | 0.82 pp | 2.33 pp | -0.04 pp | -4.32 pp |
| 3 | Yes | 4.47 pp | -3.22 pp | 1.52 pp | 2.40 pp | 1.79 pp | 3.96 pp |
| 4 | Yes | -2.21 pp | -6.28 pp | 2.58 pp | -0.67 pp | -2.30 pp | -0.28 pp |
| 5 | Yes | -1.17 pp | -2.65 pp | -3.55 pp | 6.35 pp | 4.68 pp | 1.79 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T062708436-df83442b` | First lock profile run | Fail. `selected-50` median 9.13 points, all five blocks below the actual gain. `peer-25` has one block of 11.87 points. |
| `20261019T070331580-523cc380` | Lock handoff counts | Fail. Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetLock`. CLR safety outside this workload is not established.
