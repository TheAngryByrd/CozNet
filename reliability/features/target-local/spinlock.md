# Target-local spin lock profile

Evidence: `artifacts/target-local-spinlock/20261019T132014752-3e849f73`.

`TargetLocalSpinLockVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `spin-lock-profile` process and seven `plain-spin-lock` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. Two workers each repeat `Work.peer 20` outside a `TargetLock` that spins up to 1 ms before it blocks, then `Work.selected 40` inside it, and then visit the progress point.
The criteria are in [the spin lock plan](../../../lode/plans/target-local-spin-lock.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 24.20% | 23.40% | 1.82 pp | 3.27 pp | Pass |
| `selected-50` | 5 / 5 | 46.05% | 49.93% | 2.54 pp | 5.30 pp | Pass |
| `selected-75` | 5 / 5 | 67.33% | 62.27% | 4.90 pp | 12.34 pp | Fail |
| `peer-25` | 5 / 5 | -1.26% | 0.13% | 1.17 pp | 5.01 pp | Pass |
| `peer-50` | 5 / 5 | -1.68% | 0.50% | 1.90 pp | 3.31 pp | Pass |
| `peer-75` | 5 / 5 | -3.09% | 0.22% | 3.25 pp | 6.95 pp | Pass |

The lock sets the rate, so the model gives `Work.selected` 25%, 50%, and 62.5%, and `Work.peer` 0% at each speedup. At 75%, the work outside the lock sets the rate.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.949, 0.939 |
| `selected-50` | 0.948, 0.960 |
| `selected-75` | 0.999, 0.992 |
| `peer-25` | 1.114, 1.106 |
| `peer-50` | 1.233, 1.254 |
| `peer-75` | 1.351, 1.388 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 1.129, 1.129; order 2: 1.000, 1.000; order 3: 1.139, 1.128; order 4: 1.000, 1.000; order 5: 1.003, 0.992; order 6: 0.999, 1.001. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Lock handoff

The handoff share is the time from a release to the acquire of a waiting thread, divided by the elapsed time. The share counts only handoffs to a thread that waited. See [the handoff plan](../../../lode/plans/target-local-lock-handoff.md) for the hypothesis that these handoffs cause the knee error.

| Point | Median handoff share, profile | Median handoff share, real optimization |
| --- | ---: | ---: |
| `selected-25` | 0.14% | 0.63% |
| `selected-50` | 3.16% | 0.51% |
| `selected-75` | 1.04% | 0.44% |
| `peer-25` | 0.15% | 0.64% |
| `peer-50` | 0.11% | 0.49% |
| `peer-75` | 1.27% | 0.65% |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -1.82 pp | -2.54 pp | 4.90 pp | -1.17 pp | 1.90 pp | -1.77 pp |
| 2 | Yes | -3.27 pp | -3.87 pp | -12.34 pp | -5.01 pp | -0.17 pp | -6.95 pp |
| 3 | Yes | 2.45 pp | -2.26 pp | 9.30 pp | -0.68 pp | -1.81 pp | -0.87 pp |
| 4 | Yes | 0.66 pp | 0.48 pp | 4.56 pp | -1.23 pp | -3.31 pp | -3.25 pp |
| 5 | Yes | 0.94 pp | -5.30 pp | 4.84 pp | 1.07 pp | -3.00 pp | -4.98 pp |

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
| `20261019T132014752-3e849f73` | Handoff parts | Fail. Points selected-75 fail, or a run-level criterion fails. |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetLock`. CLR safety outside this workload is not established.
