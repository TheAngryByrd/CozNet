# Target-local spin lock profile

Evidence: `artifacts/target-local-spinlock/20261019T121428141-2d666768`.

`TargetLocalSpinLockVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `spin-lock-profile` process and seven `plain-spin-lock` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. Two workers each repeat `Work.peer 20` outside a `TargetLock` that spins up to 1 ms before it blocks, then `Work.selected 40` inside it, and then visit the progress point.
The criteria are in [the spin lock plan](../../../lode/plans/target-local-spin-lock.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 24.30% | 25.06% | 1.12 pp | 3.36 pp | Pass |
| `selected-50` | 5 / 5 | 46.04% | 49.32% | 2.86 pp | 5.86 pp | Pass |
| `selected-75` | 5 / 5 | 68.61% | 62.24% | 6.37 pp | 8.62 pp | Fail |
| `peer-25` | 5 / 5 | -1.08% | 0.69% | 2.04 pp | 4.09 pp | Pass |
| `peer-50` | 5 / 5 | -1.39% | -2.19% | 1.45 pp | 6.01 pp | Pass |
| `peer-75` | 5 / 5 | -1.86% | -0.28% | 1.73 pp | 9.49 pp | Pass |

The lock sets the rate, so the model gives `Work.selected` 25%, 50%, and 62.5%, and `Work.peer` 0% at each speedup. At 75%, the work outside the lock sets the rate.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.938, 0.941 |
| `selected-50` | 0.940, 0.966 |
| `selected-75` | 1.010, 0.962 |
| `peer-25` | 1.103, 1.127 |
| `peer-50` | 1.165, 1.224 |
| `peer-75` | 1.353, 1.353 |

The median work ratio by run order in the process is order 0: 1.017, 1.008; order 1: 1.018, 1.000; order 2: 1.000, 1.000; order 3: 1.113, 1.127; order 4: 1.000, 1.000; order 5: 0.957, 0.962; order 6: 1.010, 0.964. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Lock handoff

The handoff share is the time from a release to the acquire of a waiting thread, divided by the elapsed time. The share counts only handoffs to a thread that waited. See [the handoff plan](../../../lode/plans/target-local-lock-handoff.md) for the hypothesis that these handoffs cause the knee error.

| Point | Median handoff share, profile | Median handoff share, real optimization |
| --- | ---: | ---: |
| `selected-25` | 0.20% | 0.45% |
| `selected-50` | 3.96% | 0.49% |
| `selected-75` | 1.22% | 0.48% |
| `peer-25` | 0.20% | 0.38% |
| `peer-50` | 0.50% | 0.50% |
| `peer-75` | 1.34% | 0.48% |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -0.81 pp | -3.31 pp | 6.37 pp | -3.40 pp | -1.45 pp | -1.73 pp |
| 2 | Yes | -0.48 pp | -2.04 pp | 5.12 pp | -0.39 pp | 1.38 pp | -4.70 pp |
| 3 | Yes | -3.30 pp | -5.86 pp | 3.97 pp | -4.09 pp | -6.01 pp | -9.49 pp |
| 4 | Yes | -1.12 pp | -2.86 pp | 7.89 pp | -0.72 pp | 0.78 pp | -0.32 pp |
| 5 | Yes | 3.36 pp | -1.45 pp | 8.62 pp | 2.04 pp | 1.76 pp | 0.42 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T074539772-03c40325` | First spin lock profile run | Fail. Blocks 4 and 5 are invalid: `peer` at 75% has 97 and 98 visits. In the three valid blocks, `selected-50` has a median of 1.77 points, and `selected-75` fails at 6.87 points. |
| `20261019T082529889-abecc54b` | TargetSemaphore and semaphore profile | Fail. Block 3 is invalid: `peer` at 75% has 96 visits. `selected-75` median 6.70 points with one block of 10.52 points. |
| `20261019T090601089-7d489720` | Failure and GC records | Fail. Block 4 is invalid: `peer` at 75% has 98 visits. |
| `20261019T094042685-1d761439` | Failure context | Fail. Block 5 is invalid: `peer` at 75% has 99 visits. `selected-75` median 8.59 points. |
| `20261019T110413189-a8e4bb89` | Latency progress points | Fail. Block 5 is invalid: `peer` at 75% has 99 visits. `selected-75` median 7.87 points. |
| `20261019T113703476-df6944a8` | Per-transaction virtual latency | Fail. Block 4 is invalid: `peer` at 75% has 95 visits. `selected-75` median 6.93 points. |
| `20261019T121428141-2d666768` | Transaction queue handoff | Fail. Points selected-75 fail, or a run-level criterion fails. |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetLock`. CLR safety outside this workload is not established.
