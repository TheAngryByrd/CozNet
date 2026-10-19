# Target-local transaction profile

Evidence: `artifacts/target-local-transaction/20261019T193820084-e379d1a4`.

`TargetLocalTransactionVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `transaction-profile` process and seven `plain-transaction` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms with a latency progress point. Worker 0 begins a transaction, runs `Work.selected 40`, and enqueues it in a `TargetQueue` of 4 items. Worker 1 dequeues it, runs `Work.peer 8`, and ends it. The prediction is `1 - (VL / V) / (VL0 / V0)`, where VL sums the virtual latency of the transactions, each its wall latency minus the global delay added during it and at least 0, and V counts the departures. The real gain is `1 - W / W0`, where W is the mean wall latency.
The criteria are in [the latency point plan](../../../lode/plans/target-local-transactions.md) and [the transaction delay plan](../../../lode/plans/target-local-transaction-delay.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-75 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 20.33% | 19.39% | 2.27 pp | 3.05 pp | Pass |
| `selected-50` | 5 / 5 | 40.58% | 41.41% | 1.50 pp | 6.51 pp | Pass |
| `selected-75` | 5 / 5 | 51.92% | 61.83% | 10.48 pp | 16.97 pp | Fail |
| `peer-25` | 5 / 5 | 5.85% | 2.69% | 4.46 pp | 4.48 pp | Pass |
| `peer-50` | 5 / 5 | 8.27% | 7.44% | 1.85 pp | 6.49 pp | Pass |
| `peer-75` | 5 / 5 | 11.19% | 11.37% | 1.03 pp | 4.71 pp | Pass |

The producer sets the rate at every point, so the latency of a transaction is about the sum of both parts. The model gives `Work.selected` 20.8%, 41.7%, and 62.5%, and `Work.peer` 4.2%, 8.3%, and 12.5%. The model leaves out the queue handoff and any queue wait.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.977, 0.922 |
| `selected-50` | 0.979, 0.865 |
| `selected-75` | 0.986, 0.718 |
| `peer-25` | 0.983, 1.027 |
| `peer-50` | 0.987, 1.077 |
| `peer-75` | 1.010, 1.154 |

The median work ratio by run order in the process is order 0: 0.987, 1.000; order 1: 1.000, 1.000; order 2: 0.988, 1.000; order 3: 1.000, 1.032; order 4: 0.979, 1.000; order 5: 0.971, 0.904; order 6: 0.994, 0.943. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Queue handoff

The handoff of an item is the time from just before its enqueue to the return of the dequeue that takes it. The table gives the median over valid blocks of the mean handoff, in the profile experiment and in the real arm. The baseline row compares the profile baseline with the plain original arm, and it is the only row that compares equal measures. At a speedup above 0, a profiled handoff also includes the delay that the producer pays in `Enqueue`. At every point, the handoff also includes the time that an item waits in the queue.

| Point | Median mean handoff, profile | Median mean handoff, real arm |
| --- | ---: | ---: |
| baseline | 45.7 us | 42.5 us |
| `selected-25` | 53.2 us | 36.0 us |
| `selected-50` | 62.3 us | 19.8 us |
| `selected-75` | 130.2 us | 9.7 us |
| `peer-25` | 64.8 us | 43.8 us |
| `peer-50` | 78.4 us | 41.9 us |
| `peer-75` | 83.4 us | 41.7 us |

The second value is the signed difference between the dequeue return and the enqueue return of the same item, as a mean. It can be negative, because the consumer can take an item before the producer reads its time after `Enqueue`. The table gives the median over valid blocks of the baseline values.

| Arm | Mean enqueue time | Mean dequeue return minus enqueue return | Share of spin takes |
| --- | ---: | ---: | ---: |
| Profile baseline | 13.1 us | 31.9 us | 0.0% |
| Plain original | 11.7 us | 30.0 us | 0.0% |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | -2.27 pp | -6.51 pp | -16.97 pp | -4.48 pp | -1.85 pp | -4.71 pp |
| 2 | Yes | 1.36 pp | -0.69 pp | -11.81 pp | 4.47 pp | 6.49 pp | 1.03 pp |
| 3 | Yes | -1.43 pp | -2.36 pp | -7.48 pp | 1.92 pp | 0.73 pp | -0.70 pp |
| 4 | Yes | 3.05 pp | -1.50 pp | -8.99 pp | 3.85 pp | 4.30 pp | 4.47 pp |
| 5 | Yes | 2.59 pp | 0.78 pp | -10.48 pp | 4.46 pp | -1.33 pp | 1.02 pp |

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
| `20261019T193820084-e379d1a4` | Handoff placement | Fail. Points selected-75 fail, or a run-level criterion fails. |

## Limits

A latency experiment counts each transaction that ends, from the first work start to the last finish. The consumer drains the queue after the stop, so each transaction that begins also ends.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetQueue` with one producer and one consumer, with a latency progress point. CLR safety outside this workload is not established.
