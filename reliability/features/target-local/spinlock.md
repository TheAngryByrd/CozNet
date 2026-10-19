# Target-local spin lock profile

Evidence: `artifacts/target-local-spinlock/20261019T125215820-36780b18`.

`TargetLocalSpinLockVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `spin-lock-profile` process and seven `plain-spin-lock` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. Two workers each repeat `Work.peer 20` outside a `TargetLock` that spins up to 1 ms before it blocks, then `Work.selected 40` inside it, and then visit the progress point.
The criteria are in [the spin lock plan](../../../lode/plans/target-local-spin-lock.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 4 / 5 | 22.83% | 24.87% | 2.68 pp | 4.38 pp | Fail |
| `selected-50` | 4 / 5 | 45.87% | 48.86% | 2.04 pp | 4.54 pp | Fail |
| `selected-75` | 4 / 5 | 68.27% | 62.01% | 5.60 pp | 9.09 pp | Fail |
| `peer-25` | 4 / 5 | 0.32% | 1.12% | 1.97 pp | 4.72 pp | Fail |
| `peer-50` | 4 / 5 | -0.90% | 0.53% | 2.18 pp | 3.52 pp | Fail |
| `peer-75` | 4 / 5 | -1.70% | -0.29% | 3.71 pp | 7.42 pp | Fail |

The lock sets the rate, so the model gives `Work.selected` 25%, 50%, and 62.5%, and `Work.peer` 0% at each speedup. At 75%, the work outside the lock sets the rate.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.959, 0.965 |
| `selected-50` | 0.948, 0.973 |
| `selected-75` | 0.986, 0.985 |
| `peer-25` | 1.115, 1.108 |
| `peer-50` | 1.167, 1.251 |
| `peer-75` | 1.311, 1.352 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 1.000, 1.000; order 2: 1.003, 1.000; order 3: 1.122, 1.104; order 4: 1.000, 1.000; order 5: 0.952, 0.984; order 6: 1.010, 0.985. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Lock handoff

The handoff share is the time from a release to the acquire of a waiting thread, divided by the elapsed time. The share counts only handoffs to a thread that waited. See [the handoff plan](../../../lode/plans/target-local-lock-handoff.md) for the hypothesis that these handoffs cause the knee error.

| Point | Median handoff share, profile | Median handoff share, real optimization |
| --- | ---: | ---: |
| `selected-25` | 0.22% | 0.66% |
| `selected-50` | 3.83% | 0.51% |
| `selected-75` | 1.71% | 0.53% |
| `peer-25` | 0.12% | 0.58% |
| `peer-50` | 0.10% | 0.56% |
| `peer-75` | 1.62% | 0.57% |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -4.38 pp | -2.59 pp | 4.89 pp | -4.72 pp | -3.52 pp | -7.42 pp |
| 2 | No: profile has fewer than 100 visits | 5.52 pp | 0.44 pp | 6.19 pp | 0.69 pp | 3.05 pp | 2.39 pp |
| 3 | Yes | -3.41 pp | -4.54 pp | 6.31 pp | -1.59 pp | -2.98 pp | -3.73 pp |
| 4 | Yes | -0.96 pp | -1.39 pp | 4.04 pp | 2.34 pp | 0.67 pp | 1.92 pp |
| 5 | Yes | 1.94 pp | -1.49 pp | 9.09 pp | -0.02 pp | 1.39 pp | 3.70 pp |

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
| `20261019T125215820-36780b18` | Spin before a queue wait | Fail. Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetLock`. CLR safety outside this workload is not established.
