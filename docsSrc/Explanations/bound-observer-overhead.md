# Observer overhead in the bound workload

The bound sampled workload performs profiler work on the selected thread.
Injected probes check CLR lifetime, poll native requests, publish samples, and inspect debt.
A zero-speedup run still performs this work, even when it pays no delay.

## Receipt callbacks during pending service

Pending peer service checks receipt readiness on every executed check.
The compiled path previously constructed two callback delegates before each readiness call, including calls that found no receipt.
The workload now creates these callbacks during setup and reuses them.

Each call still reads current native state. Reuse does not cache readiness or skip a service check.
The direct path retains its call counter. Controller paths retain dispatch timing and their separate peer and selected visibility rules.
This removes construction of the readiness arguments. Controller dispatch can still allocate its inner callback.
It does not establish the cause of the prediction gap or a full-workload speedup.

An extracted direct-readiness segment allocates 128 bytes per check before reuse and zero afterward in two before/after pairs.
Live direct-mode checks pass. The controller-fallback treatment fails selected debt after a recorded peer-wait overshoot.
The failure remains retained. Callback reuse does not establish stable capacity.
See [the allocation and live evidence](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/receipt-callback-reuse.md).

## Allocation before selected payment

The selected-probe callback previously contained the captured payment callback after its early-return checks.
The Release compiler allocated that closure at method entry, including probes that required no payment.
The allocation therefore occurred on entry-site probes as well as loop-site probes.

The payment body now has a separate local helper.
Only an admitted payment reaches that helper and creates its captured callback.
Native polling, sample draining, service, cadence, and debt checks retain their order before payment.
The payment body retains its source exclusion, receipt validation, and resume sequence.

An isolated Linux check calls the real entry-site callback 500,000 times per process.
Both baseline processes allocate 48 bytes per call. Both changed processes allocate zero bytes on the calling thread.
The live zero, half-speedup, and expected-invalidation cases also pass with unchanged inputs.
This change removes an allocation source from probes that require no payment.
It does not establish whole-workload savings or resolve the prediction gap.
A later 500,000-batch comparison completes all four arms but remains unqualified.
Its sampled reduction is about 39.25%, against 52.93% physical reduction.
The separate runs do not isolate the refactor's effect on elapsed time.
See [the allocation and integration evidence](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/probe-closure.md).

## Sample history at pause boundaries

Each selected pause previously copied and rescanned the full sample history to check source accounting.
An isolated Linux measurement found repeated validation costs of 4–5 µs for 50 samples and 92–94 µs for 1,500 samples.
These component measurements do not establish their share of live profiler cost.

The capture now maintains counts, represented CPU, maximum cumulative CPU, distinct instruction pointers, and symbols during sample admission.
Only pausable captures maintain these totals.
Pause validation checks these totals against the source batch.
Final completion still performs the independent full-history check.
The callback still receives its frozen epoch snapshot.
This removes a repeated scan without changing sample credit, source ownership, or payment bounds.
The isolated aggregate check takes about 0.11 µs at 1,500 samples, but this excludes admission cost.
Controlled source checks and the three-case Linux integration pass.
The longer comparison completes all arms, but its prediction still falls short by about 15.9 percentage points.
Total-workload savings and stable capacity remain unverified.
See [the retained validation evidence](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/incremental-pause-validation.md).

## Repeated debt records

Each executed service check records debt.
The reference record allocated 88 bytes per observation in the isolated host check.
The readonly value record now allocates zero calling-thread bytes for the same construction and nullable retention path.
It preserves timestamps and earlier observations as value copies.

This removes one allocation source from repeated service checks.
It does not measure the allocation's share of total observer cost or establish a full-workload speedup.
The [retained evidence](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/debt-value-observations.md) separates the allocation check from live integration.

## Repeated idle service

The workload skips service when the same immutable accounting state already completed service with no pending peer request.
The selected owner controls accounting updates, so unchanged state cannot acquire new ledger debt by elapsed time alone.
A pending request always requires service because its timeout and native receipt can change independently.
Each new state also requires service, even when its debt values equal the previous values.

Skipped service does not refresh the last debt observation timestamp.
Failure records retain the latest actual observation, which can precede several skipped checks.
The final debt observation remains explicit.
Source draining, native probe polling, and sample publication keep their existing checks.

