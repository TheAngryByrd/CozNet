# Target-local spin lock profile

Evidence: `artifacts/target-local-spinlock/20261019T074539772-03c40325`.

`TargetLocalSpinLockVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `spin-lock-profile` process and seven `plain-spin-lock` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. Two workers each repeat `Work.peer 20` outside a `TargetLock` that spins up to 1 ms before it blocks, then `Work.selected 40` inside it, and then visit the progress point.
The criteria are in [the spin lock plan](../../../lode/plans/target-local-spin-lock.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 3 / 5 | 22.07% | 23.56% | 3.18 pp | 5.63 pp | Fail |
| `selected-50` | 3 / 5 | 47.36% | 48.58% | 1.77 pp | 2.94 pp | Fail |
| `selected-75` | 3 / 5 | 68.96% | 62.09% | 6.87 pp | 8.07 pp | Fail |
| `peer-25` | 3 / 5 | 0.18% | -0.30% | 1.29 pp | 2.07 pp | Fail |
| `peer-50` | 3 / 5 | -1.09% | 1.41% | 2.35 pp | 6.00 pp | Fail |
| `peer-75` | 3 / 5 | -0.65% | 1.30% | 3.58 pp | 3.65 pp | Fail |

The lock sets the rate, so the model gives `Work.selected` 25%, 50%, and 62.5%, and `Work.peer` 0% at each speedup. At 75%, the work outside the lock sets the rate.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.966, 0.967 |
| `selected-50` | 0.947, 0.953 |
| `selected-75` | 0.973, 0.969 |
| `peer-25` | 1.119, 1.117 |
| `peer-50` | 1.182, 1.178 |
| `peer-75` | 1.313, 1.327 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 1.000, 1.000; order 2: 1.000, 1.001; order 3: 1.088, 1.084; order 4: 1.000, 1.000; order 5: 0.999, 0.964; order 6: 0.947, 0.968. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Lock handoff

The handoff share is the time from a release to the acquire of a waiting thread, divided by the elapsed time. The share counts only handoffs to a thread that waited. See [the handoff plan](../../../lode/plans/target-local-lock-handoff.md) for the hypothesis that these handoffs cause the knee error.

| Point | Median handoff share, profile | Median handoff share, real optimization |
| --- | ---: | ---: |
| `selected-25` | 0.17% | 0.63% |
| `selected-50` | 2.84% | 0.53% |
| `selected-75` | 1.41% | 0.40% |
| `peer-25` | 0.06% | 0.42% |
| `peer-50` | 0.08% | 0.33% |
| `peer-75` | 1.51% | 0.68% |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -2.48 pp | -1.23 pp | 2.87 pp | -1.29 pp | -2.35 pp | -0.44 pp |
| 2 | Yes | -3.18 pp | -2.94 pp | 6.87 pp | 2.07 pp | -6.00 pp | -3.58 pp |
| 3 | Yes | 5.63 pp | -1.77 pp | 8.07 pp | -0.12 pp | -1.10 pp | -3.65 pp |
| 4 | No: profile has fewer than 100 visits | 1.74 pp | -9.52 pp | 7.70 pp | -18.18 pp | -0.77 pp | -0.37 pp |
| 5 | No: profile has fewer than 100 visits | -1.43 pp | -0.03 pp | 4.02 pp | 1.04 pp | 3.72 pp | -1.81 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T074539772-03c40325` | Spin before a lock wait | Fail. Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetLock`. CLR safety outside this workload is not established.
