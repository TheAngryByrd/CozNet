# Target-local transaction profile

Evidence: `artifacts/target-local-transaction/20261019T131709144-4aa0a3ef`.

`TargetLocalTransactionVerify` ran 5 blocks in the Linux x64 container, with kernel `5.15.153.1-microsoft-standard-WSL2`, x64, and 4 CPUs.
Each block runs one `transaction-profile` process and seven `plain-transaction` processes in a random order.
The profile process runs `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75. Each experiment runs for 120 ms with a latency progress point. Worker 0 begins a transaction, runs `Work.selected 40`, and enqueues it in a `TargetQueue` of 4 items. Worker 1 dequeues it, runs `Work.peer 8`, and ends it. The prediction is `1 - (VL / V) / (VL0 / V0)`, where VL sums the virtual latency of the transactions, each its wall latency minus the global delay added during it and at least 0, and V counts the departures. The real gain is `1 - W / W0`, where W is the mean wall latency.
The criteria are in [the latency point plan](../../../lode/plans/target-local-transactions.md) and [the transaction delay plan](../../../lode/plans/target-local-transaction-delay.md). The runner hashes that file, and all source hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current run fails.** Points selected-25, selected-50 fail, or a run-level criterion fails. The run is kept, and no rerun replaces it.

| Point | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-25` | 5 / 5 | 24.62% | 18.72% | 7.25 pp | 8.04 pp | Fail |
| `selected-50` | 5 / 5 | 48.33% | 40.58% | 7.76 pp | 13.44 pp | Fail |
| `selected-75` | 5 / 5 | 66.12% | 62.03% | 2.63 pp | 7.29 pp | Pass |
| `peer-25` | 5 / 5 | 3.71% | 3.69% | 1.59 pp | 3.99 pp | Pass |
| `peer-50` | 5 / 5 | 7.87% | 7.88% | 3.58 pp | 4.69 pp | Pass |
| `peer-75` | 5 / 5 | 11.33% | 10.65% | 2.32 pp | 5.35 pp | Pass |

The producer sets the rate at every point, so the latency of a transaction is about the sum of both parts. The model gives `Work.selected` 20.8%, 41.7%, and 62.5%, and `Work.peer` 4.2%, 8.3%, and 12.5%. The model leaves out the queue handoff and any queue wait.

## Work ratio

The work ratio of a worker is its wall time outside measured child payments for each visit, divided by the same value of the block baseline. The table shows the median for worker 0 and worker 1.
All workers share the visits of the progress point. Blocked time, overhead, and the visits of the other worker also change the ratio, so a ratio alone does not show faster work. The ratio is a report, not a criterion.

| Point | Median work ratio |
| --- | --- |
| `selected-25` | 0.999, 0.929 |
| `selected-50` | 0.988, 0.865 |
| `selected-75` | 0.996, 0.729 |
| `peer-25` | 1.001, 1.050 |
| `peer-50` | 1.011, 1.113 |
| `peer-75` | 1.008, 1.145 |

The median work ratio by run order in the process is order 0: 1.000, 1.000; order 1: 1.015, 1.000; order 2: 1.014, 1.000; order 3: 1.000, 1.050; order 4: 0.988, 1.000; order 5: 0.997, 0.912; order 6: 0.996, 0.929. The seed fixes the order of each block, so order and point are confounded.
The warm-up experiment passed in 5 of 5 blocks.



## Queue handoff

The handoff of an item is the time from just before its enqueue to the return of the dequeue that takes it. The table gives the median over valid blocks of the mean handoff, in the profile experiment and in the real arm. The baseline row compares the profile baseline with the plain original arm, and it is the only row that compares equal measures. At a speedup above 0, a profiled handoff also includes the delay that the producer pays in `Enqueue`. At every point, the handoff also includes the time that an item waits in the queue.

| Point | Median mean handoff, profile | Median mean handoff, real arm |
| --- | ---: | ---: |
| baseline | 48.6 us | 145.2 us |
| `selected-25` | 52.0 us | 127.2 us |
| `selected-50` | 55.8 us | 91.9 us |
| `selected-75` | 143.1 us | 52.8 us |
| `peer-25` | 60.5 us | 141.5 us |
| `peer-50` | 74.7 us | 140.0 us |
| `peer-75` | 82.9 us | 138.0 us |

The second value is the signed difference between the dequeue return and the enqueue return of the same item, as a mean. It can be negative, because the consumer can take an item before the producer reads its time after `Enqueue`. The table gives the median over valid blocks of the baseline values.

| Arm | Mean enqueue time | Mean dequeue return minus enqueue return | Share of spin takes |
| --- | ---: | ---: | ---: |
| Profile baseline | 20.4 us | 27.8 us | 0.0% |
| Plain original | 42.0 us | 103.2 us | 0.0% |

## Blocks

| Block | Valid | selected-25 | selected-50 | selected-75 | peer-25 | peer-50 | peer-75 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Yes | 0.89 pp | 3.91 pp | 2.63 pp | -0.25 pp | -4.69 pp | -4.40 pp |
| 2 | Yes | 2.83 pp | 8.20 pp | 6.45 pp | 1.59 pp | 4.41 pp | -1.75 pp |
| 3 | Yes | 7.25 pp | 7.76 pp | 2.40 pp | -0.06 pp | 1.90 pp | 2.32 pp |
| 4 | Yes | 7.45 pp | 4.73 pp | 7.29 pp | -3.99 pp | -3.58 pp | -1.60 pp |
| 5 | Yes | 8.04 pp | 13.44 pp | 0.41 pp | 2.24 pp | 1.59 pp | 5.35 pp |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261019T110105861-414dba20` | First transaction profile run | Fail. All six points fail. The real gains follow the model, but the `peer` predictions stay near 5% at each speedup. |
| `20261019T113356248-f1cbfddc` | Per-transaction virtual latency | Fail. `selected-50` median 7.09 points and `peer-25` median 5.47 points. The other four points pass. |
| `20261019T121118409-e589636a` | Transaction queue handoff | Fail. `selected-25` median 7.58 points and `selected-50` median 10.07 points. The other four points pass. |
| `20261019T124914850-b10c2f47` | Spin before a queue wait | Fail. `selected-25` one block of 27.61 points, `selected-50` median 8.32 points, and `peer-50` one block of -12.59 points. |
| `20261019T131709144-4aa0a3ef` | Handoff parts | Fail. Points selected-25, selected-50 fail, or a run-level criterion fails. |

## Limits

A latency experiment counts each transaction that ends, from the first work start to the last finish. The consumer drains the queue after the stop, so each transaction that begins also ends.
The profile covers CPU-bound threads on Linux x64 that block only at a `TargetQueue` with one producer and one consumer, with a latency progress point. CLR safety outside this workload is not established.
