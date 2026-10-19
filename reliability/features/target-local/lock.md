# Target-local lock profile

Evidence: `artifacts/target-local-lock/20261019T113602781-8f0b3de1`.

`TargetLocalLockVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `lock-profile` process and seven `plain-lock` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. Two workers each repeat `Work.peer 20` outside a `TargetLock`, then `Work.selected 40` inside it, and then visit the progress point.
The criteria are in [the lock plan](../../../lode/plans/target-local-lock.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 23.89% | 26.72% | 3.27 pp | 6.62 pp | Pass |
| `selected-50` | 5 / 5 | 45.07% | 52.25% | 7.49 pp | 12.95 pp | Fail |
| `selected-75` | 5 / 5 | 66.05% | 64.90% | 5.67 pp | 6.68 pp | Fail |
| `peer-25` | 5 / 5 | -0.53% | -0.36% | 3.36 pp | 15.19 pp | Fail |
| `peer-50` | 5 / 5 | -1.85% | 1.17% | 4.98 pp | 17.92 pp | Fail |
| `peer-75` | 5 / 5 | -1.36% | -0.50% | 3.85 pp | 18.82 pp | Fail |

The lock sets the rate, so the model gives `Work.selected` 25%, 50%, and 62.5%, and `Work.peer` 0% at each speedup. At 75%, the work outside the lock sets the rate.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.966, 0.949 |
| `selected-50` | 0.930, 0.940 |
| `selected-75` | 0.981, 0.963 |
| `peer-25` | 1.055, 1.059 |
| `peer-50` | 1.107, 1.113 |
| `peer-75` | 1.186, 1.170 |

The median work ratio by run order in the process is order 0: 1.010, 1.007; order 1: 1.000, 1.000; order 2: 1.000, 1.000; order 3: 1.042, 1.027; order 4: 1.000, 1.000; order 5: 0.999, 0.985; order 6: 0.989, 0.992. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Lock handoff

The handoff share is the time from a release to the acquire of a waiting thread, divided by the elapsed time. The share counts only handoffs to a thread that waited. See [the handoff plan](../../../lode/plans/target-local-lock-handoff.md) for the hypothesis that these handoffs cause the knee error.

| Point | Median handoff share, profile | Median handoff share, real optimization |
| --- | ---: | ---: |
| `selected-25` | 5.23% | 4.80% |
| `selected-50` | 7.65% | 1.26% |
| `selected-75` | 5.76% | 0.76% |
| `peer-25` | 5.18% | 5.87% |
| `peer-50` | 4.68% | 5.67% |
| `peer-75` | 4.10% | 5.77% |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | 6.62 pp | -6.38 pp | 4.76 pp | 3.65 pp | 4.80 pp | 5.67 pp |
| 2 | Yes | -2.86 pp | -7.20 pp | 6.68 pp | 2.19 pp | -3.01 pp | -3.05 pp |
| 3 | Yes | -5.57 pp | -9.14 pp | -4.28 pp | -1.15 pp | -5.23 pp | -3.08 pp |
| 4 | Yes | -3.22 pp | -7.49 pp | 5.72 pp | -3.36 pp | -4.98 pp | 3.85 pp |
| 5 | Yes | -3.27 pp | -12.95 pp | -5.67 pp | -15.19 pp | -17.92 pp | -18.82 pp |

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
| `20261019T113602781-8f0b3de1` | Per-transaction virtual latency | Fail. Points selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetLock`. CLR safety outside this workload is not established.
