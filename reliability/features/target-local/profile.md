# Target-local causal profile

Evidence: `artifacts/target-local-profile/20261019T063004675-e6636cdf`.

`TargetLocalProfileVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `causal-profile` process and seven `plain` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. The selected thread runs 6000 rounds and the peer 3000. The progress point is the end of the workload.
The criteria are in [the profile plan](../../../lode/plans/target-local-causal-profile.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run passes.**

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 27.20% | 24.32% | 2.27 pp | 3.58 pp | Pass |
| `selected-50` | 5 / 5 | 49.52% | 50.04% | 1.49 pp | 1.91 pp | Pass |
| `selected-75` | 5 / 5 | 49.59% | 49.38% | 0.25 pp | 1.69 pp | Pass |
| `peer-25` | 5 / 5 | 0.91% | 0.06% | 1.94 pp | 6.46 pp | Pass |
| `peer-50` | 5 / 5 | 2.49% | -2.08% | 4.34 pp | 5.25 pp | Pass |
| `peer-75` | 5 / 5 | 1.73% | -0.31% | 2.53 pp | 4.07 pp | Pass |

The fork/join model gives `Work.selected` 25%, 50%, and 50%, and `Work.peer` 0% at each speedup. The plateau at 75% comes from the peer, which then sets the end time.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.970, 0.989 |
| `selected-50` | 0.995, 1.013 |
| `selected-75` | 1.010, 1.011 |
| `peer-25` | 0.991, 1.007 |
| `peer-50` | 0.975, 0.980 |
| `peer-75` | 0.983, 1.016 |

The median work ratio by run order in the process is order 0: 1.000, 1.011; order 1: 0.991, 1.006; order 2: 0.995, 1.013; order 3: 0.995, 1.013; order 4: 0.993, 1.000; order 5: 1.007, 0.993; order 6: 0.981, 0.989. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | 3.58 pp | -1.91 pp | 0.60 pp | -3.80 pp | 2.91 pp | 3.12 pp |
| 2 | Yes | -1.42 pp | 1.49 pp | 1.69 pp | 1.48 pp | 5.07 pp | 1.23 pp |
| 3 | Yes | 2.04 pp | -1.74 pp | -0.12 pp | 1.94 pp | -4.34 pp | 2.53 pp |
| 4 | Yes | 2.27 pp | 0.77 pp | -0.13 pp | 1.55 pp | 2.76 pp | 1.00 pp |
| 5 | Yes | 3.09 pp | -1.04 pp | 0.25 pp | 6.46 pp | 5.25 pp | 4.07 pp |

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

## Limits

The progress point is the end of the workload.
The profile covers CPU-bound threads on Linux x64 that do not block outside `TargetBarrier`. CLR safety outside this workload is not established.
