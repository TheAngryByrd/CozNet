# Nominal service and pause timing

Capture: `artifacts/nominal-service-phases/20261016T085054588-6c87658a`.

The capture uses 50,000 selected batches, interval 8192, nominal-event credit, prepared receipts, and owner/controller execution.
Pause timing and service timing are enabled. Dispatch timing is disabled.
All three processes exit zero and pass their expected outcome and replay checks. Source and binary inputs remain unchanged.

The zero case freezes 249 accepted samples and 248.591983–248.631401 ms elapsed time.
The treatment freezes 307 accepted samples, 112.5 ms credit, and 113.034745 ms global delay.
Its elapsed range is 325.043219–325.210503 ms. Its effective range is 212.008474–212.175758 ms.
Both measurements remain unqualified. The intended CLR invalidation retains an invalid measurement.

## Treatment brackets

The treatment records 59 complete selected pauses.

| Pause phase | Total ms |
| --- | ---: |
| Start to producer stopped | 1.86996 |
| Producer stopped to disabled counters | 0.74751 |
| Disabled counters to drain acknowledgment | 0.61205 |
| Drain acknowledgment to batch validation | 3.02729 |
| Batch validation to snapshot | 0.23493 |
| Snapshot to sample publication | 1.21755 |
| Sample publication to callback return | 3.55128 |
| Callback return to source enabled | 0.42187 |
| Source enabled to enabled counters | 0.22891 |
| Enabled counters to collector resumed | 0.13614 |
| Collector resumed to completion | 0.22637 |

| Measurement service path | Calls | Total ms |
| --- | ---: | ---: |
| Skipped | 54,702 | 1.6245 |
| Idle | 79 | 0.7424 |
| Pending | 44,959 | 14.7300 |
| Receipt | 283 | 17.2974 |
| Submit | 283 | 4.4270 |
| Receipt and submit | 1 | 0.0504 |

The zero case records 100,000 skipped calls in 2.6370 ms and 249 idle calls in 1.1514 ms.
Pause intervals and service intervals can overlap. Their sums are not independent costs.
Wall brackets include scheduling and instrumentation overhead. They do not measure CPU ownership.
This separate timed capture cannot explain the gap in the earlier untimed four-arm block.
Selected-thread CPU observations are the next discriminator between CPU consumption and elapsed time off CPU.

See [CPU interpretation](../../../docsSrc/Explanations/selected-cpu.md), [service timing](../../../docsSrc/Reference/cooperative-service-timing.md), and [nominal accuracy evidence](nominal-event-credit.md).
