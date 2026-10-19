# Target-local throughput profile

Evidence: `artifacts/target-local-throughput/20261019T125416608-a58c20e0`.

`TargetLocalThroughputVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `throughput-profile` process and seven `plain-throughput` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. The selected worker repeats `Work.selected 40` and the peer repeats `Work.peer 20`, with one visit of the progress point after each call.
The criteria are in [the throughput plan](../../../lode/plans/target-local-throughput.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run passes.**

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 8.32% | 10.19% | 1.01 pp | 4.02 pp | Pass |
| `selected-50` | 5 / 5 | 23.98% | 24.43% | 0.86 pp | 4.04 pp | Pass |
| `selected-75` | 5 / 5 | 48.05% | 49.26% | 2.65 pp | 3.82 pp | Pass |
| `peer-25` | 5 / 5 | 16.79% | 17.86% | 0.75 pp | 5.37 pp | Pass |
| `peer-50` | 5 / 5 | 38.73% | 40.79% | 1.82 pp | 2.31 pp | Pass |
| `peer-75` | 5 / 5 | 64.78% | 65.50% | 0.22 pp | 3.01 pp | Pass |

If both workers have the same cost for each round, the expected gains are 10%, 25%, and 50% for `Work.selected` and 18.2%, 40%, and 66.7% for `Work.peer`. The criteria use the measured gains.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 1.225, 0.904 |
| `selected-50` | 1.507, 0.762 |
| `selected-75` | 2.021, 0.519 |
| `peer-25` | 0.832, 1.107 |
| `peer-50` | 0.613, 1.208 |
| `peer-75` | 0.352, 1.353 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 1.000, 1.000; order 2: 1.000, 1.000; order 3: 0.846, 1.117; order 4: 1.000, 1.000; order 5: 1.235, 0.927; order 6: 1.234, 0.930. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -0.86 pp | 0.16 pp | -2.65 pp | -5.37 pp | -2.31 pp | -0.12 pp |
| 2 | Yes | -1.01 pp | 4.04 pp | -3.82 pp | 0.68 pp | -1.82 pp | -3.01 pp |
| 3 | Yes | -2.86 pp | -0.86 pp | -2.72 pp | -2.41 pp | -1.67 pp | -1.54 pp |
| 4 | Yes | -4.02 pp | -0.62 pp | -0.67 pp | -0.22 pp | -2.30 pp | -0.22 pp |
| 5 | Yes | -0.50 pp | -3.90 pp | -1.11 pp | -0.75 pp | -1.54 pp | -0.21 pp |

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
| `20261019T125416608-a58c20e0` | Spin before a queue wait | Pass |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that do not block outside `TargetBarrier`. CLR safety outside this workload is not established.
