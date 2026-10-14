# Spin during runtime transitions and forced GC

All fourteen runtime-transition outcomes pass with spin waiting.
All twelve sampled cases pass their protocol checks.
Two of the three forced-GC treatments exceed the unchanged 2 ms observed debt limit.
The overall verification fails and retains those results.
Sleep remains the default. General CLR safety and prediction accuracy remain unverified.

## Workload and contract

Command: `dotnet run --project build/build.fsproj -- -t LinuxSpinReliabilityVerify`.
The [archive](data/spin-reliability/run-20261014T030050201-e82dd6ed.zip) contains the transition and sampled suites.
The parent runs both suites and fails if either suite fails.

The transition runner accepts a wait mode for both native contracts and managed workloads.
Its five native contracts and target-lifetime check pass in spin mode.
All eight managed cases pass: JIT activity, native waits, managed waits, and repeated transitions, each with and without forced GC.
JIT cases reach executable compiler code. Transition cases reach selected managed work and native fixture code.
Ineligible cases defer every signal without waiting.
These transition cases do not generate causal credits.

The sampled runner uses three blocks with seed `20261018`.
Each block contains zero and 50 percent, with and without forced GC, in shuffled order.
Each process uses two arithmetic workers, one sampling source, a controller, and an optional GC worker.
Each process has 250 ms warmup and two seconds of measurement.
The seed controls case order, not scheduling. Worker counts, CPU availability, sample period, and GC pattern remain fixed.

The GC worker allocates one MiB, requests a forced blocking compacting generation-two collection, then waits 20 ms.
It starts with the target workers and remains active through warmup, measurement, and settlement.
It records call timestamps and the GC information returned after the call.
These records do not identify exact runtime suspension intervals.
At least one complete collection must fall inside the measurement window.
Calls above 250 ms, invalid observations, worker errors, or failure to join fail the protocol.
Observations remain unreadable until the GC worker joins. An unjoined worker retains its event resources until process exit.

## Sampled results

All five controller checks pass on Linux. The four new GC checks also pass on Windows.
All six zero-percent cases pass with no signal requests or causal credit.
All three treatments without forced GC pass.

| Treatment | Completed measurement collections | Largest GC call (ms) | Peak peer debt (ms) | Capacity |
| --- | ---: | ---: | ---: | --- |
| Block 1, no forced GC | 0 | Not applicable | 1.501952 | Pass |
| Block 1, forced GC | 92 | 4.152118 | 7.450265 | Fail |
| Block 2, no forced GC | 0 | Not applicable | 1.749526 | Pass |
| Block 2, forced GC | 92 | 3.126663 | 16.728312 | Fail |
| Block 3, no forced GC | 0 | Not applicable | 0.599695 | Pass |
| Block 3, forced GC | 93 | 2.508786 | 1.194743 | Pass |

The forced-GC zero-percent cases each contain 97 completed measurement collections.
Largest GC call values include warmup and settlement. Collection counts in the table include only complete measurement calls.
All GC workers join. All native workers join and restore their backends.
Every sampled handler stays within 5 ms, and every acknowledgment stays within 100 ms.
Requested waits remain at most 50 microseconds.

Block 1's peak peer debt retains pending request 12848.
That request later reports a deferred interruption with zero native wait.
Its native timing records 5.878808 ms before the first handler clock reading and a 0.000524 ms handler interval.
This does not identify signal scheduling, runtime suspension, or handler entry as the cause.
It also does not explain the complete debt history of either failed case.

## Verification and next step

The archive retains 39 measured source and binary snapshots with verified hashes.
`AnalyzeTiming.ps1` verifies 27 sampled hashes and reconciles credit and waits across all 24 warmup and measurement phases.
`AnalyzeCounters.ps1` verifies 24 counter windows, including process CPU monotonicity.
`AnalyzeReliability.py` verifies mode coverage, transition reach, collection reach, call limits, teardown, and capacity outcomes.

The focused checks are `ParserRejectsUnknownMode`, `CollectionMustCompleteInsideMeasurement`,
`DurationBoundIncludesExactLimit`, and `CancellationBeforeReleaseJoinsWithoutCollection`.
They test the real parser and worker cancellation. Timing boundary checks use explicit observation records.
Live cases establish collection reach. The boundary checks alone do not establish that GC ran.

Next, correlate runtime suspension events with pending requests and debt growth in a bounded diagnostic run.
Preserve the failed comparison. Do not infer suspension timing from GC call duration alone.
Physical-change prediction remains a separate unmet requirement, with the unchanged two-percentage-point gate.
