# Target-local spin lock profile

Evidence: `artifacts/target-local-spinlock/20261019T082529889-abecc54b`.

`TargetLocalSpinLockVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `spin-lock-profile` process and seven `plain-spin-lock` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. Two workers each repeat `Work.peer 20` outside a `TargetLock` that spins up to 1 ms before it blocks, then `Work.selected 40` inside it, and then visit the progress point.
The criteria are in [the spin lock plan](../../../lode/plans/target-local-spin-lock.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 4 / 5 | 24.97% | 24.27% | 1.46 pp | 2.55 pp | Fail |
| `selected-50` | 4 / 5 | 46.07% | 49.51% | 2.66 pp | 3.93 pp | Fail |
| `selected-75` | 4 / 5 | 69.06% | 62.36% | 6.70 pp | 10.52 pp | Fail |
| `peer-25` | 4 / 5 | -0.67% | -1.31% | 1.08 pp | 2.02 pp | Fail |
| `peer-50` | 4 / 5 | -0.80% | 0.83% | 3.76 pp | 5.05 pp | Fail |
| `peer-75` | 4 / 5 | -2.79% | -0.63% | 2.96 pp | 3.47 pp | Fail |

The lock sets the rate, so the model gives `Work.selected` 25%, 50%, and 62.5%, and `Work.peer` 0% at each speedup. At 75%, the work outside the lock sets the rate.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.939, 0.936 |
| `selected-50` | 0.951, 0.957 |
| `selected-75` | 0.969, 0.978 |
| `peer-25` | 1.128, 1.118 |
| `peer-50` | 1.159, 1.205 |
| `peer-75` | 1.327, 1.325 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 1.000, 1.026; order 2: 1.000, 1.000; order 3: 1.064, 1.107; order 4: 1.000, 1.000; order 5: 0.952, 0.985; order 6: 0.967, 0.963. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Lock handoff

The handoff share is the time from a release to the acquire of a waiting thread, divided by the elapsed time. The share counts only handoffs to a thread that waited. See [the handoff plan](../../../lode/plans/target-local-lock-handoff.md) for the hypothesis that these handoffs cause the knee error.

| Point | Median handoff share, profile | Median handoff share, real optimization |
| --- | ---: | ---: |
| `selected-25` | 0.09% | 0.57% |
| `selected-50` | 3.50% | 0.58% |
| `selected-75` | 2.03% | 0.50% |
| `peer-25` | 0.22% | 0.63% |
| `peer-50` | 0.15% | 0.43% |
| `peer-75` | 1.59% | 0.62% |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -2.55 pp | -3.93 pp | 7.87 pp | -2.02 pp | -5.05 pp | -3.47 pp |
| 2 | Yes | 1.78 pp | -2.15 pp | 5.53 pp | -0.44 pp | 2.47 pp | -2.95 pp |
| 3 | No: profile has fewer than 100 visits | -0.97 pp | -4.86 pp | 7.96 pp | 3.35 pp | -5.20 pp | -7.91 pp |
| 4 | Yes | 1.13 pp | -3.17 pp | 10.52 pp | 1.72 pp | 2.69 pp | 0.27 pp |
| 5 | Yes | 0.22 pp | -0.66 pp | 3.51 pp | -0.29 pp | -4.84 pp | -2.96 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T074539772-03c40325` | First spin lock profile run | Fail. Blocks 4 and 5 are invalid: `peer` at 75% has 97 and 98 visits. In the three valid blocks, `selected-50` has a median of 1.77 points, and `selected-75` fails at 6.87 points. |
| `20261019T082529889-abecc54b` | TargetSemaphore and semaphore profile | Fail. Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetLock`. CLR safety outside this workload is not established.
