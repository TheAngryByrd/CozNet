# Target-local transaction profile

Evidence: `artifacts/target-local-transaction/20261019T202333518-e70c6219`.

`TargetLocalTransactionVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `transaction-profile` process and seven `plain-transaction` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms with a latency progress point. Worker 0 begins a transaction, runs `Work.selected 40`, and enqueues it in a `TargetQueue` of 4 items. Worker 1 dequeues it, runs `Work.peer 8`, and ends it. The prediction is `1 - (VL / V) / (VL0 / V0)`, where VL sums the virtual latency of the transactions, each its wall latency minus the global delay added during it and at least 0, and V counts the departures. The real gain is `1 - W / W0`, where W is the mean wall latency.
The criteria are in [the latency point plan](../../../lode/plans/target-local-transactions.md) and [the transaction delay plan](../../../lode/plans/target-local-transaction-delay.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 20.30% | 18.08% | 1.77 pp | 4.44 pp | Pass |
| `selected-50` | 5 / 5 | 39.59% | 42.71% | 3.12 pp | 4.86 pp | Pass |
| `selected-75` | 5 / 5 | 52.92% | 63.46% | 10.75 pp | 16.22 pp | Fail |
| `peer-25` | 5 / 5 | 3.52% | 2.78% | 1.43 pp | 5.07 pp | Pass |
| `peer-50` | 5 / 5 | 7.28% | 7.50% | 1.86 pp | 3.23 pp | Pass |
| `peer-75` | 5 / 5 | 11.42% | 11.48% | 2.85 pp | 4.39 pp | Pass |

The producer sets the rate at every point, so the latency of a transaction is about the sum of both parts. The model gives `Work.selected` 20.8%, 41.7%, and 62.5%, and `Work.peer` 4.2%, 8.3%, and 12.5%. The model leaves out the queue handoff and any queue wait.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.996, 0.938 |
| `selected-50` | 1.010, 0.887 |
| `selected-75` | 1.004, 0.773 |
| `peer-25` | 1.004, 1.055 |
| `peer-50` | 1.011, 1.106 |
| `peer-75` | 1.009, 1.153 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 1.008, 1.000; order 2: 1.000, 1.000; order 3: 1.013, 1.065; order 4: 1.002, 1.000; order 5: 1.004, 0.897; order 6: 0.996, 0.938. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Queue handoff

The handoff of an item is the time from just before its enqueue to the return of the dequeue that takes it. The table gives the median over valid blocks of the mean handoff, in the profile experiment and in the real arm. The baseline row compares the profile baseline with the plain original arm, and it is the only row that compares equal measures. At a speedup above 0, a profiled handoff also includes the delay that the producer pays in `Enqueue`. At every point, the handoff also includes the time that an item waits in the queue.

| Point | Median mean handoff, profile | Median mean handoff, real arm |
| --- | ---: | ---: |
| baseline | 51.1 us | 43.3 us |
| `selected-25` | 54.5 us | 40.0 us |
| `selected-50` | 58.3 us | 17.5 us |
| `selected-75` | 114.7 us | 8.0 us |
| `peer-25` | 66.2 us | 47.2 us |
| `peer-50` | 73.6 us | 44.8 us |
| `peer-75` | 98.8 us | 45.1 us |

The second value is the signed difference between the dequeue return and the enqueue return of the same item, as a mean. It can be negative, because the consumer can take an item before the producer reads its time after `Enqueue`. The table gives the median over valid blocks of the baseline values.

| Arm | Mean enqueue time | Mean dequeue return minus enqueue return | Share of spin takes |
| --- | ---: | ---: | ---: |
| Profile baseline | 14.2 us | 38.1 us | 0.0% |
| Plain original | 14.3 us | 28.7 us | 0.0% |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | 4.44 pp | -2.34 pp | -16.22 pp | 2.72 pp | 3.23 pp | -4.39 pp |
| 2 | Yes | -1.00 pp | -4.86 pp | -13.96 pp | -0.02 pp | -2.91 pp | -2.94 pp |
| 3 | Yes | 1.77 pp | -4.06 pp | -8.95 pp | 0.53 pp | -1.86 pp | 0.61 pp |
| 4 | Yes | 3.68 pp | -3.12 pp | -10.75 pp | 5.07 pp | 0.39 pp | 2.76 pp |
| 5 | Yes | -0.14 pp | -2.05 pp | -7.90 pp | -1.43 pp | 1.29 pp | 2.85 pp |

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
| `20261019T191349654-efea55a8` | Diagnostic sample period (1 ms run) | Fail. Block 2 is invalid from a debt failure after a wake. With four valid blocks, `selected-75` has a signed median of -10.92 points. |
| `20261019T193820084-e379d1a4` | Handoff placement | Fail. Five valid blocks. Five points pass, and `selected-75` fails with a signed median of -10.48 points. |
| `20261019T195335859-ea098418` | Wake after idle | Fail. Five valid blocks. Two points pass, and `selected-75` fails with a signed median of -17.22 points. |
| `20261019T202333518-e70c6219` | Qualification package | Fail. Points selected-75 fail, or a run-level criterion fails. |

## Limits

A latency experiment counts each transaction that ends, from the first work start to the last finish. The consumer drains the queue after the stop, so each transaction that begins also ends.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetQueue` with one producer and one consumer, with a latency progress point. CLR safety outside this workload is not established.
