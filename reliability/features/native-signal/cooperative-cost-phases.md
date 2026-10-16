# Cooperative cost phase evidence

This report records one declared diagnostic sequence at `b8bd3fcf8bd5616ae4182f2ee4615adcd2c53559`.
It does not qualify prediction accuracy or observer cost.

Plan: `artifacts/cooperative-cost-phases/20261016T024241100-plan/plan.json`.
Capture: `artifacts/cooperative-cost-phases/20261016T024241100-plan/20261016T024242584-9da702b3`.
Order: zero, 50-percent treatment, intended invalidation after selected payment.
The runner enabled owner drain, direct receipt readiness, finite measurement, pause timing, and dispatch timing.
Each completed case requested 50,000 selected batches with `qualified-sample` cadence.

Retained `summary.json` records zero exits for all three cases and passing protocol analysis.
It records unchanged inputs. Both completed measurements remain unqualified.
Intended invalidation remains invalid. No unchanged capture was repeated.

## Treatment pause phases

All 146 treatment pauses have complete timing records.
The recorded frequency is 1,000,000,000 ticks per second.
Each total sums the endpoint difference across those records.

| Phase | Total ns |
| --- | ---: |
| Started → ProducerStopped | 2,141,041 |
| ProducerStopped → DisabledCountersAccepted | 1,005,692 |
| DisabledCountersAccepted → DrainAcknowledged | 742,934 |
| DrainAcknowledged → BatchValidated | 6,668,090 |
| BatchValidated → SnapshotReady | 220,689 |
| SnapshotReady → SamplesPublished | 1,282,081 |
| SamplesPublished → CallbackReturned | 26,240,063 |
| CallbackReturned → SourceEnabled | 759,279 |
| SourceEnabled → EnabledCountersAccepted | 513,759 |
| EnabledCountersAccepted → CollectorResumed | 152,436 |
| CollectorResumed → Completed | 70,276 |
| Total | 39,796,340 |

Selected payment intervals sum `Finished - Started` to 24,200,458 ns.
Accepted native selected wait totals 79,340 ns.
These quantities overlap with pause phases and must not be added.
The treatment records 76,350 receipt-readiness calls and 1,344 dispatch calls.

## Retained dispatch subset

The subset uses only `Recent` records with all four ordered timestamps.
Each submission must follow the measurement start's `AfterTimestamp`.
Each return must precede the measurement end's `BeforeTimestamp`.
The resulting 126 calls are a retained tail, not a representative sample.

| Operation | Count | Queue ns | Action ns | Return ns |
| --- | ---: | ---: | ---: | ---: |
| PeerSubmit | 27 | 896,183 | 86,232 | 630,415 |
| PeerRead | 27 | 711,414 | 441,156 | 1,334,273 |
| PeerRelease | 27 | 515,038 | 47,073 | 250,076 |
| SelectedSubmit | 15 | 766,410 | 77,170 | 570,067 |
| SelectedRead | 15 | 268,416 | 236,637 | 425,997 |
| SelectedRelease | 15 | 69,314 | 22,837 | 26,124 |

Queue is `Started - Submitted`. Action is `Finished - Started`. Return is `Returned - Finished`.
The observer retains 128 recent calls and independent maxima.
No extrapolation to all calls is valid. Maxima can fall outside measurement.
These brackets include observer work and scheduling. They do not isolate CPU cost.

## Verification and limits

Phase totals, payment intervals, and dispatch groups were recomputed from the three raw records.
The recomputed values matched `phase-summary.json`.
The calculation checked the recorded frequency and ordered timestamps.
The zero case has no pauses. Invalidation has one incomplete pause and no complete pause timing.
Its zero complete-record sum does not measure that pause's duration.

Raw record SHA-256 values:

| Record | SHA-256 |
| --- | --- |
| `0.json` | `751cc1d17b40685995db63f30c59a09a39b6cc71336dabbcf5700d210777ec5c` |
| `50.json` | `369c4483bd319defcdb238e6239c9fecc102072c11a3f5043b2ad58cac0cc145` |
| `invalidate-after-selected-payment.json` | `8b7404a08e6e4b29e4ff19decfe72954c64838a2c8042d9d76233cfba7b64c9a` |

The raw artifacts remain local. This report does not make them available through the published documentation.
Attribution, boundary uncertainty, ledger residuals, and observer cost remain unresolved.
See [the human explanation](../../../docsSrc/Explanations/cooperative-pause-cost.md)
and [the first matched block](cooperative-comparison.md).
