# Target-local spin lock profile

Evidence: `artifacts/target-local-spinlock/20261019T090601089-7d489720`.

`TargetLocalSpinLockVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `spin-lock-profile` process and seven `plain-spin-lock` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. Two workers each repeat `Work.peer 20` outside a `TargetLock` that spins up to 1 ms before it blocks, then `Work.selected 40` inside it, and then visit the progress point.
The criteria are in [the spin lock plan](../../../lode/plans/target-local-spin-lock.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 4 / 5 | 24.26% | 23.37% | 2.24 pp | 2.93 pp | Fail |
| `selected-50` | 4 / 5 | 45.38% | 48.96% | 4.03 pp | 7.30 pp | Fail |
| `selected-75` | 4 / 5 | 67.93% | 62.37% | 5.34 pp | 8.03 pp | Fail |
| `peer-25` | 4 / 5 | -1.84% | -0.99% | 3.75 pp | 5.54 pp | Fail |
| `peer-50` | 4 / 5 | -0.64% | -1.62% | 1.87 pp | 4.81 pp | Fail |
| `peer-75` | 4 / 5 | -3.24% | -0.66% | 3.45 pp | 6.13 pp | Fail |

The lock sets the rate, so the model gives `Work.selected` 25%, 50%, and 62.5%, and `Work.peer` 0% at each speedup. At 75%, the work outside the lock sets the rate.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.939, 0.946 |
| `selected-50` | 0.975, 0.969 |
| `selected-75` | 1.001, 0.998 |
| `peer-25` | 1.136, 1.125 |
| `peer-50` | 1.183, 1.235 |
| `peer-75` | 1.353, 1.372 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 1.012, 1.000; order 2: 1.000, 1.000; order 3: 1.116, 1.096; order 4: 1.000, 1.000; order 5: 1.023, 1.001; order 6: 0.996, 1.026. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Lock handoff

The handoff share is the time from a release to the acquire of a waiting thread, divided by the elapsed time. The share counts only handoffs to a thread that waited. See [the handoff plan](../../../lode/plans/target-local-lock-handoff.md) for the hypothesis that these handoffs cause the knee error.

| Point | Median handoff share, profile | Median handoff share, real optimization |
| --- | ---: | ---: |
| `selected-25` | 0.10% | 0.53% |
| `selected-50` | 2.88% | 0.49% |
| `selected-75` | 0.85% | 0.34% |
| `peer-25` | 0.08% | 0.48% |
| `peer-50` | 0.09% | 0.46% |
| `peer-75` | 1.58% | 0.62% |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -1.62 pp | -5.42 pp | 4.14 pp | -4.15 pp | -2.06 pp | -6.13 pp |
| 2 | Yes | 2.86 pp | -7.30 pp | 8.03 pp | -3.34 pp | -1.68 pp | -3.43 pp |
| 3 | Yes | 2.93 pp | -2.65 pp | 3.64 pp | 5.54 pp | 4.81 pp | -3.46 pp |
| 4 | No: profile has fewer than 100 visits | -2.21 pp | -1.17 pp | 5.92 pp | -3.35 pp | 0.03 pp | -6.02 pp |
| 5 | Yes | 0.54 pp | -1.72 pp | 6.54 pp | -1.90 pp | -0.42 pp | -1.28 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T074539772-03c40325` | First spin lock profile run | Fail. Blocks 4 and 5 are invalid: `peer` at 75% has 97 and 98 visits. In the three valid blocks, `selected-50` has a median of 1.77 points, and `selected-75` fails at 6.87 points. |
| `20261019T082529889-abecc54b` | TargetSemaphore and semaphore profile | Fail. Block 3 is invalid: `peer` at 75% has 96 visits. `selected-75` median 6.70 points with one block of 10.52 points. |
| `20261019T090601089-7d489720` | Failure and GC records | Fail. Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetLock`. CLR safety outside this workload is not established.
