# Target-local causal profile

Evidence: `artifacts/target-local-profile/20261019T151001061-8a5886f5`.

`TargetLocalProfileVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `causal-profile` process and seven `plain` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. The selected thread runs 6000 rounds and the peer 3000. The progress point is the end of the workload.
The criteria are in [the profile plan](../../../lode/plans/target-local-causal-profile.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run passes.**

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 23.96% | 25.21% | 2.20 pp | 2.92 pp | Pass |
| `selected-50` | 5 / 5 | 49.28% | 48.46% | 1.15 pp | 1.92 pp | Pass |
| `selected-75` | 5 / 5 | 48.65% | 49.60% | 2.62 pp | 2.71 pp | Pass |
| `peer-25` | 5 / 5 | 0.08% | -1.80% | 2.54 pp | 5.09 pp | Pass |
| `peer-50` | 5 / 5 | -1.01% | -1.10% | 1.09 pp | 2.50 pp | Pass |
| `peer-75` | 5 / 5 | -1.94% | 0.30% | 1.11 pp | 3.90 pp | Pass |

The fork/join model gives `Work.selected` 25%, 50%, and 50%, and `Work.peer` 0% at each speedup. The plateau at 75% comes from the peer, which then sets the end time.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 1.011, 1.009 |
| `selected-50` | 1.010, 1.001 |
| `selected-75` | 1.013, 1.017 |
| `peer-25` | 0.999, 0.990 |
| `peer-50` | 1.010, 0.993 |
| `peer-75` | 1.019, 1.013 |

The median work ratio by run order in the process is order 0: 1.004, 1.000; order 1: 1.000, 1.009; order 2: 1.000, 1.000; order 3: 1.010, 1.000; order 4: 1.000, 1.000; order 5: 0.995, 1.009; order 6: 1.013, 1.005. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -2.20 pp | -1.35 pp | -2.62 pp | 2.54 pp | -1.41 pp | -3.24 pp |
| 2 | Yes | 2.92 pp | 1.92 pp | 2.71 pp | 2.79 pp | 2.50 pp | 0.33 pp |
| 3 | Yes | -2.42 pp | 0.22 pp | -2.67 pp | -0.42 pp | 1.09 pp | -0.14 pp |
| 4 | Yes | 0.75 pp | 1.15 pp | 0.05 pp | -5.09 pp | 0.41 pp | -3.90 pp |
| 5 | Yes | -1.09 pp | -0.29 pp | -0.95 pp | 1.88 pp | -1.09 pp | 1.11 pp |

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
| `20261019T125516048-6470f3c8` | Spin before a queue wait | Pass |
| `20261019T132316833-16878b7d` | Handoff parts | Pass |
| `20261019T151001061-8a5886f5` | Worker pinning | Pass |

## Limits

The progress point is the end of the workload.
The profile covers CPU-bound threads on Linux x64 that do not block outside `TargetBarrier`. CLR safety outside this workload is not established.
