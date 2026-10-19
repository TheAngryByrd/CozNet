# Target-local transaction profile

Evidence: `artifacts/target-local-transaction/20261019T191349654-efea55a8`.

`TargetLocalTransactionVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `transaction-profile` process and seven `plain-transaction` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms with a latency progress point. Worker 0 begins a transaction, runs `Work.selected 40`, and enqueues it in a `TargetQueue` of 4 items. Worker 1 dequeues it, runs `Work.peer 8`, and ends it. The prediction is `1 - (VL / V) / (VL0 / V0)`, where VL sums the virtual latency of the transactions, each its wall latency minus the global delay added during it and at least 0, and V counts the departures. The real gain is `1 - W / W0`, where W is the mean wall latency.
The criteria are in [the latency point plan](../../../lode/plans/target-local-transactions.md) and [the transaction delay plan](../../../lode/plans/target-local-transaction-delay.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 4 / 5 | 19.60% | 27.80% | 26.27 pp | 37.66 pp | Fail |
| `selected-50` | 4 / 5 | 39.16% | 40.91% | 1.58 pp | 16.75 pp | Fail |
| `selected-75` | 4 / 5 | 52.32% | 63.52% | 10.92 pp | 16.38 pp | Fail |
| `peer-25` | 4 / 5 | 3.85% | 13.82% | 23.85 pp | 25.99 pp | Fail |
| `peer-50` | 4 / 5 | 7.95% | 14.14% | 8.27 pp | 22.26 pp | Fail |
| `peer-75` | 4 / 5 | 10.90% | 17.91% | 7.37 pp | 23.04 pp | Fail |

The producer sets the rate at every point, so the latency of a transaction is about the sum of both parts. The model gives `Work.selected` 20.8%, 41.7%, and 62.5%, and `Work.peer` 4.2%, 8.3%, and 12.5%. The model leaves out the queue handoff and any queue wait.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 1.004, 0.944 |
| `selected-50` | 1.006, 0.892 |
| `selected-75` | 0.997, 0.749 |
| `peer-25` | 0.996, 1.045 |
| `peer-50` | 0.996, 1.091 |
| `peer-75` | 1.002, 1.145 |

The median work ratio by run order in the process is order 0: 1.001, 1.000; order 1: 1.000, 1.007; order 2: 0.991, 1.000; order 3: 1.001, 1.054; order 4: 0.991, 0.933; order 5: 1.001, 0.951; order 6: 0.986, 0.964. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Queue handoff

The handoff of an item is the time from just before its enqueue to the return of the dequeue that takes it. The table gives the median over valid blocks of the mean handoff, in the profile experiment and in the real arm. The baseline row compares the profile baseline with the plain original arm, and it is the only row that compares equal measures. At a speedup above 0, a profiled handoff also includes the delay that the producer pays in `Enqueue`. At every point, the handoff also includes the time that an item waits in the queue.

| Point | Median mean handoff, profile | Median mean handoff, real arm |
| --- | ---: | ---: |
| baseline | 45.7 us | 174.8 us |
| `selected-25` | 53.8 us | 59.5 us |
| `selected-50` | 57.3 us | 26.4 us |
| `selected-75` | 124.4 us | 26.6 us |
| `peer-25` | 61.1 us | 41.4 us |
| `peer-50` | 75.4 us | 43.4 us |
| `peer-75` | 80.2 us | 42.0 us |

The second value is the signed difference between the dequeue return and the enqueue return of the same item, as a mean. It can be negative, because the consumer can take an item before the producer reads its time after `Enqueue`. The table gives the median over valid blocks of the baseline values.

| Arm | Mean enqueue time | Mean dequeue return minus enqueue return | Share of spin takes |
| --- | ---: | ---: | ---: |
| Profile baseline | 13.0 us | 31.0 us | 0.0% |
| Plain original | 15.3 us | 145.4 us | 0.0% |

## Failed experiments

Each row is a ledger failure of an experiment that did not pass, or the error of a failed experiment without a ledger failure. The time counts from the first journal event with a time. The amount is the debt for failure 1 and 0 for other failures. The gap is the time from the previous journal event of the same worker, in sequence order, and a buffered sample can make it negative. A failure without a time shows n/a. The collection count and the GC pause cover all threads of the process during the experiment.

| Block | Experiment | Worker | Failure | Amount | Time | Gap | Collections | GC pause |
| ---: | --- | ---: | ---: | ---: | ---: | --- | ---: | ---: |
| 2 | `selected` 75% | 1 | 1 | 2.250 ms | 90.654 ms | 0.004 ms after `Wake` | 0 | 0.000 ms |

## Failure context

For each failure, each row summarizes one worker over the journal events that come before the failure in sequence and no more than 10 ms before it in time. The record also keeps the 64 latest of these events. Child payments show their total measured time for their total requested time over the whole window, which can cover several payments. The overshoot is the largest measured minus requested time of one completed child. A failed child wait is counted, but it is not ranked. The ledger records a publish only when it raises the global delay, from the observed value to the local delay of its worker. Offsets count from the failure. An event of another worker can have a positive offset, because it takes its time before its sequence.

| Block | Experiment | Failing worker | Window events | Worker | Events | Children | Child time | Largest overshoot | Publishes | Last publish |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | --- | --- | ---: | --- |
| 2 | `selected` 75% | 1 | 153 | 0 | 49 | 1 (0 failed) | 0.000 ms for 0.000 ms | 0.000 ms at -5.809 ms | 11 | 66.000 ms over 65.250 ms at -0.728 ms |
| 2 | `selected` 75% | 1 | 153 | 1 | 104 | 62 (0 failed) | 3.050 ms for 3.041 ms | 0.004 ms at -4.961 ms | 6 | 63.000 ms over 63.000 ms at -3.962 ms |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -19.36 pp | -16.75 pp | -9.85 pp | -25.99 pp | -22.26 pp | -23.04 pp |
| 2 | No: profile exit 1; profile not passed | 15.39 pp | 28.80 pp | n/a | 22.37 pp | 16.08 pp | 17.33 pp |
| 3 | Yes | -37.66 pp | -1.21 pp | -16.38 pp | -21.95 pp | -16.27 pp | -11.98 pp |
| 4 | Yes | -0.04 pp | -1.95 pp | -10.35 pp | 1.37 pp | -0.28 pp | 0.23 pp |
| 5 | Yes | 33.19 pp | 0.05 pp | -11.48 pp | 25.76 pp | 0.28 pp | 2.77 pp |

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
| `20261019T183627302-c5843c4c` | Payment before each progress stamp | Fail. Block 3 is invalid from a debt failure after a wake. With four valid blocks, `selected-75` has a signed median of -11.83 points. |
| `20261019T191349654-efea55a8` | Diagnostic sample period (1 ms run) | Fail. Points selected-25, selected-50, selected-75, peer-25, peer-50, peer-75 fail, or a run-level criterion fails. |

## Limits

A latency experiment counts each transaction that ends, from the first work start to the last finish. The consumer drains the queue after the stop, so each transaction that begins also ends.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetQueue` with one producer and one consumer, with a latency progress point. CLR safety outside this workload is not established.
