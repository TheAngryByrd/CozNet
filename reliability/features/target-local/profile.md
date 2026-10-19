# Target-local causal profile

Evidence: `artifacts/target-local-profile/20261019T074837405-5db1c839`.

`TargetLocalProfileVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `causal-profile` process and seven `plain` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. The selected thread runs 6000 rounds and the peer 3000. The progress point is the end of the workload.
The criteria are in [the profile plan](../../../lode/plans/target-local-causal-profile.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 4 / 5 | 24.59% | 24.03% | 1.24 pp | 2.34 pp | Fail |
| `selected-50` | 4 / 5 | 48.37% | 50.08% | 1.71 pp | 3.06 pp | Fail |
| `selected-75` | 4 / 5 | 49.29% | 50.01% | 1.44 pp | 2.45 pp | Fail |
| `peer-25` | 4 / 5 | -0.02% | -0.45% | 2.26 pp | 5.58 pp | Fail |
| `peer-50` | 4 / 5 | 0.76% | 1.02% | 0.57 pp | 2.35 pp | Fail |
| `peer-75` | 4 / 5 | -1.12% | 1.10% | 2.67 pp | 5.27 pp | Fail |

The fork/join model gives `Work.selected` 25%, 50%, and 50%, and `Work.peer` 0% at each speedup. The plateau at 75% comes from the peer, which then sets the end time.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 1.004, 1.009 |
| `selected-50` | 1.020, 1.019 |
| `selected-75` | 1.006, 1.004 |
| `peer-25` | 1.000, 1.024 |
| `peer-50` | 0.992, 1.003 |
| `peer-75` | 1.011, 1.013 |

The median work ratio by run order in the process is order 0: 1.003, 1.000; order 1: 1.013, 1.000; order 2: 1.011, 1.026; order 3: 1.017, 0.999; order 4: 1.000, 1.000; order 5: 0.997, 0.990; order 6: 1.008, 0.989. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | No: profile exit 1; profile not passed | -3.81 pp | -4.23 pp | n/a | -6.61 pp | -6.23 pp | -7.98 pp |
| 2 | Yes | 1.31 pp | -0.00 pp | -0.39 pp | 1.62 pp | 0.02 pp | -0.69 pp |
| 3 | Yes | 0.62 pp | -1.59 pp | -2.45 pp | -2.90 pp | -0.58 pp | 0.83 pp |
| 4 | Yes | -1.17 pp | -1.84 pp | 1.62 pp | -1.43 pp | -2.35 pp | -5.27 pp |
| 5 | Yes | 2.34 pp | -3.06 pp | -1.25 pp | 5.58 pp | 0.55 pp | -4.51 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T024109669-c30228ca` | First causal profile run | Pass |
| `20261019T032133781-6cc15ea3` | Throughput progress points | Fail. `selected-75` and `peer-75` each have one block above 10 points, both in block 1, where the plain arms give 36.0% and 4.5%. |
| `20261019T040508680-38464c4a` | Blocking queue and pipeline profile | Fail. Block 3 is invalid: its `selected` experiment at 75% breaks the 2 ms debt bound. |
| `20261019T043738634-485c3ea9` | Per-thread experiment diagnostics | Fail. `peer-50` median 5.52 points and one block of 10.91 points. `peer-75` has one block of 12.72 points. |
| `20261019T051105836-8f4f2ab0` | Profile warm-up experiment | Pass |
| `20261019T060624408-2e3fb8c4` | Rotated experiment order and warm-up gate | Pass |
| `20261019T063004675-e6636cdf` | TargetLock and lock profile | Pass |
| `20261019T070634914-3cd1d60c` | Lock handoff counts | Pass |
| `20261019T074837405-5db1c839` | Spin before a lock wait | Fail. Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. |

## Limits

The progress point is the end of the workload.
The profile covers CPU-bound threads on Linux x64 that do not block outside `TargetBarrier`. CLR safety outside this workload is not established.
