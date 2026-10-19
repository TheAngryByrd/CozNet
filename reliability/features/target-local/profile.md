# Target-local causal profile

Evidence: `artifacts/target-local-profile/20261019T082828297-eb8d2196`.

`TargetLocalProfileVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `causal-profile` process and seven `plain` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. The selected thread runs 6000 rounds and the peer 3000. The progress point is the end of the workload.
The criteria are in [the profile plan](../../../lode/plans/target-local-causal-profile.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 4 / 5 | 25.00% | 24.47% | 1.11 pp | 4.93 pp | Fail |
| `selected-50` | 4 / 5 | 49.48% | 49.79% | 1.05 pp | 1.82 pp | Fail |
| `selected-75` | 4 / 5 | 49.63% | 50.88% | 0.93 pp | 2.69 pp | Fail |
| `peer-25` | 4 / 5 | -2.54% | -1.28% | 3.76 pp | 6.94 pp | Fail |
| `peer-50` | 4 / 5 | 0.58% | 1.11% | 1.58 pp | 5.19 pp | Fail |
| `peer-75` | 4 / 5 | -0.25% | 0.60% | 2.38 pp | 4.70 pp | Fail |

The fork/join model gives `Work.selected` 25%, 50%, and 50%, and `Work.peer` 0% at each speedup. The plateau at 75% comes from the peer, which then sets the end time.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.998, 1.003 |
| `selected-50` | 1.007, 1.004 |
| `selected-75` | 0.999, 1.005 |
| `peer-25` | 1.025, 1.004 |
| `peer-50` | 0.994, 1.005 |
| `peer-75` | 1.002, 1.028 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 0.993, 1.000; order 2: 1.000, 0.991; order 3: 0.997, 1.000; order 4: 1.000, 0.999; order 5: 0.994, 1.008; order 6: 1.008, 1.015. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | No: profile exit 1; profile not passed | -2.40 pp | -0.52 pp | -0.37 pp | -3.96 pp | -0.81 pp | n/a |
| 2 | Yes | 4.93 pp | 1.17 pp | -0.79 pp | 4.30 pp | 0.09 pp | 1.09 pp |
| 3 | Yes | 1.31 pp | -1.82 pp | 1.08 pp | -3.22 pp | 0.36 pp | 1.95 pp |
| 4 | Yes | -0.48 pp | 0.30 pp | -2.69 pp | -0.09 pp | -5.19 pp | -4.70 pp |
| 5 | Yes | -0.91 pp | -0.92 pp | -0.74 pp | -6.94 pp | -2.79 pp | -2.80 pp |

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
| `20261019T074837405-5db1c839` | Spin before a lock wait | Fail. Block 1 is invalid: `selected` at 75% breaks the 2 ms debt bound, with 8.41 ms of run-queue delay on thread 1. |
| `20261019T082828297-eb8d2196` | TargetSemaphore and semaphore profile | Fail. Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. |

## Limits

The progress point is the end of the workload.
The profile covers CPU-bound threads on Linux x64 that do not block outside `TargetBarrier`. CLR safety outside this workload is not established.
