# Target-local causal profile

Evidence: `artifacts/target-local-profile/20261019T040508680-38464c4a`.

`TargetLocalProfileVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `causal-profile` process and seven `plain` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. The selected thread runs 6000 rounds and the peer 3000. The progress point is the end of the workload.
The criteria are in [the profile plan](../../../lode/plans/target-local-causal-profile.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 4 / 5 | 24.12% | 24.17% | 0.54 pp | 1.22 pp | Fail |
| `selected-50` | 4 / 5 | 48.29% | 49.22% | 1.70 pp | 2.27 pp | Fail |
| `selected-75` | 4 / 5 | 49.16% | 48.97% | 1.61 pp | 2.74 pp | Fail |
| `peer-25` | 4 / 5 | -0.07% | -1.32% | 2.14 pp | 5.47 pp | Fail |
| `peer-50` | 4 / 5 | -0.45% | -0.59% | 2.76 pp | 4.11 pp | Fail |
| `peer-75` | 4 / 5 | 0.21% | 0.01% | 2.05 pp | 5.14 pp | Fail |

The fork/join model gives `Work.selected` 25%, 50%, and 50%, and `Work.peer` 0% at each speedup. The plateau at 75% comes from the peer, which then sets the end time.

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | 1.22 pp | 2.27 pp | 2.74 pp | 5.47 pp | 2.97 pp | 3.34 pp |
| 2 | Yes | 0.29 pp | -1.21 pp | -1.61 pp | -1.45 pp | -4.11 pp | -5.14 pp |
| 3 | No: profile exit 1; profile not passed | 0.11 pp | -1.89 pp | n/a | -2.55 pp | -1.48 pp | 1.37 pp |
| 4 | Yes | -0.67 pp | -1.10 pp | -1.60 pp | 1.23 pp | 2.54 pp | -0.37 pp |
| 5 | Yes | -0.41 pp | -2.18 pp | 0.48 pp | -2.84 pp | 0.25 pp | 0.76 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T024109669-c30228ca` | First causal profile run | Pass |
| `20261019T032133781-6cc15ea3` | Throughput progress points | Fail. `selected-75` and `peer-75` each have one block above 10 points, both in block 1, where the plain arms give 36.0% and 4.5%. |
| `20261019T040508680-38464c4a` | Blocking queue and pipeline profile | Fail. Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. |

## Limits

The progress point is the end of the workload.
The profile covers CPU-bound threads on Linux x64 that do not block outside `TargetBarrier`. CLR safety outside this workload is not established.
