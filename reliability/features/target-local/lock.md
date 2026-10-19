# Target-local lock profile

Evidence: `artifacts/target-local-lock/20261019T131912794-bfae28ce`.

`TargetLocalLockVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `lock-profile` process and seven `plain-lock` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. Two workers each repeat `Work.peer 20` outside a `TargetLock`, then `Work.selected 40` inside it, and then visit the progress point.
The criteria are in [the lock plan](../../../lode/plans/target-local-lock.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 4 / 5 | 24.66% | 25.87% | 2.81 pp | 7.41 pp | Fail |
| `selected-50` | 4 / 5 | 43.96% | 51.62% | 6.91 pp | 8.39 pp | Fail |
| `selected-75` | 4 / 5 | 71.44% | 64.35% | 7.69 pp | 10.05 pp | Fail |
| `peer-25` | 4 / 5 | 0.28% | 0.89% | 1.20 pp | 2.73 pp | Fail |
| `peer-50` | 4 / 5 | 0.22% | 0.72% | 1.38 pp | 3.83 pp | Fail |
| `peer-75` | 4 / 5 | 1.46% | 1.80% | 2.55 pp | 4.28 pp | Fail |

The lock sets the rate, so the model gives `Work.selected` 25%, 50%, and 62.5%, and `Work.peer` 0% at each speedup. At 75%, the work outside the lock sets the rate.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.944, 0.934 |
| `selected-50` | 0.939, 0.929 |
| `selected-75` | 0.957, 0.936 |
| `peer-25` | 1.055, 1.044 |
| `peer-50` | 1.115, 1.090 |
| `peer-75` | 1.153, 1.191 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 1.000, 1.000; order 2: 1.000, 1.000; order 3: 1.014, 1.038; order 4: 1.000, 1.000; order 5: 0.961, 0.949; order 6: 1.018, 1.003. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Lock handoff

The handoff share is the time from a release to the acquire of a waiting thread, divided by the elapsed time. The share counts only handoffs to a thread that waited. See [the handoff plan](../../../lode/plans/target-local-lock-handoff.md) for the hypothesis that these handoffs cause the knee error.

| Point | Median handoff share, profile | Median handoff share, real optimization |
| --- | ---: | ---: |
| `selected-25` | 5.41% | 4.43% |
| `selected-50` | 8.09% | 1.30% |
| `selected-75` | 5.77% | 0.93% |
| `peer-25` | 5.74% | 6.19% |
| `peer-50` | 5.08% | 5.86% |
| `peer-75` | 4.53% | 5.52% |

## Failed experiments

Each row is a ledger failure of an experiment that did not pass, or the error of a failed experiment without a ledger failure. The time counts from the first journal event with a time. The amount is the debt for failure 1 and 0 for other failures. The gap is the time from the previous journal event of the same worker, in sequence order, and a buffered sample can make it negative. A failure without a time shows n/a. The collection count and the GC pause cover all threads of the process during the experiment.

| Block | Experiment | Worker | Failure | Amount | Time | Gap | Collections | GC pause |
| ---: | --- | ---: | ---: | ---: | ---: | --- | ---: | ---: |
| 1 | `selected` 50% | 0 | 1 | 2.452 ms | 20.234 ms | 0.004 ms after `Sample` | 0 | 0.000 ms |

## Failure context

For each failure, each row summarizes one worker over the journal events that come before the failure in sequence and no more than 10 ms before it in time. The record also keeps the 64 latest of these events. Child payments show their total measured time for their total requested time over the whole window, which can cover several payments. The overshoot is the largest measured minus requested time of one completed child. A failed child wait is counted, but it is not ranked. The ledger records a publish only when it raises the global delay, from the observed value to the local delay of its worker. Offsets count from the failure. An event of another worker can have a positive offset, because it takes its time before its sequence.

| Block | Experiment | Failing worker | Window events | Worker | Events | Children | Child time | Largest overshoot | Publishes | Last publish |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | --- | --- | ---: | --- |
| 1 | `selected` 50% | 0 | 86 | 0 | 37 | 9 (0 failed) | 0.806 ms for 0.329 ms | 0.452 ms at -9.313 ms | 7 | 10.010 ms over 9.510 ms at -3.972 ms |
| 1 | `selected` 50% | 0 | 86 | 1 | 49 | 25 (0 failed) | 4.462 ms for 1.160 ms | 3.299 ms at -0.372 ms | 8 | 12.962 ms over 10.010 ms at -0.371 ms |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | No: profile exit 1; profile not passed | -2.58 pp | n/a | 1.64 pp | 0.46 pp | -4.50 pp | -6.17 pp |
| 2 | Yes | -7.41 pp | -5.62 pp | 7.90 pp | 1.20 pp | 1.32 pp | -4.28 pp |
| 3 | Yes | -1.79 pp | -5.26 pp | 1.43 pp | -1.21 pp | -1.35 pp | -1.50 pp |
| 4 | Yes | -0.18 pp | -8.39 pp | 10.05 pp | -2.73 pp | -1.41 pp | -1.74 pp |
| 5 | Yes | 3.82 pp | -8.20 pp | 7.47 pp | 0.74 pp | 3.83 pp | 3.37 pp |

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
| `20261019T131912794-bfae28ce` | Handoff parts | Fail. Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetLock`. CLR safety outside this workload is not established.
