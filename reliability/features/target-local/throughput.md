# Target-local throughput profile

Evidence: `artifacts/target-local-throughput/20261019T074738456-b0d0236a`.

`TargetLocalThroughputVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `throughput-profile` process and seven `plain-throughput` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. The selected worker repeats `Work.selected 40` and the peer repeats `Work.peer 20`, with one visit of the progress point after each call.
The criteria are in [the throughput plan](../../../lode/plans/target-local-throughput.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run passes.**

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 9.50% | 9.59% | 1.59 pp | 5.22 pp | Pass |
| `selected-50` | 5 / 5 | 24.27% | 25.52% | 1.62 pp | 2.63 pp | Pass |
| `selected-75` | 5 / 5 | 48.46% | 50.07% | 1.67 pp | 3.13 pp | Pass |
| `peer-25` | 5 / 5 | 17.19% | 17.09% | 1.95 pp | 4.09 pp | Pass |
| `peer-50` | 5 / 5 | 38.64% | 39.92% | 1.21 pp | 1.34 pp | Pass |
| `peer-75` | 5 / 5 | 65.11% | 66.62% | 2.06 pp | 2.84 pp | Pass |

If both workers have the same cost for each round, the expected gains are 10%, 25%, and 50% for `Work.selected` and 18.2%, 40%, and 66.7% for `Work.peer`. The criteria use the measured gains.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 1.196, 0.906 |
| `selected-50` | 1.489, 0.757 |
| `selected-75` | 1.995, 0.516 |
| `peer-25` | 0.828, 1.096 |
| `peer-50` | 0.614, 1.213 |
| `peer-75` | 0.349, 1.353 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 1.000, 1.000; order 2: 1.000, 1.000; order 3: 0.841, 1.120; order 4: 1.000, 1.000; order 5: 1.264, 0.944; order 6: 1.252, 0.942. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | 1.12 pp | -2.63 pp | -0.91 pp | -1.81 pp | -1.28 pp | -2.67 pp |
| 2 | Yes | 2.99 pp | 0.84 pp | -1.67 pp | 2.64 pp | -1.21 pp | -0.55 pp |
| 3 | Yes | -1.59 pp | -1.80 pp | -1.03 pp | -1.20 pp | -1.34 pp | -0.91 pp |
| 4 | Yes | -5.22 pp | -1.16 pp | -2.03 pp | -1.95 pp | -0.23 pp | -2.84 pp |
| 5 | Yes | -0.38 pp | 1.62 pp | -3.13 pp | -4.09 pp | 0.52 pp | -2.06 pp |

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

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that do not block outside `TargetBarrier`. CLR safety outside this workload is not established.
