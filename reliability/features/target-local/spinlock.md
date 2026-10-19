# Target-local spin lock profile

Evidence: `artifacts/target-local-spinlock/20261019T202120350-c3aa911b`.

`TargetLocalSpinLockVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `spin-lock-profile` process and seven `plain-spin-lock` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. Two workers each repeat `Work.peer 20` outside a `TargetLock` that spins up to 1 ms before it blocks, then `Work.selected 40` inside it, and then visit the progress point.
The criteria are in [the spin lock plan](../../../lode/plans/target-local-spin-lock.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 4 / 5 | 23.24% | 23.82% | 1.34 pp | 2.75 pp | Fail |
| `selected-50` | 4 / 5 | 45.67% | 48.74% | 3.00 pp | 4.34 pp | Fail |
| `selected-75` | 4 / 5 | 66.81% | 62.20% | 4.96 pp | 9.66 pp | Fail |
| `peer-25` | 4 / 5 | -0.52% | -1.59% | 1.38 pp | 3.66 pp | Fail |
| `peer-50` | 4 / 5 | -1.88% | 0.05% | 1.32 pp | 5.34 pp | Fail |
| `peer-75` | 4 / 5 | -2.48% | 0.70% | 2.39 pp | 8.20 pp | Fail |

The lock sets the rate, so the model gives `Work.selected` 25%, 50%, and 62.5%, and `Work.peer` 0% at each speedup. At 75%, the work outside the lock sets the rate.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.963, 0.955 |
| `selected-50` | 0.977, 0.962 |
| `selected-75` | 0.992, 0.973 |
| `peer-25` | 1.098, 1.104 |
| `peer-50` | 1.230, 1.252 |
| `peer-75` | 1.319, 1.357 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 1.005, 1.000; order 2: 1.000, 1.000; order 3: 1.086, 1.037; order 4: 1.005, 1.000; order 5: 1.022, 1.027; order 6: 0.977, 1.022. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Lock handoff

The handoff share is the time from a release to the acquire of a waiting thread, divided by the elapsed time. The share counts only handoffs to a thread that waited. See [the handoff plan](../../../lode/plans/target-local-lock-handoff.md) for the hypothesis that these handoffs cause the knee error.

| Point | Median handoff share, profile | Median handoff share, real optimization |
| --- | ---: | ---: |
| `selected-25` | 0.15% | 0.22% |
| `selected-50` | 2.73% | 0.09% |
| `selected-75` | 0.58% | 0.02% |
| `peer-25` | 0.19% | 0.22% |
| `peer-50` | 0.10% | 0.18% |
| `peer-75` | 1.28% | 0.11% |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | 1.51 pp | -1.48 pp | 5.95 pp | 1.91 pp | 0.85 pp | -0.65 pp |
| 2 | Yes | -1.17 pp | -2.92 pp | 9.66 pp | 3.66 pp | 1.79 pp | -1.45 pp |
| 3 | Yes | -2.75 pp | -4.34 pp | 3.98 pp | 0.85 pp | -0.71 pp | -3.34 pp |
| 4 | No: profile has fewer than 100 visits | -1.17 pp | -4.18 pp | 1.07 pp | -3.74 pp | -0.82 pp | -4.79 pp |
| 5 | Yes | 0.93 pp | -3.08 pp | 0.51 pp | -0.09 pp | -5.34 pp | -8.20 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T074539772-03c40325` | First spin lock profile run | Fail. Blocks 4 and 5 are invalid: `peer` at 75% has 97 and 98 visits. In the three valid blocks, `selected-50` has a median of 1.77 points, and `selected-75` fails at 6.87 points. |
| `20261019T082529889-abecc54b` | TargetSemaphore and semaphore profile | Fail. Block 3 is invalid: `peer` at 75% has 96 visits. `selected-75` median 6.70 points with one block of 10.52 points. |
| `20261019T090601089-7d489720` | Failure and GC records | Fail. Block 4 is invalid: `peer` at 75% has 98 visits. |
| `20261019T094042685-1d761439` | Failure context | Fail. Block 5 is invalid: `peer` at 75% has 99 visits. `selected-75` median 8.59 points. |
| `20261019T110413189-a8e4bb89` | Latency progress points | Fail. Block 5 is invalid: `peer` at 75% has 99 visits. `selected-75` median 7.87 points. |
| `20261019T113703476-df6944a8` | Per-transaction virtual latency | Fail. Block 4 is invalid: `peer` at 75% has 95 visits. `selected-75` median 6.93 points. |
| `20261019T121428141-2d666768` | Transaction queue handoff | Fail. `selected-75` median 6.37 points. All five blocks are valid. |
| `20261019T125215820-36780b18` | Spin before a queue wait | Fail. Block 2 is invalid: `peer` at 75% has 99 visits. `selected-75` median 5.60 points. |
| `20261019T132014752-3e849f73` | Handoff parts | Fail. `selected-75` one block of 12.34 points. All five blocks are valid. |
| `20261019T150642975-999bd98a` | Worker pinning | Fail. Block 2 is invalid: `peer` at 75% has 99 visits. `selected-75` median 8.09 points. |
| `20261019T175936455-d8d0f784` | Matched plain warm-up | Fail. Blocks 1, 2, 4, and 5 are invalid: the profile `peer` experiment at 75% has 95 to 98 visits. |
| `20261019T202120350-c3aa911b` | Qualification package | Fail. Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetLock`. CLR safety outside this workload is not established.
