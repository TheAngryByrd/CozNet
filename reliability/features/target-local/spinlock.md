# Target-local spin lock profile

Evidence: `artifacts/target-local-spinlock/20261019T113703476-df6944a8`.

`TargetLocalSpinLockVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `spin-lock-profile` process and seven `plain-spin-lock` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. Two workers each repeat `Work.peer 20` outside a `TargetLock` that spins up to 1 ms before it blocks, then `Work.selected 40` inside it, and then visit the progress point.
The criteria are in [the spin lock plan](../../../lode/plans/target-local-spin-lock.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 4 / 5 | 25.23% | 25.10% | 1.96 pp | 3.38 pp | Fail |
| `selected-50` | 4 / 5 | 46.07% | 49.24% | 3.55 pp | 4.55 pp | Fail |
| `selected-75` | 4 / 5 | 68.77% | 61.72% | 6.93 pp | 10.00 pp | Fail |
| `peer-25` | 4 / 5 | 0.35% | 0.08% | 2.80 pp | 6.38 pp | Fail |
| `peer-50` | 4 / 5 | 0.50% | -0.92% | 1.99 pp | 2.65 pp | Fail |
| `peer-75` | 4 / 5 | -3.39% | -0.07% | 2.57 pp | 6.53 pp | Fail |

The lock sets the rate, so the model gives `Work.selected` 25%, 50%, and 62.5%, and `Work.peer` 0% at each speedup. At 75%, the work outside the lock sets the rate.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.930, 0.933 |
| `selected-50` | 0.963, 0.963 |
| `selected-75` | 0.980, 0.946 |
| `peer-25` | 1.114, 1.093 |
| `peer-50` | 1.174, 1.199 |
| `peer-75` | 1.340, 1.390 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 1.017, 1.035; order 2: 1.000, 1.000; order 3: 1.098, 1.095; order 4: 1.000, 1.000; order 5: 0.981, 0.951; order 6: 0.966, 1.000. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Lock handoff

The handoff share is the time from a release to the acquire of a waiting thread, divided by the elapsed time. The share counts only handoffs to a thread that waited. See [the handoff plan](../../../lode/plans/target-local-lock-handoff.md) for the hypothesis that these handoffs cause the knee error.

| Point | Median handoff share, profile | Median handoff share, real optimization |
| --- | ---: | ---: |
| `selected-25` | 0.09% | 0.49% |
| `selected-50` | 3.35% | 0.49% |
| `selected-75` | 1.57% | 0.36% |
| `peer-25` | 0.08% | 0.40% |
| `peer-50` | 0.09% | 0.52% |
| `peer-75` | 1.84% | 0.53% |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | 0.45 pp | -3.71 pp | 10.00 pp | -0.14 pp | -0.58 pp | -6.53 pp |
| 2 | Yes | -3.38 pp | -4.55 pp | 5.39 pp | -3.44 pp | -1.68 pp | -3.24 pp |
| 3 | Yes | -1.83 pp | -3.39 pp | 6.35 pp | -2.16 pp | 2.30 pp | -1.91 pp |
| 4 | No: profile has fewer than 100 visits | 1.47 pp | -3.57 pp | 5.01 pp | 0.19 pp | -0.80 pp | -6.71 pp |
| 5 | Yes | 2.10 pp | -0.45 pp | 7.52 pp | 6.38 pp | 2.65 pp | -1.13 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T074539772-03c40325` | First spin lock profile run | Fail. Blocks 4 and 5 are invalid: `peer` at 75% has 97 and 98 visits. In the three valid blocks, `selected-50` has a median of 1.77 points, and `selected-75` fails at 6.87 points. |
| `20261019T082529889-abecc54b` | TargetSemaphore and semaphore profile | Fail. Block 3 is invalid: `peer` at 75% has 96 visits. `selected-75` median 6.70 points with one block of 10.52 points. |
| `20261019T090601089-7d489720` | Failure and GC records | Fail. Block 4 is invalid: `peer` at 75% has 98 visits. |
| `20261019T094042685-1d761439` | Failure context | Fail. Block 5 is invalid: `peer` at 75% has 99 visits. `selected-75` median 8.59 points. |
| `20261019T110413189-a8e4bb89` | Latency progress points | Fail. Block 5 is invalid: `peer` at 75% has 99 visits. `selected-75` median 7.87 points. |
| `20261019T113703476-df6944a8` | Per-transaction virtual latency | Fail. Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetLock`. CLR safety outside this workload is not established.
