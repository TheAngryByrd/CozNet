# Target-local throughput profile

Evidence: `artifacts/target-local-throughput/20261019T110611005-2a35600a`.

`TargetLocalThroughputVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `throughput-profile` process and seven `plain-throughput` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. The selected worker repeats `Work.selected 40` and the peer repeats `Work.peer 20`, with one visit of the progress point after each call.
The criteria are in [the throughput plan](../../../lode/plans/target-local-throughput.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run passes.**

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 8.38% | 9.16% | 2.85 pp | 3.87 pp | Pass |
| `selected-50` | 5 / 5 | 23.17% | 23.91% | 0.64 pp | 5.31 pp | Pass |
| `selected-75` | 5 / 5 | 48.29% | 50.21% | 1.89 pp | 4.10 pp | Pass |
| `peer-25` | 5 / 5 | 16.77% | 18.77% | 2.91 pp | 3.54 pp | Pass |
| `peer-50` | 5 / 5 | 38.72% | 40.11% | 1.33 pp | 2.95 pp | Pass |
| `peer-75` | 5 / 5 | 65.01% | 66.33% | 1.89 pp | 2.61 pp | Pass |

If both workers have the same cost for each round, the expected gains are 10%, 25%, and 50% for `Work.selected` and 18.2%, 40%, and 66.7% for `Work.peer`. The criteria use the measured gains.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 1.215, 0.918 |
| `selected-50` | 1.522, 0.768 |
| `selected-75` | 1.978, 0.519 |
| `peer-25` | 0.832, 1.105 |
| `peer-50` | 0.613, 1.212 |
| `peer-75` | 0.350, 1.359 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 1.000, 1.000; order 2: 1.000, 1.000; order 3: 0.842, 1.120; order 4: 1.000, 1.000; order 5: 1.255, 0.938; order 6: 1.214, 0.915. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -3.87 pp | 0.05 pp | -1.41 pp | -0.65 pp | -1.17 pp | -2.61 pp |
| 2 | Yes | -0.87 pp | 0.64 pp | -3.45 pp | -3.05 pp | -1.33 pp | -2.48 pp |
| 3 | Yes | -0.83 pp | -5.31 pp | -1.89 pp | 0.72 pp | -1.77 pp | -1.26 pp |
| 4 | Yes | -2.85 pp | -0.74 pp | -4.10 pp | -3.54 pp | -2.95 pp | -0.55 pp |
| 5 | Yes | 3.41 pp | 0.57 pp | -1.18 pp | 2.91 pp | -0.82 pp | -1.89 pp |

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

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that do not block outside `TargetBarrier`. CLR safety outside this workload is not established.
