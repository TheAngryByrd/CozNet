# Target-local throughput profile

Evidence: `artifacts/target-local-throughput/20261019T062905608-e6d24e45`.

`TargetLocalThroughputVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `throughput-profile` process and seven `plain-throughput` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. The selected worker repeats `Work.selected 40` and the peer repeats `Work.peer 20`, with one visit of the progress point after each call.
The criteria are in [the throughput plan](../../../lode/plans/target-local-throughput.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 4 / 5 | 10.51% | 9.43% | 1.25 pp | 4.90 pp | Fail |
| `selected-50` | 4 / 5 | 24.81% | 24.30% | 0.67 pp | 2.17 pp | Fail |
| `selected-75` | 4 / 5 | 48.62% | 50.19% | 1.12 pp | 3.30 pp | Fail |
| `peer-25` | 4 / 5 | 18.37% | 16.43% | 1.94 pp | 4.43 pp | Fail |
| `peer-50` | 4 / 5 | 39.35% | 40.23% | 1.70 pp | 2.38 pp | Fail |
| `peer-75` | 4 / 5 | 65.80% | 66.76% | 0.71 pp | 2.43 pp | Fail |

If both workers have the same cost for each round, the expected gains are 10%, 25%, and 50% for `Work.selected` and 18.2%, 40%, and 66.7% for `Work.peer`. The criteria use the measured gains.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 1.187, 0.893 |
| `selected-50` | 1.487, 0.754 |
| `selected-75` | 1.984, 0.516 |
| `peer-25` | 0.816, 1.086 |
| `peer-50` | 0.607, 1.197 |
| `peer-75` | 0.342, 1.322 |

The median work ratio by run order in the process is order 0: 1.084, 0.942; order 1: 1.000, 1.000; order 2: 1.000, 1.000; order 3: 0.817, 1.084; order 4: 1.105, 0.955; order 5: 1.267, 0.955; order 6: 1.196, 0.903. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | 1.78 pp | -2.17 pp | -3.30 pp | 4.43 pp | 2.38 pp | -2.43 pp |
| 2 | Yes | 4.90 pp | 0.22 pp | -1.37 pp | 1.46 pp | -1.25 pp | 0.15 pp |
| 3 | Yes | -0.71 pp | 0.82 pp | -0.31 pp | 2.42 pp | -1.83 pp | -0.61 pp |
| 4 | No: profile exit 1; profile not passed | -4.59 pp | -19.00 pp | -4.84 pp | 1.41 pp | n/a | n/a |
| 5 | Yes | -0.40 pp | -0.52 pp | 0.87 pp | 1.10 pp | -1.57 pp | -0.82 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T032027341-60c2ab7c` | First throughput profile run | Pass |
| `20261019T040411141-1b17380b` | Blocking queue and pipeline profile | Pass |
| `20261019T043639774-d3af17e6` | Per-thread experiment diagnostics | Pass |
| `20261019T051006922-f2f80c26` | Profile warm-up experiment | Pass |
| `20261019T060526482-e851bced` | Rotated experiment order and warm-up gate | Pass |
| `20261019T062905608-e6d24e45` | TargetLock and lock profile | Fail. Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that do not block outside `TargetBarrier`. CLR safety outside this workload is not established.
