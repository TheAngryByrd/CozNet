# Target-local causal profile

Evidence: `artifacts/target-local-profile/20261019T121727927-5e679245`.

`TargetLocalProfileVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `causal-profile` process and seven `plain` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. The selected thread runs 6000 rounds and the peer 3000. The progress point is the end of the workload.
The criteria are in [the profile plan](../../../lode/plans/target-local-causal-profile.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run passes.**

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 25.75% | 24.48% | 2.85 pp | 3.18 pp | Pass |
| `selected-50` | 5 / 5 | 49.38% | 49.33% | 1.84 pp | 5.22 pp | Pass |
| `selected-75` | 5 / 5 | 47.84% | 50.69% | 2.27 pp | 4.55 pp | Pass |
| `peer-25` | 5 / 5 | 0.77% | 0.38% | 1.60 pp | 3.16 pp | Pass |
| `peer-50` | 5 / 5 | -1.45% | 1.72% | 3.44 pp | 5.12 pp | Pass |
| `peer-75` | 5 / 5 | -1.11% | 0.67% | 1.78 pp | 2.24 pp | Pass |

The fork/join model gives `Work.selected` 25%, 50%, and 50%, and `Work.peer` 0% at each speedup. The plateau at 75% comes from the peer, which then sets the end time.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.988, 1.001 |
| `selected-50` | 1.007, 1.000 |
| `selected-75` | 1.034, 1.046 |
| `peer-25` | 0.992, 1.005 |
| `peer-50` | 1.014, 1.009 |
| `peer-75` | 1.011, 1.038 |

The median work ratio by run order in the process is order 0: 0.995, 1.000; order 1: 1.005, 1.005; order 2: 1.000, 1.000; order 3: 1.000, 1.000; order 4: 1.014, 1.001; order 5: 0.988, 1.013; order 6: 1.007, 1.000. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | 3.01 pp | 1.35 pp | 1.02 pp | 3.16 pp | 1.12 pp | -1.44 pp |
| 2 | Yes | 1.67 pp | 1.84 pp | -3.44 pp | 1.13 pp | -2.03 pp | 1.01 pp |
| 3 | Yes | -2.85 pp | -4.21 pp | -2.27 pp | 0.39 pp | -3.44 pp | -1.78 pp |
| 4 | Yes | 1.66 pp | 1.19 pp | -0.43 pp | -1.60 pp | -3.81 pp | -1.91 pp |
| 5 | Yes | -3.18 pp | -5.22 pp | -4.55 pp | -2.75 pp | -5.12 pp | -2.24 pp |

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
| `20261019T082828297-eb8d2196` | TargetSemaphore and semaphore profile | Fail. Block 1 is invalid: `peer` at 75% records ledger failure 1, with no large run-queue delay. |
| `20261019T090901082-03de2f25` | Failure and GC records | Fail. Block 3 is invalid: `selected` at 25% records ledger failure 1, with no collection and no run-queue delay. |
| `20261019T094341955-4375de13` | Failure context | Pass |
| `20261019T110709359-b184c519` | Latency progress points | Pass |
| `20261019T114214114-cd942bc0` | Per-transaction virtual latency | Pass |
| `20261019T121727927-5e679245` | Transaction queue handoff | Pass |

## Limits

The progress point is the end of the workload.
The profile covers CPU-bound threads on Linux x64 that do not block outside `TargetBarrier`. CLR safety outside this workload is not established.
