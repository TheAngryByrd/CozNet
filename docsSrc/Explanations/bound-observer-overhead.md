# Observer overhead in the bound workload

The bound sampled workload performs profiler work on the selected thread.
Injected probes check CLR lifetime, poll native requests, publish samples, and inspect debt.
A zero-speedup run still performs this work, even when it pays no delay.

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