This change removes repeated idle work. Its full-workload timing benefit remains unverified.
The controlled Linux run passed zero speedup, 50-percent speedup, and expected invalidation after selected payment.
Zero speedup skipped 500,000 of 500,350 service checks. The treatment skipped 423,869 of 500,392 checks.
These counts establish reuse in the workload, not prediction accuracy.
See [the service cadence reference](../Reference/bound-sampled-payment.md#idle-service-cadence).

## Physical comparison after debt value records

A four-arm comparison before idle service reuse completed all workloads without invalid records or changed inputs.
Its descriptive sampled elapsed reduction was −6.6192% to −6.5628%.
The physical elapsed reduction was 48.4659% to 48.4796%.
The sampled treatment was therefore slower in this block, despite the physical reduction.

The comparison remains unqualified because sample membership, attribution, and ledger residuals remain unresolved.
It contains one declared block and does not isolate the effect of the value-record change.
Its ranges are descriptive clock-boundary ranges, not accuracy bounds or confidence intervals.
See [the retained captures](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/idle-service-cadence.md).

## Owner-drain component cost

Owner draining remains on the selected probe path before idle service reuse.
It checks source ownership and CLR lifetime, drains the perf ring, and retains the current batch snapshot.

A separate Linux diagnostic compares an empty callback, a disabled empty ring, and active owner draining.
Six shuffled blocks run 500,000 calls per case in fresh processes.
Median costs are 1.312, 10.809, and 78.245 ns per call, respectively.
The median paired owner-drain cost above the empty callback is 38.414 ms across those calls.

Active cases publish only 38–42 samples during the measured loops.
Most calls therefore find no new sample, but the active measurement also includes sample handling and lifetime checks.
The diagnostic omits checksum work, peer contention, and debt payment.
Its differences cannot isolate individual checks or establish savings in the full workload.

Fewer probe callbacks are a candidate for the next experiment.
A larger interval must still reach peer service and detect invalid CLR lifetime within the existing contracts.
See [the retained diagnostic and failed harness capture](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/owner-drain-cost.md).

## Fixed-work reference

The fixed-work reference compares the same checksum arithmetic and 5,000 selected batches.
Both paths use the existing dedicated peer worker.
The reference disables the native profiler and omits sampling, probe callbacks, and accounting.
It calls the existing arithmetic through a compiled delegate.
The sampled path calls that arithmetic directly and uses sample-qualified selected checks.
Its profiler rewrites the source method before JIT compilation, so the generated code differs from the reference.
Their outer loops are separate implementations.

## What the comparison shows

Eight balanced, shuffled pairs ran on Linux x64 with .NET 10.0.0.
Each case used a new process. Tiered compilation and ReadyToRun were disabled.
The shuffled order placed all reference-first pairs before all profile-first pairs.
Time drift can therefore align with pair order.
All 16 cases completed with valid checksums and joined workers.
Each sampled case requested zero speedup and retained no payments or pauses.

| Selected work | Observed elapsed time |
| --- | --- |
| Physical reference | 15.3–16.3 ms |
| Sampled workload, seven cases | 113.5–118.5 ms |
| Sampled workload, remaining case | 204.5 ms |

The median within-pair elapsed ratio was 7.37.
The 204.5 ms result remains in the comparison.
The result shows substantial overhead in this small controlled workload.
It does not measure startup cost, total process CPU, or one component's cost.

Peer progress is not fixed, and each path stops its peer after different completion work.
The recorded peer totals are not progress within the selected measurement window.
The separate outer loops and reference delegate call also limit exact cost attribution.
These measurements do not establish an application-wide overhead ratio.

## What the samples can explain

A separate four-case capture enabled the runtime perf map.
Post-run lookup assigned unique map entries to 279 of 471 source samples.
It assigned 80 samples to `CpuWorkload.Run` and left 192 unresolved.
Other labels include ledger lookup, type checks, interop, and diagnostic formatting.

These are instruction-pointer labels, not stack traces or component timings.
The sample set includes final source-drain records outside the selected work bracket.
The post-run map does not prove code lifetime at each sample timestamp.
Its labels do not replace the admitted CLR map or alter sample credits.
Enabling map output also changes the observed process.

These labels identify candidates for separate component measurements.
They do not justify removing correctness checks.

## Effect of immutable-state reuse

The controlled workload now reuses validated accounting results for the same immutable state object.
An isolated repeated-state call allocates zero bytes, compared with 1,000 bytes for uncached idle preparation and debt capture.
State changes still require full accounting operations.

A subsequent eight-pair capture retained a median profile/reference elapsed ratio of 2.95.
All 16 cases completed. The earlier capture retained a ratio of 7.37.
Reference timings also changed, and both captures contain slower cases that remain in the results.
The separate captures do not isolate the change's exact elapsed-time effect.

The subsequent selected-payment matrix passed all twelve cases with unchanged timing bounds.
Continuous mode still performed thousands of pauses and admitted few selected intervals.
One passing matrix does not establish stable capacity or physical prediction accuracy.
See [the reuse evidence](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/accounting-reuse.md)
and [the cache contract](../Reference/cooperative-managed.md#immutable-accounting-reuse).

See [the cadence contract](../Reference/bound-selected-cadence.md),
[the cadence procedure](../How_Tos/run-bound-selected-cadence.md), and
[the retained evidence](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/fixed-work-observer.md).

## Cost of bound probes

A separate Linux x64 diagnostic compares four stages with the same compiled delegate and checksum arithmetic.
Each stage completes 50,000 measured batches after warmup.
Eight shuffled blocks use separate processes, with tiered compilation and ReadyToRun disabled.
All 32 cases passed work, probe identity, cleanup, and unchanged-input checks.

| Stage | Median elapsed time | Observed range |
| --- | --- | --- |
| Profiler disabled | 160.6 ms | 154.4–191.6 ms |
| Bound probes without a callback | 180.6 ms | 178.7–217.3 ms |
| Bound probes with CLR lifetime checks | 202.6 ms | 196.1–238.5 ms |
| Bound probes with idle native polling | 221.8 ms | 215.0–329.2 ms |

Each measured stage allocated zero bytes on the calling thread.
Injected stages executed 500,000 probes per case.
Within-block median differences were 20.7 ms, 20.1 ms, and 18.9 ms between adjacent stages.
These differences include process and scheduling variation. Some individual differences were negative.
All results, including the 329.2 ms idle-poll case, remain in the comparison.

The diagnostic excludes sampling, debt accounting, and active payment.
Its callbacks also perform diagnostic checks that differ between stages.
The result supports further study of lifetime checks, but does not establish their exact cost in the sampled workload.
It does not justify weaker lifetime or thread-ownership checks.
See [the probe evidence](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/bound-probe-cost.md).

## Why the cheaper lifetime query remains deferred

The current guard compares generation, module, function, method, and site count on every native check.
`CozIsProbeBindingCurrent` checks only generation, site bounds, and the native Ready state.
It cannot replace full identity comparison for an arbitrary parsed binding.

A scratch candidate first checks the full identity.
It then binds one immutable object to the cheaper query for later checks.
This design depends on the current native record: identity fields never change after Ready, and invalidation is permanent.
A reusable record or a new generation scheme would require a new admission argument.
The candidate retains the existing checks before and after each operation and permanent first-failure behavior.
Neither design pins code or prevents invalidation immediately after a successful query.

Eight shuffled pairs compared both guards with 500,000 probes per case.
All 16 cases passed their work checks and rejected duplicate-module invalidation after measurement.
The median paired reduction was 15.6 ms, or about 31 ns per probe in this diagnostic.
Both paths allocated zero bytes on the calling thread.
One pair favored the current guard. Large timing differences remain in the records.

This result does not measure the candidate inside the full sampled workload.
It also does not exercise invalidation during a native wait or prove all concurrent schedules.
The runtime retains full identity checks because the measured benefit does not yet justify a new admission contract.
The next investigation should address sample coverage and physical prediction, which remain unmet requirements.

## Why GC transition suppression remains deferred

The binding guard uses an ordinary P/Invoke transition for each full identity query.
A scratch comparison applied `SuppressGCTransition` to a separate import of the same export.
Both paths compared every binding field and retained the existing lifetime guard.

Eight shuffled Linux x64 pairs each completed 50,000 batches and 500,000 probes.
All 16 cases rejected duplicate-module invalidation after measurement.
The median paired elapsed reduction was 4.21 ms, or 2.14%.
Two pairs favored the ordinary import. Both paths allocated zero bytes on the calling thread.
These measurements include checksum work and guard execution. They do not isolate native-call duration or establish full-workload savings.

The [.NET contract](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/SuppressGCTransitionAttribute.cs) requires trivial native work below one microsecond.
It also excludes blocking, callbacks, exceptions, and manipulation of locks or other concurrency primitives.
The binding query uses fixed lock-free atomic reads. The contract gives no explicit exception for those reads.
Suppression also changes native-library loading and debugging behavior.

The ordinary import remains in use.
Release disassembly, concurrent GC progress, missing-library behavior, unload behavior, and Windows execution remain unverified for the suppressed import.
The timing result does not resolve these constraints or qualify causal prediction.
See [the retained comparison](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/binding-transition-cost.md).

## Coverage and pause cost

A longer sample-qualified capture completes 50,000 selected batches in each normal case.
All three cases, including expected invalidation, passed with unchanged inputs and timing bounds.
The treatment credited 125 mapped intervals, compared with 9 and 15 in two earlier 5,000-batch treatments.
It retained 421 source observations across 125 enabled epochs.
Every enabled epoch contained at least one processed observation.

The treatment also retained 62 mapped observations as epoch baselines.
Those observations earned no credit because their nominal periods can span a disabled interval.
Removing that rule would change the eligibility contract.
An enabled-event counter cannot assign the uncertain interval to application instructions.
The source counter and nominal sample totals also cover more than the selected-work measurement window.
These counts therefore do not establish application CPU coverage.

The 124 pause intervals totaled 60.507 ms within the 522.706 ms selected-work interval.
Recorded selected payment totaled 0.166 ms.
The first pause took 26.117 ms and was the longest. It remains in the totals.
Pause duration includes producer stop, final drain, callback work, controller dispatch, and resume.
The current timestamps do not separate all these phases or distinguish execution from descheduling.

The longer run establishes more credited observations under the existing bounds.
It does not establish stable capacity or prediction accuracy.
Phase timing at the first payment is the next diagnostic needed before changing that path.
See [the coverage evidence](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/bound-coverage.md).

## First pause phase capture

Optional phase timing retains completed boundaries and leaves unreached boundaries null.
The first timed 50,000-batch capture passed zero speedup and expected invalidation.
Its treatment failed at 2,309 batches because peer debt reached 2.500 ms, above the unchanged 2 ms limit.
The analyzer accepted the evidence structure and preserved the runtime failure.
The failed capture does not establish that timing caused the debt failure.

The first treatment pause took 42.733 ms.
The interval from drain acknowledgment through batch validation took 35.567 ms.
This interval includes admission closure, a lifetime check, snapshot construction, and validation.
The final-drain wait itself took 1.064 ms after disabled counters were accepted.
The phase measurements narrow the investigation, but do not identify execution versus descheduling or prove a JIT cause.

Batch validation uses several LINQ operations over the frozen sample prefix.
Its first-use execution and scheduling need separate investigation before changing validation or adding warmup.
The diagnostic must retain failures and must not treat validation time as payable application work.
See [phase fields](../Reference/bound-selected-cadence.md#optional-pause-timing) and [retained evidence](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/bound-coverage.md#phase-timing).

## Validation preparation

Bound collection startup executes the full batch validator on a local setup list before creating the source.
The list contains mapped, unmapped, and duplicate-IP cases.
It never enters the sample sink, event counter, eligibility epochs, or causal ledger.
Every real paused and final batch still receives the same complete validation.
Preparation adds startup work. It does not bound future JIT, allocation, or scheduling cost.

An eight-process Linux diagnostic called the existing validator on a retained 47-sample prefix.
First validation took 18.4–35.5 ms. Repeated validation averaged 3.15–4.26 microseconds per call.
The snapshot-copy diagnostic was much smaller, but excluded the capture lock.
These isolated measurements include invocation overhead and do not prove an exact JIT duration.

With preparation, the next live first pause spent 0.400 ms from drain acknowledgment through validation.
Its outer pause took 6.664 ms. The previous capture recorded 35.567 ms and 42.733 ms respectively.
Separate captures cannot isolate every source of timing variation.
The prepared treatment later rejected a 6.292 ms child against the unchanged 5 ms handler limit.
Preparation therefore does not establish stable service capacity or prediction accuracy.
See [validation preparation evidence](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/validation-preparation.md).
