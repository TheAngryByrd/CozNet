# Target-local lock profile

Evidence: `artifacts/target-local-lock/20261019T175822524-6025b0d2`.

`TargetLocalLockVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `lock-profile` process and seven `plain-lock` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. Two workers each repeat `Work.peer 20` outside a `TargetLock`, then `Work.selected 40` inside it, and then visit the progress point.
The criteria are in [the lock plan](../../../lode/plans/target-local-lock.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-25, selected-50, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 24.88% | 27.65% | 4.23 pp | 12.07 pp | Fail |
| `selected-50` | 5 / 5 | 47.01% | 51.86% | 5.86 pp | 14.86 pp | Fail |
| `selected-75` | 5 / 5 | 64.48% | 65.18% | 3.68 pp | 8.60 pp | Pass |
| `peer-25` | 5 / 5 | -1.25% | 1.73% | 11.28 pp | 11.60 pp | Fail |
| `peer-50` | 5 / 5 | 1.98% | 1.50% | 5.81 pp | 8.15 pp | Fail |
| `peer-75` | 5 / 5 | -0.30% | 1.44% | 5.90 pp | 9.77 pp | Fail |

The lock sets the rate, so the model gives `Work.selected` 25%, 50%, and 62.5%, and `Work.peer` 0% at each speedup. At 75%, the work outside the lock sets the rate.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.925, 0.911 |
| `selected-50` | 0.909, 0.902 |
| `selected-75` | 0.955, 0.966 |
| `peer-25` | 1.058, 1.053 |
| `peer-50` | 1.084, 1.070 |
| `peer-75` | 1.169, 1.142 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 1.025, 1.023; order 2: 1.000, 1.025; order 3: 1.058, 1.053; order 4: 1.000, 1.000; order 5: 1.052, 1.070; order 6: 1.026, 1.007. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Lock handoff

The handoff share is the time from a release to the acquire of a waiting thread, divided by the elapsed time. The share counts only handoffs to a thread that waited. See [the handoff plan](../../../lode/plans/target-local-lock-handoff.md) for the hypothesis that these handoffs cause the knee error.

| Point | Median handoff share, profile | Median handoff share, real optimization |
| --- | ---: | ---: |
| `selected-25` | 5.09% | 5.40% |
| `selected-50` | 7.80% | 0.91% |
| `selected-75` | 6.54% | 0.38% |
| `peer-25` | 5.40% | 5.20% |
| `peer-50` | 4.59% | 5.59% |
| `peer-75` | 5.01% | 5.49% |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -10.82 pp | -14.86 pp | -8.60 pp | -11.60 pp | -7.51 pp | -5.90 pp |
| 2 | Yes | 2.73 pp | -1.97 pp | -7.93 pp | 2.47 pp | 5.81 pp | 0.92 pp |
| 3 | Yes | -4.23 pp | -1.14 pp | -3.68 pp | -11.41 pp | -8.15 pp | -9.77 pp |
| 4 | Yes | -12.07 pp | -5.86 pp | 1.06 pp | -11.28 pp | 2.11 pp | -0.93 pp |
| 5 | Yes | 0.24 pp | -6.33 pp | 0.06 pp | 2.82 pp | 4.07 pp | -8.48 pp |

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
| `20261019T150538269-4c383533` | Worker pinning | Fail. `selected-50` median 9.20 points and `selected-75` median 6.47 points. |
| `20261019T175822524-6025b0d2` | Matched plain warm-up | Fail. Points selected-25, selected-50, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetLock`. CLR safety outside this workload is not established.
