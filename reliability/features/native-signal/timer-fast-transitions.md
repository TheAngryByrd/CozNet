# Runtime transitions with 10 µs initial delivery

`artifacts/timer-transitions/20261014T223702889-2004ca26` retains the `LinuxFastTimerTransitions` run.
All eight scenarios and the portable layout outcome pass. The manifest confirms unchanged inputs.
Every scenario records `initialIntervalNanoseconds = 10000`.

| Scenario | No GC | Forced GC | Forced collections | Selected payments without/with GC |
| --- | --- | --- | ---: | ---: |
| JIT | Pass | Pass | 3 | 0 / 0 |
| Managed wait | Pass | Pass | 3 | 0 / 0 |
| Native wait | Pass | Pass | 3 | 0 / 0 |
| Managed/native transitions | Pass | Pass | 2 | 56 / 57 |

Each GC case contains at least one complete collection inside a recorded command interval.
Every selected worker retires, joins, and releases. Every native session closes.
The matrix preserves the existing scenario reach conditions, synthetic credit plan, and timing limits.
The requested child wait stays at most 50 µs, the handler interval at most 5 ms, and the first managed receipt at most 100 ms.
The lease budget stays at most 2 ms. Each forced collection call stays within 250 ms.

This run verifies the controlled transition scenarios with the shorter initial timer interval.
It does not establish general CLR safety, sampled forced-GC capacity, or prediction accuracy.
The source and binary hashes, Docker image identities, exact arguments, raw journals, and address maps remain in the evidence directory.

## Verification after the retirement fix

`artifacts/timer-transitions/20261014T224401368-243cfa02` retains the matrix after the mailbox-retirement fix.
All eight scenarios and the portable layout outcome pass. The manifest confirms unchanged inputs.
Each scenario uses the `10us` argument. The existing timing and reach checks remain unchanged.
This result verifies the controlled transition cases. It does not establish sampled forced-GC capacity or prediction accuracy.

See [the procedure](../../../docsSrc/How_Tos/run-timer-transitions.md) and [the workload reference](../../../docsSrc/Reference/timer-transitions.md).
