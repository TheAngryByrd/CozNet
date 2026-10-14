# Native service accounting

## Native owner identity

`coz_service_owner_thread` returns the native owner's Linux thread ID through a signed 32-bit output buffer.
The controller must call it while the session remains open.
A null buffer returns `EINVAL`. Rejected calls clear a supplied output buffer to zero.
The ID identifies the thread that installed the signal transport. It remains stable until session close.
Scheduler capture must finish before the native owner exits.
Failed capture termination prevents service restoration and requires the isolated-process watchdog.

## Native submission timing

`coz_service_submit_timed` is an optional native diagnostic entry point.
It uses the same submission contract as `coz_service_submit`.
The caller supplies a `CozServiceSubmissionTiming` output buffer of 48 bytes.
The function does not retain the buffer.
A null buffer returns `EINVAL` before submission.

The record contains six unsigned 64-bit timestamps in native monotonic nanoseconds:

| Field | Capture point |
| --- | --- |
| `entered` | Before acquisition of the API mutex |
| `api_locked` | After acquisition of the API mutex |
| `owner_entered` | Before acquisition of the owner mutex |
| `owner_locked` | After acquisition of the owner mutex |
| `owner_finished` | After accepted submission and owner notification, with the owner mutex held |
| `returned` | After the guarded API operation, before return to the caller |

Each call resets the record. Unreached capture points remain zero.
Owner rejection leaves `owner_finished` zero.
Wrong-thread and stale-handle rejection leave all owner timestamps zero.
A failed clock read also produces zero. It does not change the submission result.
The caller must retain accepted command ownership even if timing is invalid.

The portable owner accepts an optional `SubmissionTiming` output pointer.
Ordinary submissions do not perform these additional clock reads.
The native clock implementation remains behind the transport interface.
`LinuxServiceSubmissionTiming` enables managed capture in the initialized sampled workload.
Each controller journal retains `Submission`, including failed calls.
`ManagedEntered` and `ManagedReturned` bracket the native call and client ownership update with `Stopwatch` timestamps.
`Status` is null if the native call did not return a status.
`Native` contains the raw six-field native record.
`SubmissionTiming` contains parsed intervals only after successful, ordered, identity-matched capture.
Managed intervals use ticks. Native intervals use nanoseconds. Their timestamp origins differ.
Invalid timing retains pending command ownership and raw evidence.

Elapsed intervals include both execution and time when the calling thread does not execute.
These timestamps alone cannot identify lock contention, scheduling delay, or runtime suspension.

## Portable owner polling policy

The native `Owner` constructor accepts an optional `OwnerPollSchedule` value.
`yielding()` yields after each active iteration and remains the default.
`bounded()` yields after each group of 64 active iterations.
Every iteration releases the owner mutex before either yielding or acquiring it again.
Idle service resets the polling cycle and waits on the existing condition variable.
The constructor also resets a supplied schedule before service starts.

```cpp
coz::service::Owner owner(transport, 100000, coz::service::OwnerPollSchedule::bounded());
```

The schedule counts iterations. It does not promise a wall-time bound or prevent preemption.
It uses portable C++ operations and does not change thread priority or CPU affinity.
`coz_service_open_with_polling` accepts policy `0` (yield) or `1` (poll-64).
Other policy values return `EINVAL`. The legacy `coz_service_open` entry point selects policy `0`.
The managed `OwnerPollingPolicy` parser accepts exactly `yield` and `poll-64`.
The sampled service commands accept one optional final policy argument. Omission selects `yield`.
Backend records expose `ownerPolling` and `ownerIterationsBeforeYield`.
`LinuxServiceOwnerPolling` compares both policies with submission timing and four scheduler streams.
Deterministic checks cover the 64-iteration cycle, reset, bounded child requests, measured overshoot, release, and shutdown.
The [first live comparison](../../reliability/features/native-signal/service-submission/owner-polling.md) fails the debt limit with both policies.
Neither policy establishes sampled prediction accuracy.

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
