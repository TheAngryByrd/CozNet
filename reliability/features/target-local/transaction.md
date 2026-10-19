# Target-local transaction profile

Evidence: `artifacts/target-local-transaction/20261019T183627302-c5843c4c`.

`TargetLocalTransactionVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `transaction-profile` process and seven `plain-transaction` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms with a latency progress point. Worker 0 begins a transaction, runs `Work.selected 40`, and enqueues it in a `TargetQueue` of 4 items. Worker 1 dequeues it, runs `Work.peer 8`, and ends it. The prediction is `1 - (VL / V) / (VL0 / V0)`, where VL sums the virtual latency of the transactions, each its wall latency minus the global delay added during it and at least 0, and V counts the departures. The real gain is `1 - W / W0`, where W is the mean wall latency.
The criteria are in [the latency point plan](../../../lode/plans/target-local-transactions.md) and [the transaction delay plan](../../../lode/plans/target-local-transaction-delay.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 4 / 5 | 20.92% | 21.07% | 5.21 pp | 21.31 pp | Fail |
| `selected-50` | 4 / 5 | 39.96% | 41.38% | 1.98 pp | 7.08 pp | Fail |
| `selected-75` | 4 / 5 | 51.73% | 63.23% | 11.83 pp | 13.09 pp | Fail |
| `peer-25` | 4 / 5 | 5.04% | 4.32% | 3.78 pp | 8.25 pp | Fail |
| `peer-50` | 4 / 5 | 9.22% | 6.26% | 3.18 pp | 6.22 pp | Fail |
| `peer-75` | 4 / 5 | 12.77% | 12.13% | 2.98 pp | 7.80 pp | Fail |

The producer sets the rate at every point, so the latency of a transaction is about the sum of both parts. The model gives `Work.selected` 20.8%, 41.7%, and 62.5%, and `Work.peer` 4.2%, 8.3%, and 12.5%. The model leaves out the queue handoff and any queue wait.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.985, 0.924 |
| `selected-50` | 1.003, 0.883 |
| `selected-75` | 0.998, 0.757 |
| `peer-25` | 0.992, 1.048 |
| `peer-50` | 0.991, 1.094 |
| `peer-75` | 0.991, 1.135 |

The median work ratio by run order in the process is order 0: 1.003, 1.051; order 1: 0.995, 1.000; order 2: 1.000, 1.000; order 3: 0.995, 1.042; order 4: 1.000, 1.000; order 5: 0.997, 0.918; order 6: 1.012, 0.949. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Queue handoff

The handoff of an item is the time from just before its enqueue to the return of the dequeue that takes it. The table gives the median over valid blocks of the mean handoff, in the profile experiment and in the real arm. The baseline row compares the profile baseline with the plain original arm, and it is the only row that compares equal measures. At a speedup above 0, a profiled handoff also includes the delay that the producer pays in `Enqueue`. At every point, the handoff also includes the time that an item waits in the queue.

| Point | Median mean handoff, profile | Median mean handoff, real arm |
| --- | ---: | ---: |
| baseline | 50.1 us | 38.7 us |
| `selected-25` | 48.5 us | 32.9 us |
| `selected-50` | 55.8 us | 26.6 us |
| `selected-75` | 112.2 us | 11.3 us |
| `peer-25` | 64.0 us | 38.8 us |
| `peer-50` | 80.3 us | 39.3 us |
| `peer-75` | 93.2 us | 40.5 us |

The second value is the signed difference between the dequeue return and the enqueue return of the same item, as a mean. It can be negative, because the consumer can take an item before the producer reads its time after `Enqueue`. The table gives the median over valid blocks of the baseline values.

| Arm | Mean enqueue time | Mean dequeue return minus enqueue return | Share of spin takes |
| --- | ---: | ---: | ---: |
| Profile baseline | 13.4 us | 35.7 us | 0.0% |
| Plain original | 12.9 us | 27.1 us | 0.0% |

## Failed experiments

Each row is a ledger failure of an experiment that did not pass, or the error of a failed experiment without a ledger failure. The time counts from the first journal event with a time. The amount is the debt for failure 1 and 0 for other failures. The gap is the time from the previous journal event of the same worker, in sequence order, and a buffered sample can make it negative. A failure without a time shows n/a. The collection count and the GC pause cover all threads of the process during the experiment.

| Block | Experiment | Worker | Failure | Amount | Time | Gap | Collections | GC pause |
| ---: | --- | ---: | ---: | ---: | ---: | --- | ---: | ---: |
| 3 | `selected` 75% | 1 | 1 | 2.250 ms | 57.726 ms | 0.004 ms after `Wake` | 0 | 0.000 ms |

## Failure context

For each failure, each row summarizes one worker over the journal events that come before the failure in sequence and no more than 10 ms before it in time. The record also keeps the 64 latest of these events. Child payments show their total measured time for their total requested time over the whole window, which can cover several payments. The overshoot is the largest measured minus requested time of one completed child. A failed child wait is counted, but it is not ranked. The ledger records a publish only when it raises the global delay, from the observed value to the local delay of its worker. Offsets count from the failure. An event of another worker can have a positive offset, because it takes its time before its sequence.

| Block | Experiment | Failing worker | Window events | Worker | Events | Children | Child time | Largest overshoot | Publishes | Last publish |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | --- | --- | ---: | --- |
| 3 | `selected` 75% | 1 | 160 | 0 | 51 | 2 (0 failed) | 0.000 ms for 0.000 ms | 0.000 ms at -7.897 ms | 12 | 40.500 ms over 39.750 ms at -0.755 ms |
| 3 | `selected` 75% | 1 | 160 | 1 | 109 | 72 (0 failed) | 3.750 ms for 3.554 ms | 0.192 ms at -8.448 ms | 5 | 37.500 ms over 37.500 ms at -3.920 ms |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | 21.31 pp | -1.68 pp | -10.03 pp | 3.45 pp | 1.11 pp | 3.00 pp |
| 2 | Yes | -2.08 pp | -2.28 pp | -11.06 pp | -4.11 pp | -0.42 pp | -0.74 pp |
| 3 | No: profile exit 1; profile not passed | -0.56 pp | -1.51 pp | n/a | 22.31 pp | 0.97 pp | 0.05 pp |
| 4 | Yes | -7.60 pp | -7.08 pp | -13.09 pp | -8.25 pp | -6.22 pp | -7.80 pp |
| 5 | Yes | 2.82 pp | -0.23 pp | -12.59 pp | 1.34 pp | 5.25 pp | 2.96 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T110105861-414dba20` | First transaction profile run | Fail. All six points fail. The real gains follow the model, but the `peer` predictions stay near 5% at each speedup. |
| `20261019T113356248-f1cbfddc` | Per-transaction virtual latency | Fail. `selected-50` median 7.09 points and `peer-25` median 5.47 points. The other four points pass. |
| `20261019T121118409-e589636a` | Transaction queue handoff | Fail. `selected-25` median 7.58 points and `selected-50` median 10.07 points. The other four points pass. |
| `20261019T124914850-b10c2f47` | Spin before a queue wait | Fail. `selected-25` one block of 27.61 points, `selected-50` median 8.32 points, and `peer-50` one block of -12.59 points. |
| `20261019T131709144-4aa0a3ef` | Handoff parts | Fail. `selected-25` median 7.25 points and `selected-50` median 7.76 points. |
| `20261019T150326902-04963faa` | Worker pinning | Fail. Five points fail. Only `selected-75` passes. |
| `20261019T175316141-ec3885bd` | Matched plain warm-up | Fail. All six points fail. Block 3 is above the real gain at every point, by 12.01 to 16.26 points. |
| `20261019T183627302-c5843c4c` | Payment before each progress stamp | Fail. Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. |

## Limits

A latency experiment counts each transaction that ends, from the first work start to the last finish. The consumer drains the queue after the stop, so each transaction that begins also ends.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetQueue` with one producer and one consumer, with a latency progress point. CLR safety outside this workload is not established.
