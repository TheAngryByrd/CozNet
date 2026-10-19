# Target-local lock profile

Evidence: `artifacts/target-local-lock/20261019T150538269-4c383533`.

`TargetLocalLockVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `lock-profile` process and seven `plain-lock` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. Two workers each repeat `Work.peer 20` outside a `TargetLock`, then `Work.selected 40` inside it, and then visit the progress point.
The criteria are in [the lock plan](../../../lode/plans/target-local-lock.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-50, selected-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 23.72% | 26.31% | 2.71 pp | 6.72 pp | Pass |
| `selected-50` | 5 / 5 | 43.60% | 52.71% | 9.20 pp | 14.01 pp | Fail |
| `selected-75` | 5 / 5 | 70.11% | 64.16% | 6.47 pp | 9.63 pp | Fail |
| `peer-25` | 5 / 5 | -0.53% | 0.34% | 3.96 pp | 7.43 pp | Pass |
| `peer-50` | 5 / 5 | 0.10% | 0.63% | 1.35 pp | 4.87 pp | Pass |
| `peer-75` | 5 / 5 | -0.61% | 0.56% | 2.02 pp | 3.60 pp | Pass |

The lock sets the rate, so the model gives `Work.selected` 25%, 50%, and 62.5%, and `Work.peer` 0% at each speedup. At 75%, the work outside the lock sets the rate.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.951, 0.956 |
| `selected-50` | 0.939, 0.952 |
| `selected-75` | 0.973, 0.959 |
| `peer-25` | 1.061, 1.063 |
| `peer-50` | 1.098, 1.111 |
| `peer-75` | 1.206, 1.200 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 1.017, 1.045; order 2: 1.000, 1.000; order 3: 1.054, 1.039; order 4: 1.000, 1.000; order 5: 0.974, 0.952; order 6: 1.026, 1.033. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Lock handoff

The handoff share is the time from a release to the acquire of a waiting thread, divided by the elapsed time. The share counts only handoffs to a thread that waited. See [the handoff plan](../../../lode/plans/target-local-lock-handoff.md) for the hypothesis that these handoffs cause the knee error.

| Point | Median handoff share, profile | Median handoff share, real optimization |
| --- | ---: | ---: |
| `selected-25` | 5.27% | 4.11% |
| `selected-50` | 7.78% | 2.10% |
| `selected-75` | 5.65% | 0.80% |
| `peer-25` | 5.67% | 6.25% |
| `peer-50` | 5.26% | 7.40% |
| `peer-75` | 4.17% | 5.77% |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | 1.10 pp | -8.13 pp | 5.68 pp | 3.96 pp | 0.98 pp | -2.81 pp |
| 2 | Yes | -2.71 pp | -14.01 pp | 2.00 pp | -7.43 pp | -2.43 pp | -0.35 pp |
| 3 | Yes | -6.72 pp | -12.36 pp | 9.63 pp | -0.23 pp | 0.14 pp | -3.60 pp |
| 4 | Yes | 2.20 pp | -1.26 pp | 6.94 pp | 0.19 pp | 1.35 pp | 1.77 pp |
| 5 | Yes | -5.18 pp | -9.20 pp | 6.47 pp | -4.51 pp | -4.87 pp | -2.02 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T062708436-df83442b` | First lock profile run | Fail. `selected-50` median 9.13 points, all five blocks below the actual gain. `peer-25` has one block of 11.87 points. |
| `20261019T070331580-523cc380` | Lock handoff counts | Fail. Blocks 1 and 2 are invalid: `selected` at 50% breaks the 2 ms debt bound. In the three valid blocks, `selected-50` predicts 48.17% against 51.79%. The handoff share at `selected-50` is 7.40% in the profile and 2.18% in the real optimization. |
| `20261019T074433206-6d45795e` | Spin before a lock wait | Fail. `selected-50` median 7.89 points, all five blocks below the actual gain. `selected-75` median 5.07 points, with one block of 10.51 points. |
| `20261019T082429851-cb3998b5` | TargetSemaphore and semaphore profile | Fail. `selected-50` median 5.62 points, `selected-75` median 5.17 points with one block of 12.83 points, and `peer-75` median 5.55 points. |
| `20261019T090501903-273c483d` | Failure and GC records | Fail. Block 4 is invalid: `peer` at 75% records ledger failure 1. In the four valid blocks, `selected-50` median 8.32 points. |
| `20261019T093943719-10b7cd71` | Failure context | Fail. `selected-50` median 6.09 points with one block of -12.53 points. |
| `20261019T110313420-3292dd51` | Latency progress points | Pass |
| `20261019T113602781-8f0b3de1` | Per-transaction virtual latency | Fail. Five points fail. `selected-50` median 7.49 points, `selected-75` median 5.67 points, and the three `peer` points by block 5. |
| `20261019T121327119-d95b4f14` | Transaction queue handoff | Fail. `selected-50` median 6.84 points, `selected-75` one block of 12.51 points, and `peer-25` one block of -15.85 points. |
| `20261019T125115354-73f1edc7` | Spin before a queue wait | Fail. `selected-50` median 6.28 points, `peer-50` median 5.18, `peer-75` median 5.74, and `peer-25` one block of -13.92 points. |
| `20261019T131912794-bfae28ce` | Handoff parts | Fail. Block 1 is invalid: a debt failure after one child payment of 3.35 ms for a request of 50 microseconds. |
| `20261019T150538269-4c383533` | Worker pinning | Fail. Points selected-50, selected-75 fail, or a run-level criterion fails. |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetLock`. CLR safety outside this workload is not established.
