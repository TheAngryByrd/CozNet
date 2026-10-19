# Target-local throughput profile

Evidence: `artifacts/target-local-throughput/20261019T051006922-f2f80c26`.

`TargetLocalThroughputVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `throughput-profile` process and seven `plain-throughput` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. The selected worker repeats `Work.selected 40` and the peer repeats `Work.peer 20`, with one visit of the progress point after each call.
The criteria are in [the throughput plan](../../../lode/plans/target-local-throughput.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run passes.**

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 8.34% | 9.16% | 1.97 pp | 3.04 pp | Pass |
| `selected-50` | 5 / 5 | 23.64% | 23.25% | 1.40 pp | 3.01 pp | Pass |
| `selected-75` | 5 / 5 | 48.01% | 49.79% | 1.78 pp | 3.01 pp | Pass |
| `peer-25` | 5 / 5 | 17.51% | 18.03% | 0.88 pp | 1.77 pp | Pass |
| `peer-50` | 5 / 5 | 38.09% | 38.07% | 0.47 pp | 4.38 pp | Pass |
| `peer-75` | 5 / 5 | 64.40% | 66.58% | 1.37 pp | 2.61 pp | Pass |

If both workers have the same cost for each round, the expected gains are 10%, 25%, and 50% for `Work.selected` and 18.2%, 40%, and 66.7% for `Work.peer`. The criteria use the measured gains.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 1.219, 0.914 |
| `selected-50` | 1.506, 0.767 |
| `selected-75` | 1.996, 0.520 |
| `peer-25` | 0.825, 1.094 |
| `peer-50` | 0.619, 1.224 |
| `peer-75` | 0.356, 1.354 |

The median work ratio by run order in the process is order 0: 0.616, 1.233; order 1: 1.542, 0.782; order 2: 0.837, 1.060; order 3: 1.000, 1.000; order 4: 1.503, 0.767; order 5: 1.219, 0.915; order 6: 1.000, 1.000. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -1.75 pp | 0.34 pp | -0.71 pp | 0.89 pp | -0.47 pp | -1.37 pp |
| 2 | Yes | -2.70 pp | -2.98 pp | -2.17 pp | 0.36 pp | 0.15 pp | 0.06 pp |
| 3 | Yes | 0.95 pp | -1.40 pp | -1.78 pp | -1.77 pp | 0.42 pp | -2.61 pp |
| 4 | Yes | 1.97 pp | 3.01 pp | 0.31 pp | 0.06 pp | 0.91 pp | -0.74 pp |
| 5 | Yes | -3.04 pp | -0.71 pp | -3.01 pp | -0.88 pp | -4.38 pp | -2.52 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T032027341-60c2ab7c` | First throughput profile run | Pass |
| `20261019T040411141-1b17380b` | Blocking queue and pipeline profile | Pass |
| `20261019T043639774-d3af17e6` | Per-thread experiment diagnostics | Pass |
| `20261019T051006922-f2f80c26` | Profile warm-up experiment | Pass |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that do not block outside `TargetBarrier`. CLR safety outside this workload is not established.
