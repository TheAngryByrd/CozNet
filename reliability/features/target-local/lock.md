# Target-local lock profile

Evidence: `artifacts/target-local-lock/20261019T062708436-df83442b`.

`TargetLocalLockVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `lock-profile` process and seven `plain-lock` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. Two workers each repeat `Work.peer 20` outside a `TargetLock`, then `Work.selected 40` inside it, and then visit the progress point.
The criteria are in [the lock plan](../../../lode/plans/target-local-lock.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-50, peer-25 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 24.55% | 26.31% | 2.53 pp | 3.06 pp | Pass |
| `selected-50` | 5 / 5 | 42.62% | 52.02% | 9.13 pp | 12.95 pp | Fail |
| `selected-75` | 5 / 5 | 67.34% | 64.84% | 2.95 pp | 4.20 pp | Pass |
| `peer-25` | 5 / 5 | -2.50% | -1.88% | 1.41 pp | 11.87 pp | Fail |
| `peer-50` | 5 / 5 | -1.62% | 3.06% | 3.36 pp | 4.68 pp | Pass |
| `peer-75` | 5 / 5 | -0.81% | -2.55% | 3.46 pp | 8.22 pp | Pass |

The lock sets the rate, so the model gives `Work.selected` 25%, 50%, and 62.5%, and `Work.peer` 0% at each speedup. At 75%, the work outside the lock sets the rate.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.957, 0.956 |
| `selected-50` | 0.949, 0.934 |
| `selected-75` | 0.958, 0.959 |
| `peer-25` | 1.077, 1.064 |
| `peer-50` | 1.116, 1.106 |
| `peer-75` | 1.107, 1.128 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 1.000, 1.040; order 2: 0.982, 0.984; order 3: 1.073, 1.083; order 4: 1.000, 1.000; order 5: 0.958, 0.939; order 6: 0.976, 0.996. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -3.06 pp | -12.32 pp | 2.50 pp | 1.41 pp | -4.68 pp | 7.06 pp |
| 2 | Yes | -2.53 pp | -6.51 pp | 0.40 pp | -11.87 pp | -3.36 pp | -3.16 pp |
| 3 | Yes | -2.50 pp | -9.13 pp | 2.95 pp | -0.59 pp | -1.81 pp | 0.85 pp |
| 4 | Yes | 1.36 pp | -5.17 pp | 3.74 pp | 3.38 pp | 2.37 pp | 8.22 pp |
| 5 | Yes | -2.87 pp | -12.95 pp | 4.20 pp | -0.87 pp | -4.32 pp | -3.46 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T062708436-df83442b` | First lock profile run | Fail. Points selected-50, peer-25 fail, or a run-level criterion fails. |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetLock`. CLR safety outside this workload is not established.
