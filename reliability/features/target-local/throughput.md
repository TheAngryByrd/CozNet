# Target-local throughput profile

Evidence: `artifacts/target-local-throughput/20261019T121628106-2be55ac2`.

`TargetLocalThroughputVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `throughput-profile` process and seven `plain-throughput` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. The selected worker repeats `Work.selected 40` and the peer repeats `Work.peer 20`, with one visit of the progress point after each call.
The criteria are in [the throughput plan](../../../lode/plans/target-local-throughput.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run passes.**

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 8.98% | 11.50% | 2.53 pp | 5.59 pp | Pass |
| `selected-50` | 5 / 5 | 25.81% | 26.13% | 0.52 pp | 2.34 pp | Pass |
| `selected-75` | 5 / 5 | 48.45% | 50.36% | 1.99 pp | 3.09 pp | Pass |
| `peer-25` | 5 / 5 | 19.19% | 17.96% | 1.99 pp | 2.87 pp | Pass |
| `peer-50` | 5 / 5 | 40.51% | 40.38% | 0.82 pp | 1.39 pp | Pass |
| `peer-75` | 5 / 5 | 65.86% | 66.65% | 0.75 pp | 2.30 pp | Pass |

If both workers have the same cost for each round, the expected gains are 10%, 25%, and 50% for `Work.selected` and 18.2%, 40%, and 66.7% for `Work.peer`. The criteria use the measured gains.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 1.208, 0.911 |
| `selected-50` | 1.469, 0.744 |
| `selected-75` | 1.966, 0.517 |
| `peer-25` | 0.808, 1.077 |
| `peer-50` | 0.595, 1.182 |
| `peer-75` | 0.342, 1.306 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 1.000, 1.000; order 2: 1.000, 1.000; order 3: 0.806, 1.063; order 4: 1.000, 1.000; order 5: 1.230, 0.927; order 6: 1.216, 0.911. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -2.53 pp | -0.52 pp | -1.99 pp | 1.23 pp | 1.39 pp | -0.22 pp |
| 2 | Yes | 0.36 pp | -0.75 pp | -3.09 pp | -2.87 pp | -1.23 pp | -0.75 pp |
| 3 | Yes | -0.66 pp | -0.33 pp | -1.02 pp | -0.60 pp | 0.82 pp | -1.77 pp |
| 4 | Yes | -5.59 pp | -2.34 pp | -3.01 pp | 1.99 pp | 0.75 pp | -2.30 pp |
| 5 | Yes | 2.91 pp | 0.15 pp | -1.28 pp | 2.04 pp | 0.00 pp | 0.14 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T032027341-60c2ab7c` | First throughput profile run | Pass |
| `20261019T040411141-1b17380b` | Blocking queue and pipeline profile | Pass |
| `20261019T043639774-d3af17e6` | Per-thread experiment diagnostics | Pass |
| `20261019T051006922-f2f80c26` | Profile warm-up experiment | Pass |
| `20261019T060526482-e851bced` | Rotated experiment order and warm-up gate | Pass |
| `20261019T062905608-e6d24e45` | TargetLock and lock profile | Fail. Block 4 invalid: a 2 ms debt failure, and a handler interval of 5.99 ms with 45.8 ms of run-queue delay. |
| `20261019T070536063-c1a60568` | Lock handoff counts | Pass |
| `20261019T074738456-b0d0236a` | Spin before a lock wait | Pass |
| `20261019T082728956-3c510383` | TargetSemaphore and semaphore profile | Pass |
| `20261019T090759533-41316f2b` | Failure and GC records | Pass |
| `20261019T094241716-2243c889` | Failure context | Pass |
| `20261019T110611005-2a35600a` | Latency progress points | Pass |
| `20261019T113911746-669e1783` | Per-transaction virtual latency | Fail. `peer-75` has one block of -10.54 points. |
| `20261019T121628106-2be55ac2` | Transaction queue handoff | Pass |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that do not block outside `TargetBarrier`. CLR safety outside this workload is not established.
