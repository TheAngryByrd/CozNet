# Timer prefix reconciliation

`native/AnalyzeTimerPrefixes.py` reconciles frozen controller accounting with final accepted timer journals.
It requires `Accounting.Timer` at both measurement boundaries.
It supports Python 3.10 or later and uses no platform APIs.

## Input contract

The capture must contain a complete measurement window and the final journal inventory.
Each pending lease must match its slot, command, and generation.
Nonzero generations cannot repeat within one slot.
A closed native session cannot retain pending ownership.

The analyzer checks native aggregates, child records, first receipt bounds, and accepted prefix totals.
It checks released waits against the archived journal prefix at each boundary.
Accepted child counts, native capture timestamps, and ownership flags cannot regress for the same pending operation.
A frozen prefix cannot omit a child first observed before its last accepted capture.
Equal clock readings do not establish observation order.
A start operation must remain owned or enter the archived prefix before the endpoint.

These checks do not establish instruction eligibility or full native protocol validity.
The analyzer does not use rejected observations as accepted work.

## Report fields

| Field | Meaning |
| --- | --- |
| `reconciled` | The recorded prefixes agree with the final accepted inventory. |
| `reasons` | Input errors that prevent reconciliation. |
| `workers` | Separate selected and peer accounting, or null on rejection. |
| `acceptedWaitDelta` | End accepted wait minus start accepted wait, in nanoseconds. |
| `laterAcceptedChildren`, `laterAcceptedWait` | Final accepted suffix beyond the frozen endpoint prefix. |
| `startFailure`, `endFailure` | Controller failures copied into the frozen worker records. |
| `journalFailures` | Failures in final journal records. |
| `recordedWorkloadFailures` | Original workload failures, including failures in reconciled captures. |
| `nativeSessionClosed` | Recorded native session closure. |
| `inputSha256`, `sourceHashes` | Input and analyzer source identities. |

An unavailable final accepted observation produces null suffix fields.
Null does not mean zero wait.
Later acceptance does not mean later native execution.
An accepted child can finish before the endpoint and reach the controller afterward.

Worker wait deltas are not global delay.
The analyzer does not add both workers' waits to estimate global delay.
`nativeEligibilityVerified`, `nativeTailBoundEstablished`, and `predictionAccuracyEstablished` remain false.
Reconciliation does not make a failed workload eligible for prediction.

See [measurement boundaries](measurement-boundaries.md) and [the reconciliation procedure](../How_Tos/reconcile-timer-prefixes.md).
