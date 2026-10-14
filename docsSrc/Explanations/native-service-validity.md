# Native service ownership and measurement validity

The native service prototype separates request ownership from measurement acceptance.
The native owner retains delivery state until it can account for each child and safely stop delivery.
The managed adapters accept only journals that satisfy accounting and timing contracts.

A native request can finish safely while its observation remains invalid.
For example, a late first managed receipt can fail the 100 ms gate after native delivery has stopped.
Target joins and session closure establish teardown. They do not turn the rejected observation into a valid profiling result.

The selected adapter accepts one child per request.
This preserves the next selected sampling check when a child pays only part of the debt.
The peer adapter accepts a sequence of bounded children against one fixed lease.
This lets native service continue without a managed submission for each child.

Both adapters preserve measured waits, including overshoot.
Clamping a measured wait to the requested budget would conceal an intervention cost and distort subsequent debt accounting.
Repeated snapshots preserve the original receipt bounds so observation cannot restart the acknowledgment deadline.

Controlled checks establish journal parsing, settlement, rejection, and target lifetime behavior.
The retained selected live fixture uses an ineligible instruction range and receives a deferred child.
It does not establish measured intervention in eligible managed code.

The sampled controller now has a native service path.
Its first live zero-speedup case passes. The treatment fails the peer lease capacity bound during warmup.
See [the retained evidence](../../reliability/features/native-signal/sampled-service/README.md).
Further diagnostics associate most first-use controller delay with runtime compilation.
In the traced treatment, compilation intervals cover at least 21.15 ms of the first drain's 22.80 ms.
The first peer journal completion shows the same pattern.
See [the startup analysis](../../reliability/features/native-signal/service-startup/README.md).
An initialization phase now exercises managed paths before native backend creation.
A separate native setup request runs while target workers wait at their start gate.
Synthetic credit and setup journals remain separate from live measurements.
Both initialized treatments still fail the unchanged debt bound during warmup.
See [the initialization comparison](../../reliability/features/native-signal/service-initialization/README.md).
Initialization reduces the measured first-use cost. It does not establish stable service capacity.
Native service capacity and physical prediction accuracy remain unverified.
The later [scheduler diagnostic](../../reliability/features/native-signal/service-submission/scheduler.md) fails during measurement with valid scheduler capture.
It exposes a 2.413 ms interval from the last handler clock reading to native owner observation.
Controller and target switch-out intervals cover only small parts of that gap.
That three-thread capture does not include the owner.
The later [owner capture](../../reliability/features/native-signal/service-submission/owner-scheduler.md) records a 6.240 ms acknowledgment-observation interval.
Owner switch-out time overlaps 6.237 ms of that interval. The record marks the switch-out as preemption.
The owner must observe each acknowledgment before it can issue the next bounded child.
This dependency can interrupt continued service when the owner is switched out.
The evidence does not identify why preemption occurred or justify changing debt limits.
The public engine still supports only explicit serial, synchronous CPU regions.

See the [accounting reference](../Reference/native-service-accounting.md) for exact contracts.
See [verification steps](../How_Tos/verify-native-service.md) for the controlled checks.
