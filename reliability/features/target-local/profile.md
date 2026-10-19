# Target-local causal profile

Evidence: `artifacts/target-local-profile/20261019T032133781-6cc15ea3`.

`TargetLocalProfileVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `causal-profile` process and seven `plain` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. The selected thread runs 6000 rounds and the peer 3000. The progress point is the end of the workload.
The criteria are in [the profile plan](../../../lode/plans/target-local-causal-profile.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-75, peer-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 23.48% | 25.96% | 3.79 pp | 5.96 pp | Pass |
| `selected-50` | 5 / 5 | 48.58% | 49.65% | 1.27 pp | 7.98 pp | Pass |
| `selected-75` | 5 / 5 | 48.83% | 49.80% | 1.05 pp | 11.41 pp | Fail |
| `peer-25` | 5 / 5 | -0.33% | -0.73% | 1.74 pp | 5.85 pp | Pass |
| `peer-50` | 5 / 5 | -0.25% | -0.77% | 3.60 pp | 5.87 pp | Pass |
| `peer-75` | 5 / 5 | -0.76% | 1.36% | 2.30 pp | 10.42 pp | Fail |

The fork/join model gives `Work.selected` 25%, 50%, and 50%, and `Work.peer` 0% at each speedup. The plateau at 75% comes from the peer, which then sets the end time.

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -5.96 pp | -5.25 pp | 11.41 pp | -5.85 pp | -5.16 pp | -10.42 pp |
| 2 | Yes | 4.64 pp | 1.27 pp | 1.05 pp | 4.96 pp | 5.87 pp | 0.36 pp |
| 3 | Yes | -2.22 pp | 1.24 pp | -1.62 pp | -0.11 pp | -3.60 pp | -2.30 pp |
| 4 | Yes | -3.79 pp | -7.98 pp | 0.04 pp | -1.18 pp | 1.61 pp | -0.42 pp |
| 5 | Yes | -0.12 pp | -1.07 pp | -0.43 pp | -1.74 pp | 0.55 pp | 2.73 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T024109669-c30228ca` | First causal profile run | Pass |
| `20261019T032133781-6cc15ea3` | Throughput progress points | Fail. Points selected-75, peer-75 fail, or a run-level criterion fails. |

## Limits

The progress point is the end of the workload.
The profile covers CPU-bound threads on Linux x64 that do not block outside `TargetBarrier`. CLR safety outside this workload is not established.
