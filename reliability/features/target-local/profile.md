# Target-local causal profile

Evidence: `artifacts/target-local-profile/20261019T060624408-2e3fb8c4`.

`TargetLocalProfileVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `causal-profile` process and seven `plain` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. The selected thread runs 6000 rounds and the peer 3000. The progress point is the end of the workload.
The criteria are in [the profile plan](../../../lode/plans/target-local-causal-profile.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run passes.**

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 23.90% | 25.90% | 1.70 pp | 6.58 pp | Pass |
| `selected-50` | 5 / 5 | 49.54% | 49.21% | 1.30 pp | 2.20 pp | Pass |
| `selected-75` | 5 / 5 | 49.69% | 49.95% | 1.03 pp | 2.64 pp | Pass |
| `peer-25` | 5 / 5 | -0.36% | 3.34% | 2.03 pp | 9.36 pp | Pass |
| `peer-50` | 5 / 5 | 0.39% | -0.90% | 3.58 pp | 6.58 pp | Pass |
| `peer-75` | 5 / 5 | 1.33% | 2.08% | 3.74 pp | 4.94 pp | Pass |

The fork/join model gives `Work.selected` 25%, 50%, and 50%, and `Work.peer` 0% at each speedup. The plateau at 75% comes from the peer, which then sets the end time.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 1.012, 1.024 |
| `selected-50` | 0.999, 1.016 |
| `selected-75` | 1.005, 1.023 |
| `peer-25` | 1.004, 1.013 |
| `peer-50` | 0.996, 1.008 |
| `peer-75` | 0.987, 1.028 |

The median work ratio by run order in the process is order 0: 1.000, 1.013; order 1: 1.000, 1.000; order 2: 1.000, 1.005; order 3: 1.000, 1.028; order 4: 1.000, 1.013; order 5: 0.998, 1.027; order 6: 0.996, 1.016. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -1.70 pp | 1.67 pp | -0.63 pp | -2.03 pp | 3.58 pp | 0.53 pp |
| 2 | Yes | -0.43 pp | 0.53 pp | 0.93 pp | -1.34 pp | 1.81 pp | 3.74 pp |
| 3 | Yes | -6.58 pp | -2.20 pp | -1.34 pp | -9.36 pp | -6.58 pp | -3.74 pp |
| 4 | Yes | -0.32 pp | -1.30 pp | -1.03 pp | 3.80 pp | 4.04 pp | -4.94 pp |
| 5 | Yes | 4.04 pp | -0.29 pp | 2.64 pp | 1.91 pp | 0.95 pp | 1.87 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T024109669-c30228ca` | First causal profile run | Pass |
| `20261019T032133781-6cc15ea3` | Throughput progress points | Fail. `selected-75` and `peer-75` each have one block above 10 points, both in block 1, where the plain arms give 36.0% and 4.5%. |
| `20261019T040508680-38464c4a` | Blocking queue and pipeline profile | Fail. Block 3 is invalid: its `selected` experiment at 75% breaks the 2 ms debt bound. |
| `20261019T043738634-485c3ea9` | Per-thread experiment diagnostics | Fail. `peer-50` median 5.52 points and one block of 10.91 points. `peer-75` has one block of 12.72 points. |
| `20261019T051105836-8f4f2ab0` | Profile warm-up experiment | Pass |
| `20261019T060624408-2e3fb8c4` | Rotated experiment order and warm-up gate | Pass |

## Limits

The progress point is the end of the workload.
The profile covers CPU-bound threads on Linux x64 that do not block outside `TargetBarrier`. CLR safety outside this workload is not established.
