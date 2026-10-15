# Recorded timer wait membership

`native/AnalyzeTimerWaitMembership.py` bounds the overlap of recorded timer waits with an observed measurement window.
The policy is `linux-monotonic-recorded-timer-waits-v1`.
It requires Python 3.10 or later and uses no platform APIs.

## Input contract

The analyzer requires [reconciled timer prefixes](timer-prefix-reconciliation.md) and the [observed clock policy](sample-membership.md).
The final native session must be closed, with all journals archived.
An unresolved final inventory produces no range.
Recorded handler intervals cannot overlap or regress within one worker, including across generations.
Archived and pending captures included in frozen accounting cannot have native timestamps later than that endpoint's upper reading.

The timer clock assumption requires the current Linux `CLOCK_MONOTONIC` backend.
The clock metadata remains a declaration, not binary attestation.
The report retains the sample policy's explicit assumption about unobserved timekeeper anomalies.

## Range calculation

Let `[S0,S1]` and `[E0,E1]` be the frozen start and end brackets.
Let `[H0,H1)` be one recorded handler interval and `W` its recorded wait duration.
The native backend measures the wait inside that handler interval.
The record does not contain the exact wait start and end.

| Quantity | Calculation |
| --- | --- |
| Guaranteed overlap `G` | Length of `[H0,H1)` intersected with `[S1,E0)` |
| Possible overlap `P` | Length of `[H0,H1)` intersected with `[S0,E1)` |
| Lower recorded wait | `max(0, W - ((H1 - H0) - G))` |
| Upper recorded wait | `min(W, P)` |

The lower limit subtracts all handler time outside the guaranteed window.
The upper limit caps wait by the possible overlap.
The calculation sums these limits separately for each worker.
It does not require a specific placement of the wait inside the handler interval.
The limits can be conservative rather than tight.

## Output

| Field | Meaning |
| --- | --- |
| `comparisonDomainAccepted` | Prefix, clock, closure, and interval checks passed. |
| `workers` | Per-worker diagnostics, or null after rejection. |
| `recordedChildren`, `recordedWait` | Final accepted child count and total wait for that worker. |
| `recordedWaitInObservedWindow` | Lower and upper recorded overlap, in nanoseconds. |
| `frozenAcceptedWaitDelta` | Controller-accounted end wait minus start wait. |
| `recordedWorkloadFailures` | Original failures, including failures in accepted comparison domains. |
| `inputSha256`, `sourceHashes` | Input and analyzer identities. |

Native execution and controller acceptance can occur on different sides of an endpoint.
A child accepted after the endpoint can contribute recorded wait inside the window.
These diagnostics retain both observations without replacing frozen accounting.

The range covers recorded waits under the stated clock assumptions.
It does not bound missing native work, verify instruction eligibility, or measure exact CPU time.
Wait duration can include descheduling inside the wait interval.
Physical-clock accuracy, exact event ordering, native-tail bounds, and prediction accuracy remain unverified.
The analyzer does not combine worker waits into global delay or change prediction eligibility.

See [the procedure](../How_Tos/inspect-timer-wait-membership.md) and [measurement boundaries](measurement-boundaries.md).
