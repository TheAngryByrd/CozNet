# Native service accounting

This reference describes internal prototype components in `tests/CozNet.LinuxPerf` and `src/CozNet`.
They are not public profiling APIs.
The `--sampled-service` workload entry point connects both adapters through `NativeServiceController`.
The first live zero-speedup case passes. The 50-percent case fails the lease capacity bound during warmup.
See [the retained evidence](../../reliability/features/native-signal/sampled-service/README.md).

The controller retains terminal journals and pending raw observations in its JSON output.
Boundary snapshots include pending service journals. Native public fields remain in serialized records.
Observed peer debt subtracts confirmed waits from an active lease before terminal settlement.
After settlement, committed waits contain that amount and the active prefix contributes zero.

## Request ownership

`SignalAccounting` tracks bounded requests through these states:

| State | Meaning |
| --- | --- |
| `Reserved` | Accounting owns a request that has not been submitted. |
| `Submitted` | Accounting retains ownership while the native sequence is unavailable. |
| `Sequenced` | The request has a bound native sequence. |

`rejectedBeforeSend` accepts only `Reserved` requests.
`sent` binds a reserved or submitted request to an increasing native sequence.
`isAwaitingSequence` identifies a matching submitted request.

## Selected service: `ServiceSelectedAdapter`

Slot 0 handles the selected target. The adapter requires a matching submitted request.
An active snapshot contains no completed children and no measured wait.
A completed snapshot contains exactly one child and no outstanding delivery.

The adapter checks identity, command, budget, aggregate wait, backend outcome, address eligibility, phase timing, and first managed receipt.
It binds the native sequence and settles the request through local immutable states.
It returns the final accounting state only after all checks pass.

Selected service stops after one child, including a partial wait.
A deferred child pays zero debt. An interrupted child pays its measured wait.
Measured overshoot remains intact. Unpaid debt remains available for a later check.
Invalid observations preserve accounting ownership. A settled adapter rejects replay.

## Peer service: `ServiceAccountingAdapter`

The peer adapter retains a finite lease and an immutable prefix of accepted children.
Each observation uses the latest accounting state.
The accepted child count, final native sequence, and measured wait must match that state.
A zero-wait child advances the journal position even though the measured sum does not change.

Previously accepted records and first receipt bounds cannot change.
Repeated snapshots do not apply accepted children again.
An invalid suffix publishes neither a partial cursor nor a partial accounting state.

Normal completion requires a terminal child and no outstanding delivery.
The complete measured sum settles the lease once, including overshoot.
New selected credit does not enlarge an existing lease budget.

Cancellation requires prior managed cancellation intent.
The stop receipt must match identity, child count, final sequence, measured total, and zero outstanding children.
An empty cancellation pays no debt.

## Receipt timing: `NativeServiceClient`

The snapshot clock provides a lower bound for first managed receipt.
A native monotonic clock read after the snapshot returns provides the upper bound.
Both use the clock domain of the native send timestamp.
The client preserves each child's first bounds across repeated snapshots.

`ServiceReceiptTiming.Parse` requires ordered, nonzero timestamps and a successful clock read.
The upper bound minus the child's send time cannot exceed 100 ms.
A late upper bound fails this conservative gate. It does not establish the exact receipt time.

## Limits

| Check | Limit | Scope |
| --- | ---: | --- |
| Requested wait | 50 µs | Each native child |
| Measured handler duration | 5 ms | Each native child |
| Acknowledgment, including first managed receipt | 100 ms | Each native child |
| Observed debt | 2 ms | Sampled workload gate |
| GC call | 250 ms | Runtime workload gate |
| Physical prediction error | Two percentage points | Prediction comparison gate |

Adapter checks do not establish that sampled workloads satisfy the last three gates.

## Lifetime and platforms

The opening OS thread owns client submission, snapshots, cancellation, release, and close.
Target threads perform their own registration and retirement.
Release requires a complete snapshot copy with no outstanding child.
Close requires release, target retirement, and host-side joins.
An isolated-process watchdog bounds unresolved ownership.
Registered target threads remain alive while delivery ownership remains unresolved.
Such a failure prevents successful joins and restoration. A delivery-stop notification alone does not authorize target thread exit.

The native owner uses portable C++20. Backend interfaces isolate OS operations.
The current signal transport and service ABI require Linux x64.
Pure managed adapter checks have retained Windows and Linux results.
Those results do not establish native transport support on Windows or macOS.

See [verification steps](../How_Tos/verify-native-service.md) and [measurement validity](../Explanations/native-service-validity.md).
