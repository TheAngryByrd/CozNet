# Recurring service timing

Capture: `artifacts/recurring-service-timing/20261016T074517040-2d0c5fa1`.

The prepared finite diagnostic uses interval 8192 and 50,000 qualified-sample batches.
Owner draining, direct receipt readiness, pause timing, dispatch timing, and service timing are enabled.
The capture retains zero speedup, 50-percent speedup, and expected invalidation after selected payment.
All three processes exit zero and pass replay. Source and binary hashes remain unchanged.
All four Linux managed contract groups pass. The host managed group also passes.
The Python suites pass 31 sampled-payment checks and 106 selected-payment checks.
PowerShell outcome checks reject absent or mismatched service timing activation.
The final verifier adds a regression check for impossible remaining calls outside the measurement window.
All 32 sampled-payment checks and 106 selected-payment checks pass with that correction.
The retained capture passes the stricter verifier without another timing run.

## Measurement-window observations

Times use the managed frequency of 1,000,000,000 ticks per second.
Each row describes `serviceTiming.Measurement` in the 50-percent case.

| Activity | Calls | Total ms | Maximum ms |
| --- | ---: | ---: | ---: |
| Skipped | 58,822 | 1.469014 | 0.028028 |
| Idle | 76 | 0.685257 | 0.541143 |
| Pending | 40,927 | 10.429893 | 0.061000 |
| Receipt | 224 | 35.374432 | 9.335967 |
| Submit | 224 | 21.194016 | 3.896741 |
| ReceiptAndSubmit | 0 | 0 | 0 |

No service call fails. Lifetime and measurement buckets agree in this capture.
Zero speedup records 100,000 skips totaling 2.952327 ms and 262 idle calls totaling 1.257984 ms.
The expected invalidation case retains one receipt call and one submission call without a service failure.
CLR invalidation occurs outside the service failure counter.

## Interpretation limits

Receipt and submission paths account for more recorded service time than pending-only checks in this treatment.
Their controller operations and first-use costs remain candidates for further measurement.
The buckets do not isolate individual native calls, lock contention, CPU execution, or scheduling delays.
The observer adds cost. Selected-pause brackets can overlap service brackets.
This diagnostic does not establish prediction accuracy or a performance improvement.

See [the reference](../../../docsSrc/Reference/cooperative-service-timing.md) and [capture procedure](../../../docsSrc/How_Tos/measure-cooperative-service.md).
