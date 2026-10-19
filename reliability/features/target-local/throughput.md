# Target-local throughput profile

Evidence: `artifacts/target-local-throughput/20261019T032027341-60c2ab7c`.

`TargetLocalThroughputVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `throughput-profile` process and seven `plain-throughput` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. The selected worker repeats `Work.selected 40` and the peer repeats `Work.peer 20`, with one visit of the progress point after each call.
The criteria are in [the throughput plan](../../../lode/plans/target-local-throughput.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run passes.**

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 9.10% | 10.82% | 3.09 pp | 4.07 pp | Pass |
| `selected-50` | 5 / 5 | 23.83% | 25.28% | 1.45 pp | 3.79 pp | Pass |
| `selected-75` | 5 / 5 | 48.50% | 49.68% | 1.87 pp | 3.22 pp | Pass |
| `peer-25` | 5 / 5 | 17.22% | 18.68% | 1.78 pp | 3.15 pp | Pass |
| `peer-50` | 5 / 5 | 38.41% | 39.97% | 1.57 pp | 5.26 pp | Pass |
| `peer-75` | 5 / 5 | 65.44% | 66.58% | 0.76 pp | 2.40 pp | Pass |

If both workers have the same cost for each round, the expected gains are 10%, 25%, and 50% for `Work.selected` and 18.2%, 40%, and 66.7% for `Work.peer`. The criteria use the measured gains.

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -4.07 pp | -1.87 pp | -2.62 pp | -3.13 pp | -5.26 pp | -2.23 pp |
| 2 | Yes | 2.22 pp | -1.24 pp | -0.61 pp | 1.14 pp | 0.64 pp | -0.08 pp |
| 3 | Yes | -1.60 pp | -3.79 pp | -1.18 pp | -0.71 pp | -1.57 pp | -0.76 pp |
| 4 | Yes | -3.09 pp | -1.45 pp | -3.22 pp | -1.78 pp | -3.51 pp | -0.60 pp |
| 5 | Yes | -3.38 pp | -1.05 pp | -1.87 pp | -3.15 pp | -0.89 pp | -2.40 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T032027341-60c2ab7c` | First throughput profile run | Pass |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that do not block outside `TargetBarrier`. CLR safety outside this workload is not established.
