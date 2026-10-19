# Target-local spin lock profile

Evidence: `artifacts/target-local-spinlock/20261019T094042685-1d761439`.

`TargetLocalSpinLockVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `spin-lock-profile` process and seven `plain-spin-lock` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. Two workers each repeat `Work.peer 20` outside a `TargetLock` that spins up to 1 ms before it blocks, then `Work.selected 40` inside it, and then visit the progress point.
The criteria are in [the spin lock plan](../../../lode/plans/target-local-spin-lock.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 4 / 5 | 23.02% | 24.85% | 3.28 pp | 3.46 pp | Fail |
| `selected-50` | 4 / 5 | 47.54% | 48.86% | 3.05 pp | 7.17 pp | Fail |
| `selected-75` | 4 / 5 | 69.98% | 61.85% | 8.59 pp | 9.53 pp | Fail |
| `peer-25` | 4 / 5 | -1.43% | 1.22% | 2.99 pp | 4.44 pp | Fail |
| `peer-50` | 4 / 5 | 0.89% | 0.34% | 4.87 pp | 6.63 pp | Fail |
| `peer-75` | 4 / 5 | -2.01% | -0.49% | 3.89 pp | 9.08 pp | Fail |

The lock sets the rate, so the model gives `Work.selected` 25%, 50%, and 62.5%, and `Work.peer` 0% at each speedup. At 75%, the work outside the lock sets the rate.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.945, 0.960 |
| `selected-50` | 0.943, 0.943 |
| `selected-75` | 0.984, 0.975 |
| `peer-25` | 1.114, 1.107 |
| `peer-50` | 1.167, 1.212 |
| `peer-75` | 1.372, 1.316 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 1.000, 1.000; order 2: 1.000, 1.006; order 3: 1.136, 1.152; order 4: 1.010, 1.000; order 5: 0.980, 0.960; order 6: 0.999, 0.973. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Lock handoff

The handoff share is the time from a release to the acquire of a waiting thread, divided by the elapsed time. The share counts only handoffs to a thread that waited. See [the handoff plan](../../../lode/plans/target-local-lock-handoff.md) for the hypothesis that these handoffs cause the knee error.

| Point | Median handoff share, profile | Median handoff share, real optimization |
| --- | ---: | ---: |
| `selected-25` | 0.15% | 0.58% |
| `selected-50` | 3.47% | 0.56% |
| `selected-75` | 1.20% | 0.53% |
| `peer-25` | 0.12% | 0.51% |
| `peer-50` | 0.15% | 0.43% |
| `peer-75` | 1.57% | 0.57% |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -3.30 pp | -4.53 pp | 2.30 pp | -4.44 pp | -2.79 pp | -4.51 pp |
| 2 | Yes | 3.26 pp | 1.57 pp | 8.41 pp | 0.33 pp | 6.63 pp | 3.27 pp |
| 3 | Yes | -3.46 pp | -7.17 pp | 8.77 pp | -3.15 pp | -3.64 pp | -9.08 pp |
| 4 | Yes | -0.53 pp | 1.32 pp | 9.53 pp | -2.83 pp | 6.09 pp | 3.19 pp |
| 5 | No: profile has fewer than 100 visits | -3.03 pp | -6.81 pp | 6.57 pp | -5.08 pp | -4.60 pp | -9.77 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T074539772-03c40325` | First spin lock profile run | Fail. Blocks 4 and 5 are invalid: `peer` at 75% has 97 and 98 visits. In the three valid blocks, `selected-50` has a median of 1.77 points, and `selected-75` fails at 6.87 points. |
| `20261019T082529889-abecc54b` | TargetSemaphore and semaphore profile | Fail. Block 3 is invalid: `peer` at 75% has 96 visits. `selected-75` median 6.70 points with one block of 10.52 points. |
| `20261019T090601089-7d489720` | Failure and GC records | Fail. Block 4 is invalid: `peer` at 75% has 98 visits. |
| `20261019T094042685-1d761439` | Failure context | Fail. Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetLock`. CLR safety outside this workload is not established.
