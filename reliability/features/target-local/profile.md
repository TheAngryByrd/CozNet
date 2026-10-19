# Target-local causal profile

Evidence: `artifacts/target-local-profile/20261019T043738634-485c3ea9`.

`TargetLocalProfileVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `causal-profile` process and seven `plain` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. The selected thread runs 6000 rounds and the peer 3000. The progress point is the end of the workload.
The criteria are in [the profile plan](../../../lode/plans/target-local-causal-profile.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points peer-50, peer-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 23.06% | 23.55% | 1.60 pp | 5.91 pp | Pass |
| `selected-50` | 5 / 5 | 47.86% | 50.57% | 2.71 pp | 5.40 pp | Pass |
| `selected-75` | 5 / 5 | 49.17% | 50.60% | 1.63 pp | 5.44 pp | Pass |
| `peer-25` | 5 / 5 | -0.48% | -0.71% | 1.96 pp | 5.21 pp | Pass |
| `peer-50` | 5 / 5 | -0.07% | -0.57% | 5.52 pp | 10.91 pp | Fail |
| `peer-75` | 5 / 5 | -3.19% | -0.17% | 3.83 pp | 12.72 pp | Fail |

The fork/join model gives `Work.selected` 25%, 50%, and 50%, and `Work.peer` 0% at each speedup. The plateau at 75% comes from the peer, which then sets the end time.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 1.019, 1.021 |
| `selected-50` | 1.025, 1.038 |
| `selected-75` | 1.008, 1.020 |
| `peer-25` | 1.005, 1.020 |
| `peer-50` | 1.001, 1.021 |
| `peer-75` | 1.032, 1.014 |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | 0.84 pp | 0.45 pp | -1.67 pp | -1.20 pp | 1.61 pp | -6.74 pp |
| 2 | Yes | -5.91 pp | -5.40 pp | -5.44 pp | -5.21 pp | -9.11 pp | -12.72 pp |
| 3 | Yes | 2.43 pp | -0.45 pp | -1.63 pp | 1.15 pp | 5.52 pp | -0.43 pp |
| 4 | Yes | -1.60 pp | -3.33 pp | -0.62 pp | 1.96 pp | 10.91 pp | 3.83 pp |
| 5 | Yes | 1.38 pp | -2.71 pp | 0.59 pp | -3.93 pp | -3.01 pp | 0.40 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T024109669-c30228ca` | First causal profile run | Pass |
| `20261019T032133781-6cc15ea3` | Throughput progress points | Fail. `selected-75` and `peer-75` each have one block above 10 points, both in block 1, where the plain arms give 36.0% and 4.5%. |
| `20261019T040508680-38464c4a` | Blocking queue and pipeline profile | Fail. Block 3 is invalid: its `selected` experiment at 75% breaks the 2 ms debt bound. |
| `20261019T043738634-485c3ea9` | Per-thread experiment diagnostics | Fail. Points peer-50, peer-75 fail, or a run-level criterion fails. |

## Limits

The progress point is the end of the workload.
The profile covers CPU-bound threads on Linux x64 that do not block outside `TargetBarrier`. CLR safety outside this workload is not established.
