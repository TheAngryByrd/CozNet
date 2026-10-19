# Target-local lock profile

Evidence: `artifacts/target-local-lock/20261019T090501903-273c483d`.

`TargetLocalLockVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `lock-profile` process and seven `plain-lock` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. Two workers each repeat `Work.peer 20` outside a `TargetLock`, then `Work.selected 40` inside it, and then visit the progress point.
The criteria are in [the lock plan](../../../lode/plans/target-local-lock.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 4 / 5 | 23.54% | 27.32% | 3.83 pp | 6.27 pp | Fail |
| `selected-50` | 4 / 5 | 43.80% | 51.33% | 8.32 pp | 8.71 pp | Fail |
| `selected-75` | 4 / 5 | 68.40% | 64.89% | 4.69 pp | 8.47 pp | Fail |
| `peer-25` | 4 / 5 | 1.21% | 1.28% | 4.90 pp | 5.90 pp | Fail |
| `peer-50` | 4 / 5 | 0.49% | 2.01% | 2.47 pp | 8.83 pp | Fail |
| `peer-75` | 4 / 5 | 0.04% | 1.30% | 4.34 pp | 9.28 pp | Fail |

The lock sets the rate, so the model gives `Work.selected` 25%, 50%, and 62.5%, and `Work.peer` 0% at each speedup. At 75%, the work outside the lock sets the rate.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.937, 0.945 |
| `selected-50` | 0.930, 0.911 |
| `selected-75` | 0.967, 0.975 |
| `peer-25` | 1.048, 1.046 |
| `peer-50` | 1.101, 1.101 |
| `peer-75` | 1.174, 1.197 |

The median work ratio by run order in the process is order 0: 0.985, 0.984; order 1: 1.000, 1.000; order 2: 1.000, 1.000; order 3: 1.000, 1.000; order 4: 1.000, 1.000; order 5: 1.028, 0.986; order 6: 0.986, 0.985. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Lock handoff

The handoff share is the time from a release to the acquire of a waiting thread, divided by the elapsed time. The share counts only handoffs to a thread that waited. See [the handoff plan](../../../lode/plans/target-local-lock-handoff.md) for the hypothesis that these handoffs cause the knee error.

| Point | Median handoff share, profile | Median handoff share, real optimization |
| --- | ---: | ---: |
| `selected-25` | 5.42% | 4.35% |
| `selected-50` | 7.95% | 1.30% |
| `selected-75` | 6.32% | 0.77% |
| `peer-25` | 5.25% | 5.92% |
| `peer-50` | 4.71% | 6.07% |
| `peer-75` | 4.53% | 5.73% |

## Failed experiments

Each row is a ledger failure of an experiment that did not pass, or the error of a failed experiment without a ledger failure. The time counts from the first journal event with a time. The amount is the debt for failure 1 and 0 for other failures. The gap is the time from the previous journal event of the same worker, in sequence order, and a buffered sample can make it negative. A failure without a time shows n/a. The collection count and the GC pause cover all threads of the process during the experiment.

| Block | Experiment | Worker | Failure | Amount | Time | Gap | Collections | GC pause |
| ---: | --- | ---: | ---: | ---: | ---: | --- | ---: | ---: |
| 4 | `peer` 75% | 1 | 1 | 3.000 ms | 56.871 ms | 0.007 ms after `Sample` | 0 | 0.000 ms |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -2.81 pp | -8.46 pp | 3.38 pp | -5.84 pp | -2.79 pp | -5.12 pp |
| 2 | Yes | -6.27 pp | -8.71 pp | 5.75 pp | -3.97 pp | -1.31 pp | -3.56 pp |
| 3 | Yes | 3.71 pp | -1.67 pp | 3.62 pp | 5.90 pp | 8.83 pp | 9.28 pp |
| 4 | No: profile exit 1; profile not passed | 0.22 pp | -9.20 pp | -3.92 pp | 0.19 pp | 2.03 pp | n/a |
| 5 | Yes | -3.95 pp | -8.18 pp | -8.47 pp | 3.93 pp | -2.15 pp | -0.68 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T062708436-df83442b` | First lock profile run | Fail. `selected-50` median 9.13 points, all five blocks below the actual gain. `peer-25` has one block of 11.87 points. |
| `20261019T070331580-523cc380` | Lock handoff counts | Fail. Blocks 1 and 2 are invalid: `selected` at 50% breaks the 2 ms debt bound. In the three valid blocks, `selected-50` predicts 48.17% against 51.79%. The handoff share at `selected-50` is 7.40% in the profile and 2.18% in the real optimization. |
| `20261019T074433206-6d45795e` | Spin before a lock wait | Fail. `selected-50` median 7.89 points, all five blocks below the actual gain. `selected-75` median 5.07 points, with one block of 10.51 points. |
| `20261019T082429851-cb3998b5` | TargetSemaphore and semaphore profile | Fail. `selected-50` median 5.62 points, `selected-75` median 5.17 points with one block of 12.83 points, and `peer-75` median 5.55 points. |
| `20261019T090501903-273c483d` | Failure and GC records | Fail. Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetLock`. CLR safety outside this workload is not established.
