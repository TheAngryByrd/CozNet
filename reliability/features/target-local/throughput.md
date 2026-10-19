# Target-local throughput profile

Evidence: `artifacts/target-local-throughput/20261019T040411141-1b17380b`.

`TargetLocalThroughputVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `throughput-profile` process and seven `plain-throughput` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. The selected worker repeats `Work.selected 40` and the peer repeats `Work.peer 20`, with one visit of the progress point after each call.
The criteria are in [the throughput plan](../../../lode/plans/target-local-throughput.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run passes.**

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 10.33% | 8.08% | 3.42 pp | 4.89 pp | Pass |
| `selected-50` | 5 / 5 | 23.88% | 22.82% | 1.62 pp | 3.18 pp | Pass |
| `selected-75` | 5 / 5 | 48.74% | 49.91% | 1.54 pp | 3.52 pp | Pass |
| `peer-25` | 5 / 5 | 18.19% | 16.14% | 3.24 pp | 4.41 pp | Pass |
| `peer-50` | 5 / 5 | 39.07% | 40.53% | 1.40 pp | 2.80 pp | Pass |
| `peer-75` | 5 / 5 | 65.53% | 66.39% | 0.35 pp | 1.79 pp | Pass |

If both workers have the same cost for each round, the expected gains are 10%, 25%, and 50% for `Work.selected` and 18.2%, 40%, and 66.7% for `Work.peer`. The criteria use the measured gains.

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | 3.42 pp | 0.06 pp | -0.41 pp | 3.32 pp | -2.80 pp | -1.15 pp |
| 2 | Yes | 3.63 pp | 1.62 pp | -1.54 pp | 2.42 pp | -1.40 pp | -1.79 pp |
| 3 | Yes | -3.00 pp | -2.70 pp | -1.96 pp | -0.76 pp | -1.58 pp | -0.03 pp |
| 4 | Yes | -0.09 pp | -0.04 pp | -3.52 pp | -4.41 pp | -1.29 pp | 0.16 pp |
| 5 | Yes | 4.89 pp | 3.18 pp | 1.27 pp | 3.24 pp | -0.97 pp | -0.35 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T032027341-60c2ab7c` | First throughput profile run | Pass |
| `20261019T040411141-1b17380b` | Blocking queue and pipeline profile | Pass |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that do not block outside `TargetBarrier`. CLR safety outside this workload is not established.
