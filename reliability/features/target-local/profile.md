# Target-local causal profile

Evidence: `artifacts/target-local-profile/20261019T090901082-03de2f25`.

`TargetLocalProfileVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `causal-profile` process and seven `plain` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. The selected thread runs 6000 rounds and the peer 3000. The progress point is the end of the workload.
The criteria are in [the profile plan](../../../lode/plans/target-local-causal-profile.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 4 / 5 | 26.20% | 25.53% | 2.89 pp | 6.37 pp | Fail |
| `selected-50` | 4 / 5 | 50.43% | 49.08% | 1.24 pp | 3.21 pp | Fail |
| `selected-75` | 4 / 5 | 49.77% | 50.60% | 1.59 pp | 3.20 pp | Fail |
| `peer-25` | 4 / 5 | 1.75% | -1.64% | 3.79 pp | 5.19 pp | Fail |
| `peer-50` | 4 / 5 | 0.47% | -0.05% | 0.50 pp | 4.34 pp | Fail |
| `peer-75` | 4 / 5 | -0.69% | -0.41% | 3.23 pp | 4.43 pp | Fail |

The fork/join model gives `Work.selected` 25%, 50%, and 50%, and `Work.peer` 0% at each speedup. The plateau at 75% comes from the peer, which then sets the end time.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.982, 0.989 |
| `selected-50` | 0.988, 0.985 |
| `selected-75` | 1.003, 1.005 |
| `peer-25` | 0.982, 0.996 |
| `peer-50` | 0.995, 1.000 |
| `peer-75` | 1.007, 1.007 |

The median work ratio by run order in the process is order 0: 1.012, 1.028; order 1: 0.994, 1.000; order 2: 1.002, 1.000; order 3: 1.000, 1.025; order 4: 0.991, 0.999; order 5: 0.989, 1.009; order 6: 1.006, 1.004. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Failed experiments

Each row is a ledger failure of an experiment that did not pass, or the error of a failed experiment without a ledger failure. The time counts from the first journal event with a time. The amount is the debt for failure 1 and 0 for other failures. The gap is the time from the previous journal event of the same worker, in sequence order, and a buffered sample can make it negative. A failure without a time shows n/a. The collection count and the GC pause cover all threads of the process during the experiment.

| Block | Experiment | Worker | Failure | Amount | Time | Gap | Collections | GC pause |
| ---: | --- | ---: | ---: | ---: | ---: | --- | ---: | ---: |
| 3 | `selected` 25% | 0 | 1 | 2.582 ms | 25.002 ms | 0.003 ms after `Sample` | 0 | 0.000 ms |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | 0.14 pp | 0.28 pp | -1.36 pp | 5.15 pp | -0.31 pp | 1.02 pp |
| 2 | Yes | 6.37 pp | 3.21 pp | 1.09 pp | 5.19 pp | 4.34 pp | 3.59 pp |
| 3 | No: profile exit 1; profile not passed | n/a | 1.77 pp | -2.67 pp | 1.97 pp | 2.19 pp | 1.32 pp |
| 4 | Yes | -4.41 pp | 1.07 pp | -1.83 pp | -0.25 pp | -0.33 pp | -2.87 pp |
| 5 | Yes | -1.36 pp | -1.40 pp | -3.20 pp | -2.42 pp | 0.67 pp | -4.43 pp |

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
| `20261019T090901082-03de2f25` | Failure and GC records | Fail. Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. |

## Limits

The progress point is the end of the workload.
The profile covers CPU-bound threads on Linux x64 that do not block outside `TargetBarrier`. CLR safety outside this workload is not established.
