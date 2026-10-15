# Cooperative controller dispatch timing

`COZNET_DISPATCH_TIMING=1` enables timing on the selected-payment workload's controller calls.
`CooperativeDispatchTiming.Parse` returns no observer for an absent value or `0`.
The shared observer uses `Stopwatch` and managed synchronization. It has no platform-specific API.
The live sampled source remains Linux-only.

```csharp
var timing = CooperativeDispatchTiming.Parse("1")!;
var result = timing.Run(CooperativeDispatchOperation.PeerRead, command, generation,
    ReadReceipt, action => controller.Run(action));
```

Each record retains operation, command, generation, sequence, and four managed timestamps.
`Submitted` precedes dispatch. `Started` precedes the controller action. `Finished` follows the action, including an action failure.
`Returned` follows caller observation of dispatch completion or failure.
Queue time is `Started - Submitted`. Action time is `Finished - Started`. Return time is `Returned - Finished`.
These brackets include clock, synchronization, and exception-handling cost.
They do not measure pure scheduling latency or CPU time.
The return bracket includes worker result/fault publication, not only caller wakeup.
`Returned` precedes journal and maximum updates. The total bracket excludes this final observer work.

```mermaid
sequenceDiagram
    participant Owner
    participant Controller
    Owner->>Owner: Submitted
    Owner->>Controller: Dispatch
    Controller->>Controller: Started, action, Finished
    Controller-->>Owner: Complete or fail
    Owner->>Owner: Returned and immutable snapshot
```

The observer retains the latest 128 snapshots and independent longest total, queue, action, and return records.
It counts all calls, dispatch failures, and incomplete actions.
A failed dispatch can have no start or finish. A later callback cannot change its immutable snapshot.
`Run` requires its owning thread. Controller callbacks update only their private timestamp bracket under a lock.
The workload records create, request, header, receipt, release, failure-capture, and cleanup operations separately.
Submit records retain generation zero because the native generation is not yet returned.
Global session operations retain zero command and generation.

Timing does not change exception wrapping, payment acceptance, native ownership, or timing limits.
It adds allocations, locks, and clock reads when enabled. Capacity results cannot isolate this observer cost.
The current clock provider must be monotonic and nonthrowing. Runtime capture uses `Stopwatch.GetTimestamp`.
Controlled checks use a deterministic clock and exercise both deferred and active dispatch failures.
Replay checks timestamps, sequence, bounded history, counts, duplicate identity, and extrema against retained observations.
Count reconciliation includes unique maxima records that have left recent history.
Unretained incomplete calls must fit within unretained failures. A completed workload cannot contain failed dispatches.
Evicted observations cannot be independently reconstructed.
Replay does not bind diagnostic request identities to payment or failure-boundary records.
Operation labels and the selected/peer identity mapping have source review, not isolated workload-wiring checks.
Header polling can evict submit records. Global maxima can belong to setup or cleanup.

The unrestricted treatment completes 50,000 batches with 29,456 calls. All three cases pass.
The one-CPU treatment fails at 19,944 batches with a 6,167,752 ns native child.
Its failed peer read has a 0.316728 ms action bracket and 115.427269 ms return bracket.
This separates recorded phases but does not isolate observer cost or a scheduling cause.
See [the evidence](../../reliability/features/native-signal/controller-dispatch.md), [managed receipts](cooperative-managed.md), [sample publication](sample-publication.md), and [the index](../lode-map.md).
