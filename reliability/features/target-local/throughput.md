# Target-local throughput profile

Evidence: `artifacts/target-local-throughput/20261019T150855392-75711219`.

`TargetLocalThroughputVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `throughput-profile` process and seven `plain-throughput` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. The selected worker repeats `Work.selected 40` and the peer repeats `Work.peer 20`, with one visit of the progress point after each call.
The criteria are in [the throughput plan](../../../lode/plans/target-local-throughput.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points peer-25 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 11.20% | 8.49% | 2.71 pp | 6.63 pp | Pass |
| `selected-50` | 5 / 5 | 25.22% | 25.98% | 2.25 pp | 6.01 pp | Pass |
| `selected-75` | 5 / 5 | 49.35% | 49.73% | 0.89 pp | 3.31 pp | Pass |
| `peer-25` | 5 / 5 | 18.71% | 19.68% | 4.13 pp | 10.25 pp | Fail |
| `peer-50` | 5 / 5 | 40.13% | 39.99% | 1.24 pp | 5.35 pp | Pass |
| `peer-75` | 5 / 5 | 66.39% | 66.44% | 1.26 pp | 3.27 pp | Pass |

If both workers have the same cost for each round, the expected gains are 10%, 25%, and 50% for `Work.selected` and 18.2%, 40%, and 66.7% for `Work.peer`. The criteria use the measured gains.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 1.178, 0.894 |
| `selected-50` | 1.465, 0.747 |
| `selected-75` | 1.962, 0.508 |
| `peer-25` | 0.813, 1.082 |
| `peer-50` | 0.599, 1.179 |
| `peer-75` | 0.336, 1.300 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 1.000, 1.000; order 2: 1.000, 1.000; order 3: 0.795, 1.058; order 4: 1.000, 1.000; order 5: 1.242, 0.929; order 6: 1.153, 0.873. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | 0.45 pp | -1.53 pp | -1.66 pp | -10.25 pp | -1.24 pp | -1.44 pp |
| 2 | Yes | 1.40 pp | 2.25 pp | 0.89 pp | 0.18 pp | -0.72 pp | -1.05 pp |
| 3 | Yes | 6.63 pp | 5.47 pp | -0.38 pp | 4.13 pp | 3.03 pp | 0.03 pp |
| 4 | Yes | -5.71 pp | -6.01 pp | -3.31 pp | -5.42 pp | -5.35 pp | -3.27 pp |
| 5 | Yes | 2.71 pp | -0.76 pp | 0.70 pp | -1.19 pp | 0.69 pp | 1.26 pp |

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
| `20261019T132215488-c111dbb5` | Handoff parts | Pass |
| `20261019T150855392-75711219` | Worker pinning | Fail. Points peer-25 fail, or a run-level criterion fails. |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that do not block outside `TargetBarrier`. CLR safety outside this workload is not established.
