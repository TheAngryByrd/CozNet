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

### Live reads

`ReadProvisional(offset)` returns an immutable copy of the records from that zero-based offset through the current end.
An offset equal to the current count returns an empty list.
Reads do not consume records. The caller advances its offset only after it processes the returned list.
An invalid offset throws and permanently fails the collection's lifetime guard.
CLR checks surround each snapshot, including empty snapshots. A failed post-check returns no snapshot.
Reads require a started collector and reject calls after cleanup.
The controller must serialize reads with start, cleanup, and completion. The collector can append concurrently.

These records remain provisional. A later source failure or invalidation rejects the whole collection.
`Complete` supplies the final records, including any tail produced after the last live read.
Native payment cannot be undone when later evidence fails. A consumer must preserve that failure and reject the experiment.

## Completion and cleanup

`CloseForCleanup` attempts disable, collector join, source disposal, and final counter capture.
Cleanup does not require a valid CLR binding.
It retains errors by stage. Repeated cleanup returns the same report without releasing the source again.
`Diagnostics` becomes available after cleanup and retains provisional samples from rejected collections.
Cleanup errors describe cleanup operations. The original operation failure remains separate.

For `Start`, `Complete` requires clean closure and exactly one successful enable/disable pair on the original counter source.
The final counter reading must follow disable completion.
Lost records, lost samples, throttling, changed counts, and inconsistent period totals reject completion.
The final CPU counter must cover every retained sample counter.
Distinct addresses and method labels must agree with the retained records.
Completion checks CLR lifetime before and after it constructs the result.

Successful completion returns an immutable sample snapshot and the source journal.
An invalidation after source closure still rejects completion.
Failed completion does not erase diagnostic records.
A successful lifetime check observes a boundary. It does not pin code after that boundary.

## Pausable collection

`StartPausable` selects the internal mode for excluded owner work.
Its source factory must return `IOwnerStoppedCpuEventSession`.
`DisableAndStopProducer` must disable the source on its owner thread and stop sample publication before it returns.
It returns no counter value. Counter validation follows the stop acknowledgement.
The hook must also stop a source whose enable attempt failed before its logical state changed.
This backend contract does not imply that all loss evidence is available before resume.
The collection requires its constructing thread for start, excluded execution, and cleanup in this mode.

`ExecuteExcluded(action)` disables the source before it requests a collector pause.
The collector finishes its current drain and a final drain before it acknowledges that pause.
The collection then closes sample admission and checks cumulative batch accounting against the disabled CPU counter.
Only a consistent batch and a current CLR binding permit the callback.

The callback receives an immutable snapshot of all admitted `BoundEpochSample` records.
Each record contains its original eligibility epoch and sample.
The callback can process the final old records before it performs excluded work.
Nested excluded execution and cleanup from the callback are rejected.
An exception or observed invalidation prevents resume and permanently fails collection.

After the callback returns, the collection checks CLR lifetime and opens the next eligibility epoch.
It enables the source, parses the source journal, and resumes the collector with the acknowledged pause handle.
`ReadEpochProvisional(offset)` returns immutable records with their admission epochs while collection remains active.
An invalid offset permanently fails the lifetime guard, as with `ReadProvisional`.
`EpochDiagnostics` retains these records after cleanup, including rejected collections.
Diagnostic records can be an incomplete prefix after failure.
For example, enable can succeed before its counter check fails, while the collector remains paused.
The resulting uncollected tail is not admitted after the CLR lifetime guard fails.

The source journal preserves cumulative CPU across all enable/disable pairs.
Completion reconciles the complete journal, cumulative batch, and all samples.
A loss or throttle report at any drain fails the whole collection.
Earlier callback effects remain irreversible. Acceptance at a pause remains provisional until final closure succeeds.
`Latest` is diagnostic evidence, not accepted progress. It can retain the batch that reported loss and caused rejection.
`DisableSucceeded` records successful producer-stop acknowledgement even if a later counter check fails.
Cleanup reports failures found during its counter checks under `counters`, separately from a failed producer stop under `disable`.
Counter failures from excluded entry remain the original operation failure and do not become cleanup errors unless cleanup encounters another failure.

This mode does not calculate credits or perform selected-owner payment.
Consumers still need first-observation baseline exclusion for each new eligibility epoch.
Portable fake-source checks exercise the mode on Windows and Linux.
`LinuxCpuEventSession.DisableAndStopProducer` requires the creating OS thread and uses the dedicated task-clock disable operation.
After a failed enable, it still issues disable when the journal already reports disabled.
That recovery marks unrecorded cleanup and cannot establish a valid experiment journal.
The source restrictions in the perf ownership analysis apply to this hook.
The `pause` verification mode exercises two excluded callbacks and three source epochs with real task-clock samples.
It checks that deliberate owner CPU work inside each callback does not advance the disabled event counter.
This check does not establish exact exclusion of all profiler CPU work.
The live mode also checks that a foreign OS thread cannot stop the source.

Pausable control is synchronous and owner-thread-only. It cannot resume on another thread after `await`.
Callers must keep cleanup on the owner thread and provide a process watchdog for blocked drains.
The verification runner rejects an epoch without selected samples. A scheduling delay does not waive that reach condition.

See [the collector](pausable-sample-collector.md), [source sessions](bound-cpu-session.md), and [perf ownership](../Research/perf-pause-ownership.md).

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


## Pausable batch snapshots

Each pausable drain retains a frozen copy of its batch symbols.
Repeated drains reuse that snapshot when the source returns the same batch object with the same ordered symbols.
Symbol comparison uses ordinal equality. Order and case changes require a new snapshot.
A changed symbol list or a different batch produces a new snapshot.
Source mutations between drains cannot change previously published snapshots.
The source must keep the symbol collection stable while the drain captures it.

Source draining, CLR lifetime checks, loss checks, and pause boundaries still execute.
Snapshot reuse does not change sample admission or payment accounting.
Final completion retains its existing defensive copy.
This optimization does not establish prediction accuracy or a full-workload overhead bound.
The [retained comparison](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/sample-batch-snapshots.md) completes all four arms but remains unqualified.
