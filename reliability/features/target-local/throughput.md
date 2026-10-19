# Target-local throughput profile

Evidence: `artifacts/target-local-throughput/20261019T070536063-c1a60568`.

`TargetLocalThroughputVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `throughput-profile` process and seven `plain-throughput` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. The selected worker repeats `Work.selected 40` and the peer repeats `Work.peer 20`, with one visit of the progress point after each call.
The criteria are in [the throughput plan](../../../lode/plans/target-local-throughput.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run passes.**

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 10.38% | 8.72% | 0.70 pp | 5.94 pp | Pass |
| `selected-50` | 5 / 5 | 24.69% | 25.55% | 1.11 pp | 5.59 pp | Pass |
| `selected-75` | 5 / 5 | 48.23% | 50.60% | 2.73 pp | 4.72 pp | Pass |
| `peer-25` | 5 / 5 | 19.09% | 18.64% | 0.98 pp | 3.84 pp | Pass |
| `peer-50` | 5 / 5 | 38.96% | 39.07% | 1.42 pp | 4.10 pp | Pass |
| `peer-75` | 5 / 5 | 65.70% | 66.97% | 1.87 pp | 4.08 pp | Pass |

If both workers have the same cost for each round, the expected gains are 10%, 25%, and 50% for `Work.selected` and 18.2%, 40%, and 66.7% for `Work.peer`. The criteria use the measured gains.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 1.196, 0.896 |
| `selected-50` | 1.497, 0.752 |
| `selected-75` | 2.012, 0.517 |
| `peer-25` | 0.812, 1.073 |
| `peer-50` | 0.612, 1.207 |
| `peer-75` | 0.343, 1.331 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 1.000, 1.000; order 2: 1.000, 1.000; order 3: 0.798, 1.062; order 4: 1.000, 1.000; order 5: 1.214, 0.914; order 6: 1.180, 0.896. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -4.42 pp | -2.23 pp | -2.73 pp | -2.91 pp | -4.10 pp | -1.87 pp |
| 2 | Yes | 5.94 pp | 0.70 pp | -1.42 pp | 3.84 pp | 0.28 pp | -1.24 pp |
| 3 | Yes | -0.40 pp | -0.64 pp | -2.20 pp | 0.05 pp | 1.42 pp | -1.05 pp |
| 4 | Yes | -0.00 pp | -1.11 pp | -3.40 pp | -0.29 pp | -0.06 pp | -3.53 pp |
| 5 | Yes | -0.70 pp | -5.59 pp | -4.72 pp | -0.98 pp | -1.76 pp | -4.08 pp |

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

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that do not block outside `TargetBarrier`. CLR safety outside this workload is not established.
