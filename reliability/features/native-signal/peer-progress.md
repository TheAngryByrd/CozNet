# Retained peer progress analysis

Source records: `artifacts/receipt-readiness-single/20261015T223518168`.
Analysis capture: `artifacts/peer-progress/20261015T230353667`.
The manifest hashes all twelve source records and the copied analysis sources.
The runner executes the copied analyzer and confirms unchanged inputs.
No new timing workload ran.

All twelve records pass existing replay and diagnostic validation.
The original workload outcomes remain unchanged. All four treatments remain failed.

| Treatment | Diagnostic result |
| --- | --- |
| Controller 1 | Submit bracket is no longer retained. |
| Direct 1 | First failure capture contains no published child. |
| Direct 2 | First-child admission follows debt by 4,293,347 through 6,017,391 ns under the timing model. |
| Controller 2 | Submit bracket is no longer retained. |

Both direct invalidation cases also have conditional bounds:

| Case | First-child admission after debt |
| --- | --- |
| Direct 1 invalidation | 5,616,384 through 7,285,008 ns |
| Direct 2 invalidation | 7,489,455 through 9,548,718 ns |

Both cases failed peer debt before completing their intended invalidation scenario.
They do not establish successful invalidation handling.
Both controller invalidation cases pass their original checks but have no pending reservation for this projection.
All four zero-speedup cases have no failure boundary and remain unavailable for this analysis.

For direct trial 2, the submit action is `[451632591753844, 451632593477888]` managed ticks at 1 GHz.
The first native `Armed` is `451632593310715`. Its `Started` is `451632610067684`.
The native elapsed duration is 16,756,969 ns.
The debt timestamp is `451632604217466` managed ticks.
The first-child finish projects 4,355,444 through 6,079,488 ns after that debt observation.
These projections use comparable clock rates. They do not require matching clock epochs.

The later failure snapshot contains ten completed children and 500,048 ns measured payment.
That later state does not demonstrate payment before the debt gate.
No scheduler cause follows. A zero-child snapshot can include an executing child that has not published its record.

The clock model treats reported elapsed-time rates as equal after unit conversion. It includes no independent clock-error bound.
This is an accepted condition for the diagnostic, not a calibrated physical guarantee.
The 128-entry dispatch history can evict submit records during header polling.
This selection limit prevents a complete direct-versus-controller timing comparison in these captures.

Fifteen controlled checks cover epoch independence, conservative rounding, overlap, invalid clocks, request identity,
missing evidence, duplicate submit anchors, full replay rejection, preserved runtime failure, and UTF-8 file output.
They also check submit ordering, already-paid commands, incomplete submissions, and both complete projected intervals.
Ordering is relative to the retained debt observation, not the first instant that debt crossed its limit.
The shared Python implementation has no platform API. The captured live evidence remains Linux-specific.

See [the reference](../../../docsSrc/Reference/cooperative-peer-progress.md) and [the readiness comparison](receipt-readiness.md).
