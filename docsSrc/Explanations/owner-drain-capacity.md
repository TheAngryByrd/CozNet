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

## Native Coz applies a batch before its delay check

In [Linux Coz's processing loop][processing], the target applies selected credits from the captured ring batch before calling `add_delays`.
The [iterator][iterator] captures one ring head. It does not chase a moving producer.
The [delay handler][delay] then publishes excess local delay or pays outstanding debt.

CozNet currently checks debt and services peer receipts after each published sample.
Receipt acceptance can therefore occur between historical samples from one captured batch.
Later selected credits can reduce selected debt before a full batch has been published.
This source difference does not prove incorrect causality or justify removing the 2 ms gate.
Any batch-order experiment must retain intermediate debt evidence and exact receipt identity.

Neither inspected processing path guarantees complete observer-CPU exclusion.
Native Coz stops its sampler around waiting, not around record processing.
CozNet owner drains also run while the source is enabled.
The credit rule is unchanged, but owner drain CPU can enter sample periods.

See [collector operations](../Reference/pausable-sample-collector.md),
[comparison commands](../How_Tos/run-bound-selected-cadence.md),
and [native owner scheduling](../Research/native-owner-scheduling.md).

[processing]: https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L736-L758
[iterator]: https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/perf.h#L182-L196
[delay]: https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L678-L733
