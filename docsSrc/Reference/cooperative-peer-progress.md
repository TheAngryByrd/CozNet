# Cooperative peer progress analysis

`native/AnalyzeCooperativePeerProgress.py` analyzes one selected-payment JSON record.
It runs the existing selected-payment replay before deriving any timing result.
It does not change payment acceptance, debt limits, or the workload outcome.
The Python analyzer has no platform API dependency.

## Timing model

The first child's native `Armed` timestamp occurs inside the matching successful `PeerSubmit` controller action.
The analyzer matches the pending command, generation, budget, and request start to the failure boundary and native capture.
It requires the first child to identify worker 2, the bound code version, and site 2.
It selects one retained peer submit with that command. Submit dispatches record generation zero before the native generation returns.
Duplicate submit commands reject the evidence.

The analyzer subtracts the first `Armed` value from the first child's `Started` or `Finished` value.
It projects that native duration from both ends of the managed submit-action bracket.
The result is relative to `LastObservedDebt.Timestamp`, in nanoseconds.
This timestamp is an observation. It does not identify when debt first crossed its limit.

This calculation assumes comparable elapsed-time rates between native nanoseconds and managed timestamp ticks.
The model treats those rates as equal after unit conversion. It includes no independent clock-error bound.
It does not assume equal clock epochs. It does not calibrate clocks or establish physical event accuracy.
Integer conversion rounds the lower endpoint down and the upper endpoint up.

`Started` marks native child admission. It is not the exact start of the spin wait.
`Finished` is the first child's completion timestamp. It is not journal retirement, mailbox quiescence, or managed receipt acceptance.

## Output

| Field | Meaning |
| --- | --- |
| `evidenceVerified` | Existing replay and this analysis accepted the record. |
| `casePassed`, `completed` | Original workload outcomes, preserved by the analyzer. |
| `acceptedPeerPayments` | Number of accepted peer receipts. |
| `assumptions` | Clock-rate and native-arming assumptions used by the projection. |
| `peer.state` | `bounded` for a conditional projection, or `unavailable`. |
| `peer.reason` | Missing, incomplete, invalid, or unretained evidence that prevents projection. |
| `peer.debtLimitExceeded` | Whether the retained selected or peer debt exceeds 2 ms. |
| `peer.firstChildStart`, `peer.firstChildFinish` | Conditional bounds relative to the debt observation. |

Each projected interval contains `earliestNanosecondsFromDebt`, `latestNanosecondsFromDebt`, and `relation`.
`after` requires a strictly positive lower endpoint. `before` requires a strictly negative upper endpoint.
An interval that contains or touches zero reports `overlaps`.

A missing submit can result from dispatch-history eviction. The analyzer does not substitute another request's submit.
A zero-child capture reports `no-published-child`. An executing child can remain unpublished, so this does not prove absent peer execution.
Unavailable evidence does not become a zero duration or a successful workload.

The process exits zero when the diagnostic evidence verifies, even if the original workload failed or projection is unavailable.
Invalid evidence exits one and records an error. File-output failures remain process errors.

See [the procedure](../How_Tos/analyze-cooperative-peer-progress.md) and [the interpretation](../Explanations/receipt-readiness.md).
