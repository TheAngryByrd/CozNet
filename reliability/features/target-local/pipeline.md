# Target-local pipeline profile

Evidence: `artifacts/target-local-pipeline/20261019T094141941-f8894fe3`.

`TargetLocalPipelineVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `pipeline-profile` process and seven `plain-pipeline` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms. A producer repeats `Work.selected 40` and puts one item into a `TargetQueue` of 4 items. A consumer takes each item, runs `Work.peer 20`, and visits the progress point.
The criteria are in [the queue plan](../../../lode/plans/target-local-queue.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 4 / 5 | 24.74% | 25.41% | 1.36 pp | 6.00 pp | Fail |
| `selected-50` | 4 / 5 | 48.99% | 49.84% | 0.35 pp | 2.03 pp | Fail |
| `selected-75` | 4 / 5 | 47.85% | 47.94% | 1.14 pp | 3.38 pp | Fail |
| `peer-25` | 4 / 5 | -1.56% | 1.86% | 2.80 pp | 6.16 pp | Fail |
| `peer-50` | 4 / 5 | 0.05% | 0.12% | 1.46 pp | 11.12 pp | Fail |
| `peer-75` | 4 / 5 | -1.21% | 2.29% | 4.50 pp | 5.47 pp | Fail |

The producer is the bottleneck, so the model gives `Work.selected` 25%, 50%, and 50%, and `Work.peer` 0% at each speedup. Above 50%, the consumer sets the rate.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.998, 0.869 |
| `selected-50` | 0.990, 0.527 |
| `selected-75` | 1.237, 0.521 |
| `peer-25` | 1.016, 1.145 |
| `peer-50` | 0.999, 1.247 |
| `peer-75` | 1.011, 1.395 |

The median work ratio by run order in the process is order 0: 1.003, 1.000; order 1: 1.008, 1.000; order 2: 1.000, 1.000; order 3: 1.000, 1.117; order 4: 1.000, 1.000; order 5: 1.003, 1.002; order 6: 1.015, 0.898. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Failed experiments

Each row is a ledger failure of an experiment that did not pass, or the error of a failed experiment without a ledger failure. The time counts from the first journal event with a time. The amount is the debt for failure 1 and 0 for other failures. The gap is the time from the previous journal event of the same worker, in sequence order, and a buffered sample can make it negative. A failure without a time shows n/a. The collection count and the GC pause cover all threads of the process during the experiment.

| Block | Experiment | Worker | Failure | Amount | Time | Gap | Collections | GC pause |
| ---: | --- | ---: | ---: | ---: | ---: | --- | ---: | ---: |
| 5 | `selected` 75% | 1 | 1 | 2.250 ms | 3.969 ms | 0.003 ms after `Wake` | 0 | 0.000 ms |

## Failure context

For each failure, each row summarizes one worker over the journal events that come before the failure in sequence and no more than 10 ms before it in time. The record also keeps the 64 latest of these events. Child payments show their total measured time for their total requested time over the whole window, which can cover several payments. The overshoot is the largest measured minus requested time of one completed child. A failed child wait is counted, but it is not ranked. The ledger records a publish only when it raises the global delay, from the observed value to the local delay of its worker. Offsets count from the failure. An event of another worker can have a positive offset, because it takes its time before its sequence.

| Block | Experiment | Failing worker | Window events | Worker | Events | Children | Child time | Largest overshoot | Publishes | Last publish |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | --- | --- | ---: | --- |
| 5 | `selected` 75% | 1 | 13 | 0 | 11 | 0 (0 failed) | 0.000 ms for 0.000 ms | none | 3 | 2.250 ms over 1.500 ms at -0.889 ms |
| 5 | `selected` 75% | 1 | 13 | 1 | 2 | 0 (0 failed) | 0.000 ms for 0.000 ms | none | 0 | none |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -1.05 pp | 0.13 pp | 0.40 pp | -0.66 pp | -0.53 pp | -5.47 pp |
| 2 | Yes | 1.66 pp | -0.49 pp | 1.85 pp | -0.68 pp | 0.39 pp | 2.05 pp |
| 3 | Yes | -6.00 pp | -2.03 pp | -3.38 pp | -4.92 pp | -11.12 pp | -4.64 pp |
| 4 | Yes | 0.53 pp | 0.22 pp | 0.44 pp | -6.16 pp | 2.40 pp | -4.36 pp |
| 5 | No: profile exit 1; profile not passed | -1.28 pp | -0.37 pp | n/a | 2.26 pp | -1.25 pp | -0.71 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T040306290-64463c6b` | First pipeline profile run | Fail. `peer-50` median 6.76 points. Across the `peer` points, 13 of 15 block predictions are below their actual gains. |
| `20261019T043536804-514995d6` | Per-thread experiment diagnostics | Fail. `peer-75` has one block of 14.82 points. The `peer-50` prediction tracks the producer work ratio. |
| `20261019T050900839-0a049f8a` | Profile warm-up experiment | Fail. `peer-50` median 6.01 points. The order-0 producer work ratio stays 1.041 with the warm-up. |
| `20261019T060421721-4a882c0a` | Rotated experiment order and warm-up gate | Pass. `peer-50` median 4.92 points. |
| `20261019T062807524-d72126fc` | TargetLock and lock profile | Pass |
| `20261019T070437097-c99cf6c4` | Lock handoff counts | Fail. `peer-50` has one block of 10.02 points. Its median difference is 2.70 points. |
| `20261019T074639406-cf96acce` | Spin before a lock wait | Pass |
| `20261019T082629301-c4292b43` | TargetSemaphore and semaphore profile | Fail. `peer-25` has one block of 10.11 points. |
| `20261019T090700489-b82b1791` | Failure and GC records | Pass |
| `20261019T094141941-f8894fe3` | Failure context | Fail. Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. |

## Limits

A throughput experiment counts every visit from the first work start to the last finish, so work in progress at the stop counts.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetQueue` with one producer and one consumer. CLR safety outside this workload is not established.
