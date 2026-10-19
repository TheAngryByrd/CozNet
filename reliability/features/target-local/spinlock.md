# Target-local spin lock profile

Evidence: `artifacts/target-local-spinlock/20261019T110413189-a8e4bb89`.

`TargetLocalSpinLockVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `spin-lock-profile` process and seven `plain-spin-lock` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. Two workers each repeat `Work.peer 20` outside a `TargetLock` that spins up to 1 ms before it blocks, then `Work.selected 40` inside it, and then visit the progress point.
The criteria are in [the spin lock plan](../../../lode/plans/target-local-spin-lock.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 4 / 5 | 24.98% | 24.47% | 0.76 pp | 4.82 pp | Fail |
| `selected-50` | 4 / 5 | 47.53% | 49.48% | 2.38 pp | 6.22 pp | Fail |
| `selected-75` | 4 / 5 | 69.92% | 62.05% | 7.87 pp | 12.75 pp | Fail |
| `peer-25` | 4 / 5 | 1.48% | -2.04% | 4.37 pp | 6.09 pp | Fail |
| `peer-50` | 4 / 5 | -1.08% | -0.76% | 3.75 pp | 5.87 pp | Fail |
| `peer-75` | 4 / 5 | -2.32% | -0.65% | 3.87 pp | 7.48 pp | Fail |

The lock sets the rate, so the model gives `Work.selected` 25%, 50%, and 62.5%, and `Work.peer` 0% at each speedup. At 75%, the work outside the lock sets the rate.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.940, 0.933 |
| `selected-50` | 0.940, 0.946 |
| `selected-75` | 0.982, 0.970 |
| `peer-25` | 1.086, 1.094 |
| `peer-50` | 1.119, 1.233 |
| `peer-75` | 1.341, 1.327 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 1.000, 1.000; order 2: 1.000, 1.000; order 3: 1.050, 1.010; order 4: 1.000, 1.000; order 5: 0.999, 0.972; order 6: 0.982, 0.990. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Lock handoff

The handoff share is the time from a release to the acquire of a waiting thread, divided by the elapsed time. The share counts only handoffs to a thread that waited. See [the handoff plan](../../../lode/plans/target-local-lock-handoff.md) for the hypothesis that these handoffs cause the knee error.

| Point | Median handoff share, profile | Median handoff share, real optimization |
| --- | ---: | ---: |
| `selected-25` | 0.20% | 0.56% |
| `selected-50` | 3.46% | 0.49% |
| `selected-75` | 1.08% | 0.37% |
| `peer-25` | 0.16% | 0.61% |
| `peer-50` | 0.14% | 0.44% |
| `peer-75` | 1.44% | 0.73% |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -0.21 pp | -2.15 pp | 12.75 pp | -2.90 pp | -4.63 pp | -1.10 pp |
| 2 | Yes | 1.23 pp | -1.14 pp | 7.53 pp | 6.09 pp | 2.87 pp | -2.23 pp |
| 3 | Yes | -4.82 pp | -6.22 pp | 3.47 pp | -5.84 pp | -5.87 pp | -7.48 pp |
| 4 | Yes | -0.28 pp | -2.62 pp | 8.22 pp | 1.07 pp | -1.92 pp | -5.51 pp |
| 5 | No: profile has fewer than 100 visits | -3.59 pp | -4.05 pp | 9.09 pp | -2.54 pp | -3.92 pp | -8.96 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T074539772-03c40325` | First spin lock profile run | Fail. Blocks 4 and 5 are invalid: `peer` at 75% has 97 and 98 visits. In the three valid blocks, `selected-50` has a median of 1.77 points, and `selected-75` fails at 6.87 points. |
| `20261019T082529889-abecc54b` | TargetSemaphore and semaphore profile | Fail. Block 3 is invalid: `peer` at 75% has 96 visits. `selected-75` median 6.70 points with one block of 10.52 points. |
| `20261019T090601089-7d489720` | Failure and GC records | Fail. Block 4 is invalid: `peer` at 75% has 98 visits. |
| `20261019T094042685-1d761439` | Failure context | Fail. Block 5 is invalid: `peer` at 75% has 99 visits. `selected-75` median 8.59 points. |
| `20261019T110413189-a8e4bb89` | Latency progress points | Fail. Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetLock`. CLR safety outside this workload is not established.
