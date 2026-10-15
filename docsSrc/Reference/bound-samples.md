# CLR-bound CPU sample collection

`BoundSampleCollection` connects a bound native method range to a fresh CPU event source.
It remains an internal capability path. The public serial API does not use it.

## Source admission

`CpuSampleOwner.Parse` requires nonzero process and thread identifiers.
The collection captures its `ClrBoundInstructionMap` before it creates the source.
`Start` accepts a factory with the map and sample sink as arguments.
The factory must return a new disabled source. It owns cleanup if it throws before returning that source.

The source must provide `ICpuCounterEvidenceSource` evidence in task-clock nanoseconds.
`BoundCpuEpoch.ParseFresh` requires epoch zero, a zero counter, no transitions, and no recorded failures.
The initial ring drain must contain no samples, loss, throttling, or other accumulated counts.
The collection checks CLR lifetime again before it enables the source.
It can start only once.

The source epoch counts enable transitions. It is not a thread eligibility epoch.
This path does not infer runnable, blocked, or delaying states and does not generate causal credits.

## Provisional records

One collector drains the source.
Each sample must match the admitted process and thread.
Its timestamp must be nonzero and cannot regress.
Its cumulative CPU counter must be present and cannot regress.
Its method label must agree with the bound native range.
The collection retains at most 10,000 samples. A further sample permanently fails the collection.

Sample acceptance checks CLR lifetime before and after validation.
An observed invalidation permanently fails the collection.
Samples remain provisional until completion succeeds.
The method range includes injected countdown code. These records do not establish exact application CPU attribution.

## Completion and cleanup

`CloseForCleanup` attempts disable, collector join, source disposal, and final counter capture.
Cleanup does not require a valid CLR binding.
It retains errors by stage. Repeated cleanup returns the same report without releasing the source again.
`Diagnostics` becomes available after cleanup and retains provisional samples from rejected collections.

`Complete` requires clean closure and exactly one successful enable/disable pair on the original counter source.
The final counter reading must follow disable completion.
Lost records, lost samples, throttling, changed counts, and inconsistent period totals reject completion.
The final CPU counter must cover every retained sample counter.
Distinct addresses and method labels must agree with the retained records.
Completion checks CLR lifetime before and after it constructs the result.

Successful completion returns an immutable sample snapshot and the source journal.
An invalidation after source closure still rejects completion.
Failed completion does not erase diagnostic records.
A successful lifetime check observes a boundary. It does not pin code after that boundary.

## Platform support and limits

The collection and source factory use portable managed code.
`LinuxCpuEventSession` accepts an explicit instruction map and creates a disabled Linux x64 task-clock event.
Its existing default map remains available to the earlier workloads.
The bound verification disables runtime perf-map output.
Windows currently runs the portable checks. A Windows CPU-event backend remains unimplemented.

The fresh source and its journal establish this collection's admission epoch.
They do not establish measurement endpoint membership, clock error bounds, thread eligibility, sampled payment capacity, or prediction accuracy.
Per-sample guard overhead remains unmeasured.

See [the verification procedure](../How_Tos/verify-bound-samples.md), [native mapping](probe-identity.md), and [bound payment](bound-cooperative.md).
