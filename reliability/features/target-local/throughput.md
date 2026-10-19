# Target-local throughput profile

Evidence: `artifacts/target-local-throughput/20261019T060526482-e851bced`.

`TargetLocalThroughputVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `throughput-profile` process and seven `plain-throughput` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. The selected worker repeats `Work.selected 40` and the peer repeats `Work.peer 20`, with one visit of the progress point after each call.
The criteria are in [the throughput plan](../../../lode/plans/target-local-throughput.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run passes.**

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 7.82% | 10.65% | 3.53 pp | 4.62 pp | Pass |
| `selected-50` | 5 / 5 | 24.24% | 24.77% | 2.13 pp | 5.34 pp | Pass |
| `selected-75` | 5 / 5 | 47.00% | 49.68% | 2.22 pp | 3.71 pp | Pass |
| `peer-25` | 5 / 5 | 17.26% | 17.53% | 3.35 pp | 4.00 pp | Pass |
| `peer-50` | 5 / 5 | 40.33% | 39.78% | 2.09 pp | 4.09 pp | Pass |
| `peer-75` | 5 / 5 | 65.06% | 66.45% | 1.71 pp | 2.41 pp | Pass |

If both workers have the same cost for each round, the expected gains are 10%, 25%, and 50% for `Work.selected` and 18.2%, 40%, and 66.7% for `Work.peer`. The criteria use the measured gains.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 1.222, 0.921 |
| `selected-50` | 1.493, 0.761 |
| `selected-75` | 2.052, 0.529 |
| `peer-25` | 0.827, 1.098 |
| `peer-50` | 0.598, 1.186 |
| `peer-75` | 0.349, 1.358 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 1.000, 1.000; order 2: 1.000, 1.000; order 3: 0.827, 1.098; order 4: 1.000, 1.000; order 5: 1.249, 0.934; order 6: 1.237, 0.929. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | 0.80 pp | 2.13 pp | -3.21 pp | -3.51 pp | 0.56 pp | -2.36 pp |
| 2 | Yes | -3.53 pp | 0.97 pp | -0.79 pp | 0.42 pp | -2.09 pp | -1.71 pp |
| 3 | Yes | -4.62 pp | -5.34 pp | -3.71 pp | -1.10 pp | -4.09 pp | -1.32 pp |
| 4 | Yes | -4.54 pp | -4.37 pp | -2.22 pp | -3.35 pp | -1.98 pp | -2.41 pp |
| 5 | Yes | 1.51 pp | 0.15 pp | -0.61 pp | 4.00 pp | 3.72 pp | 1.36 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T032027341-60c2ab7c` | First throughput profile run | Pass |
| `20261019T040411141-1b17380b` | Blocking queue and pipeline profile | Pass |
| `20261019T043639774-d3af17e6` | Per-thread experiment diagnostics | Pass |
| `20261019T051006922-f2f80c26` | Profile warm-up experiment | Pass |
| `20261019T060526482-e851bced` | Rotated experiment order and warm-up gate | Pass |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that do not block outside `TargetBarrier`. CLR safety outside this workload is not established.
