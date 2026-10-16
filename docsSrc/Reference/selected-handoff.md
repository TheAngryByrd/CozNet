# Selected scheduling handoff

The bound selected-payment workload has an opt-in scheduling experiment.
`COZNET_SELECTED_HANDOFF=1` enables it. Unspecified or `0` disables it. Other values reject activation.
The mode requires owner draining and an explicit selected cadence.

## Admission

At selected loop checkpoints, the owner publishes available samples and checks peer service.
Every sample passes the existing debt gate before handoff admission.
If a peer request remains pending and peer debt is at least 500,000 ns, the owner makes one handoff attempt.
After that attempt, the owner drains available samples and checks peer service again before selected work continues.
The mode does not loop until the peer completes.

`ISelectedHandoffBackend` supplies the platform operation.
The default backend calls `Thread.Yield()` once.
`Attempted` means that the call returned. It does not establish a context switch or peer execution.
`Unavailable` fails the workload with `PlatformNotSupportedException`.
Unknown backend results fail with `InvalidDataException`.
Backend exceptions retain their original identity.

## Evidence fields

| Field | Meaning |
| --- | --- |
| `selectedHandoffEnabled` | Parsed activation Boolean. |
| `selectedHandoffAttempts` | Backend calls attempted, including a call that throws or reports unavailable. |

The runner option is `-SelectedHandoff`.
The runner binds activation to the manifest and case outcomes.
Selected-payment replay requires owner draining and schema 4 when enabled.
Positive attempt counts require qualifying peak peer debt and a submitted peer request.
Zero-speedup and disabled records require zero attempts.
These aggregate checks do not reconstruct every handoff decision.
They cannot reject every inflated attempt count.

## Limits

The handoff grants no delay credit and creates no sampling eligibility epoch.
Only accepted measured waits settle debt.
The 2 ms debt, 50 microsecond requested-child, 5 ms handler, and 100 ms receipt limits remain unchanged.
The sampling source remains live across the handoff.
The helper executes outside the selected application method. Existing injected instruction overhead remains unresolved.

A scheduling handoff adds treatment-dependent overhead.
Enabled mode adds a debt observation even when no request qualifies for handoff.
Zero attempts do not establish zero added overhead or unchanged failure-detection timing.
Neither native Coz nor JCoz establishes that this policy preserves causal predictions.
Capacity success alone cannot establish a profiling fix.
Physical optimization comparisons must still meet the existing two-percentage-point prediction gate.
Arbitrary OS preemption can still violate wall-time limits.

The first controlled comparison passes both ordinary-CPU treatments and the CPU 0 control.
The CPU 0 handoff treatment fails the handler bound after 52 attempts.
This comparison does not establish a capacity improvement. The default remains disabled.
The invalidation cases recorded no handoff attempts. Invalidation after a handoff remains unverified.

See [the comparison procedure](../How_Tos/compare-selected-handoff.md),
[bound scheduler capture](bound-scheduler-capture.md), and [upstream scheduling behavior](../Research/native-owner-scheduling.md).
