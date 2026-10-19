# Target-local causal profile

Evidence: `artifacts/target-local-profile/20261019T024109669-c30228ca`.

`TargetLocalProfileVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `causal-profile` process and seven `plain` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. The selected thread runs 6000 rounds and the peer 3000.
The criteria are in [the profile plan](../../../lode/plans/target-local-causal-profile.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run passes.**

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 24.43% | 24.74% | 0.96 pp | 3.22 pp | Pass |
| `selected-50` | 5 / 5 | 49.17% | 50.43% | 1.80 pp | 3.33 pp | Pass |
| `selected-75` | 5 / 5 | 48.73% | 50.24% | 3.02 pp | 3.31 pp | Pass |
| `peer-25` | 5 / 5 | 0.38% | 1.06% | 3.60 pp | 6.28 pp | Pass |
| `peer-50` | 5 / 5 | -1.20% | -0.11% | 2.75 pp | 5.07 pp | Pass |
| `peer-75` | 5 / 5 | -1.70% | 1.15% | 3.14 pp | 4.27 pp | Pass |

The fork/join model gives `Work.selected` 25%, 50%, and 50%, and `Work.peer` 0% at each speedup. The plateau at 75% comes from the peer, which then sets the end time.

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | 0.22 pp | -2.23 pp | 0.53 pp | 6.28 pp | 3.06 pp | -1.00 pp |
| 2 | Yes | -3.22 pp | -3.33 pp | -3.07 pp | -3.60 pp | -5.07 pp | -3.14 pp |
| 3 | Yes | -1.98 pp | -1.80 pp | 0.17 pp | -1.60 pp | 2.75 pp | -4.27 pp |
| 4 | Yes | -0.96 pp | -0.16 pp | -3.31 pp | -4.79 pp | -0.30 pp | 1.65 pp |
| 5 | Yes | 0.92 pp | -1.26 pp | -3.02 pp | 0.05 pp | -2.74 pp | -3.59 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T024109669-c30228ca` | First causal profile run | Pass |

## Limits

The progress point is the end of the workload. Throughput progress points are not supported.
The profile covers CPU-bound fork/join threads on Linux x64. CLR safety outside this workload is not established.
