# Target-local throughput profile

Evidence: `artifacts/target-local-throughput/20261019T043639774-d3af17e6`.

`TargetLocalThroughputVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `throughput-profile` process and seven `plain-throughput` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. The selected worker repeats `Work.selected 40` and the peer repeats `Work.peer 20`, with one visit of the progress point after each call.
The criteria are in [the throughput plan](../../../lode/plans/target-local-throughput.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run passes.**

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 8.70% | 9.19% | 0.56 pp | 3.18 pp | Pass |
| `selected-50` | 5 / 5 | 23.85% | 25.43% | 1.59 pp | 8.12 pp | Pass |
| `selected-75` | 5 / 5 | 48.85% | 49.78% | 1.49 pp | 2.73 pp | Pass |
| `peer-25` | 5 / 5 | 19.05% | 19.04% | 0.64 pp | 4.65 pp | Pass |
| `peer-50` | 5 / 5 | 38.50% | 39.81% | 1.74 pp | 3.09 pp | Pass |
| `peer-75` | 5 / 5 | 65.50% | 66.93% | 1.68 pp | 1.78 pp | Pass |

If both workers have the same cost for each round, the expected gains are 10%, 25%, and 50% for `Work.selected` and 18.2%, 40%, and 66.7% for `Work.peer`. The criteria use the measured gains.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 1.208, 0.916 |
| `selected-50` | 1.507, 0.765 |
| `selected-75` | 1.974, 0.513 |
| `peer-25` | 0.809, 1.077 |
| `peer-50` | 0.615, 1.221 |
| `peer-75` | 0.345, 1.328 |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -0.37 pp | -0.31 pp | -1.70 pp | 0.64 pp | -0.15 pp | -1.78 pp |
| 2 | Yes | -3.18 pp | -8.12 pp | -2.73 pp | 4.65 pp | 0.22 pp | -0.46 pp |
| 3 | Yes | 0.56 pp | -3.00 pp | -1.23 pp | 0.01 pp | -2.15 pp | -1.07 pp |
| 4 | Yes | -1.59 pp | -0.73 pp | -1.49 pp | -0.63 pp | -3.09 pp | -1.68 pp |
| 5 | Yes | 0.08 pp | -1.59 pp | -0.86 pp | -2.27 pp | -1.74 pp | -1.78 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T032027341-60c2ab7c` | First throughput profile run | Pass |
| `20261019T040411141-1b17380b` | Blocking queue and pipeline profile | Pass |
| `20261019T043639774-d3af17e6` | Per-thread experiment diagnostics | Pass |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that do not block outside `TargetBarrier`. CLR safety outside this workload is not established.
