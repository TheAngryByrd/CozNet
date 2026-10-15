# Owner draining and payment capacity

Owner-driven collection removes the background reader from sample publication.
It does not remove controller handoffs, peer scheduling, or sampled observer CPU.
The first one-CPU comparison fails in both collection modes.

## What the constrained comparison establishes

The comparison uses the same binaries and a declared background, owner, owner, background order.
All cases use guest CPU 0, qualified-sample cadence, and 50,000 requested batches.
Child CPU capture and pause timing are enabled.
The CPU restriction also changes processor availability for runtime policy.

| Trial | Completed treatment batches | Failure |
| --- | ---: | --- |
| Background 1 | 8,218 | Peer debt 2,499,921 ns |
| Owner 1 | 77 | Request-to-receipt interval rejected |
| Owner 2 | 561 | Peer debt 2,499,927 ns |
| Background 2 | 9,559 | Peer debt 2,000,056 ns |

All zero-speedup cases complete. All expected-invalidation cases pass.
Replay verifies all twelve records, including the four failed treatments.
No retained handler exceeds the 5 ms limit.

The local capture is `artifacts/owner-drain-single-cpu/20261015T212121304`.
It retains command arrays, the runner, input hashes, raw records, and replay results.
These local artifacts are not part of the published documentation.
The result does not establish prediction accuracy or an isolated overhead estimate.

## Fresh credit can exceed available service

The second owner treatment receives three new credited samples while peer command 5 remains pending.
Their timestamps follow native arming by approximately 4.75, 10.67, and 12.70 ms.
Each adds 500,000 ns of virtual credit.
The request budget plus these credits exceeds the 2 ms peer-debt bound.
The first native child starts approximately 22.00 ms after arming.

These credits are not an old backlog from a background reader.
Owner draining cannot make the peer execute before the operating system schedules it.
The capture does not identify the reason for the delayed first child.
The shared monotonic-clock contract supports the ordering; it is not an independent clock calibration.

## A native receipt is not the managed receipt endpoint

The first owner treatment rejects its first peer request at the receipt-interval check.
Its native children finish, and a native receipt clock reading occurs about 65.42 ms after the managed request start.
The failed managed interval check occurs after the controller call returns.
Its rejected endpoint is not retained.
The outer failure capture starts about 112.03 ms after request start, but includes later error handling.

The native clock reading cannot replace the missing managed endpoint.
The next diagnostic must retain that endpoint and the request identity.
A larger receipt limit would conceal the distinction rather than explain it.

The workload now [retains rejected parser arguments](../Reference/cooperative-managed.md#rejected-managed-receipt-intervals).
Controlled checks cover both slots and serialized failure evidence.
The first new single-CPU capture fails peer debt before reaching receipt rejection.
The older missing endpoint remains unknown.

## Native Coz applies a batch before its delay check

In [Linux Coz's processing loop][processing], the target applies selected credits from the captured ring batch before calling `add_delays`.
The [iterator][iterator] captures one ring head. It does not chase a moving producer.
The [delay handler][delay] then publishes excess local delay or pays outstanding debt.

By default, CozNet checks debt and services peer receipts after each published sample.
Receipt acceptance can therefore occur between historical samples from one captured batch.
Later selected credits can reduce selected debt before a full batch has been published.
This source difference does not prove incorrect causality or justify removing the 2 ms gate.
Any batch-order experiment must retain intermediate debt evidence and exact receipt identity.

Neither inspected processing path guarantees complete observer-CPU exclusion.
Native Coz stops its sampler around waiting, not around record processing.
CozNet owner drains also run while the source is enabled.
The credit rule is unchanged, but owner drain CPU can enter sample periods.

## Batch service does not establish stable capacity

The opt-in batch mode services the peer after each published batch.
It still checks both debts after every sample. An intermediate failure stops processing before later credits can change that debt.
This preserves CozNet's acceptance limit while testing one scheduling difference from native Coz.

The one-CPU comparison uses owner draining and a declared per-sample, batch, batch, per-sample order.
Each case requests 50,000 batches with qualified-sample cadence, pause timing, and child CPU capture.
All cases use the same binaries and guest CPU 0.

| Trial | Treatment batches | Publication service calls / processed samples | Outcome |
| --- | ---: | ---: | --- |
| Per-sample 1 | 4,791 | 283 / 284 | Peer debt 2,499,949 ns |
| Batch 1 | 4,672 | 236 / 266 | Peer debt 2,499,939 ns |
| Batch 2 | 2,971 | 146 / 180 | Peer debt 2,499,929 ns |
| Per-sample 2 | 50,000 | 2,685 / 2,685 | Passed |

All zero-speedup and expected-invalidation cases pass. Replay verifies all twelve records and preserves the three treatment failures.
The local capture is `artifacts/batch-service-single/20261015T215459946`.
Publication service counts exclude service at probes and outside publication. They do not measure controller handoffs or their cost.
Incomplete treatment records cannot support an equal-work overhead comparison.

An earlier unrestricted batch treatment also fails after 30,203 batches.
Its failed peer journal retains a child with 9,374,041 ns of measured wait, above the 5 ms limit.
This is separate from the one-CPU debt failures.

These results do not support changing the default or relaxing a bound.
Reducing publication service calls does not ensure prompt peer execution or bounded native waits.
The next investigation must distinguish controller handoff time from native execution and receipt-observation time.
See [the retained evidence](../../reliability/features/native-signal/sample-publication.md) and [comparison commands](../How_Tos/run-bound-selected-cadence.md#compare-peer-service-after-sample-batches).

## Controller action exit is not caller observation

The [dispatch diagnostic](../Reference/cooperative-managed.md#controller-dispatch-timing) separates submission, controller action, and caller observation.
Its first unrestricted treatment completes 50,000 batches with 29,456 controller calls.
Session creation supplies all four global maxima in that run. Those maxima do not describe steady-state payment.

The one-CPU treatment fails after 19,944 batches at peer request 154.
The native journal retains a 6,167,752 ns child, which exceeds the 5 ms handler limit.
The receipt-read action then rejects that failed journal.

| Failed receipt read | Duration |
| --- | ---: |
| Submission to action entry | 0.024660 ms |
| Controller action bracket | 0.316728 ms |
| Action exit to caller observation | 115.427269 ms |

The action exits about 33.286 ms after request start. The caller observes failure about 148.713 ms after request start.
The return interval includes worker fault publication, exception handling, scheduling, and observer work.
It does not identify which component consumed that time.
The separate longest queue interval is about 49.895 ms for a header read on peer request 55.

The managed receipt-interval parser is not reached for request 154 because journal acceptance throws first.
These data do not explain the earlier rejected receipt endpoint or authorize a longer receipt bound.
They establish that a late caller observation can follow a short controller action, separately from a native wait failure.

The capture is `artifacts/controller-dispatch-single/20261015T221111890/01-owner`.
Zero speedup and expected invalidation pass. Replay verifies all three records and preserves the treatment failure.
The observer adds cost, and the capture does not establish prediction accuracy or a scheduling cause.
See [the evidence and limits](../../reliability/features/native-signal/controller-dispatch.md).

See [collector operations](../Reference/pausable-sample-collector.md),
[comparison commands](../How_Tos/run-bound-selected-cadence.md),
and [native owner scheduling](../Research/native-owner-scheduling.md).

[processing]: https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L736-L758
[iterator]: https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/perf.h#L182-L196
[delay]: https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L678-L733
