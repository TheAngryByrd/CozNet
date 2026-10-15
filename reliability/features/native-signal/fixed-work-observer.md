# Fixed-work observer reference

## Question and scope

This diagnostic compares the bound sampled path at zero speedup with an uninstrumented reference.
It tests whether removal of full-history copies is sufficient to make selected-work overhead small.
It does not establish exact component costs or physical prediction accuracy.

Source revision: `2e9e6df631b6edd39b7b1a001a9f822cf6dd2abc`.
Platform: Linux x64, .NET 10.0.0, SDK container image retained in each manifest.
The existing unrelated `src/Directory.Build.targets` worktree change is outside the experiment's source changes.
Tiered compilation, ReadyToRun, and runtime perf-map output are disabled for the elapsed comparison.

## Elapsed comparison

Run: `artifacts/fixed-work-observer/20261015T180943553`.
The retained `source/lode/tmp/fixed-work-observer/` directory contains the scratch driver, project, runner, and analyzer.
The manifest retains source copies' hashes, binary hashes, image identity, order, and seed `20261015`.
Eight pairs use four reference-first and four profile-first orders, shuffled before execution.
The resulting order places all four reference-first pairs in blocks 0–3 and all four profile-first pairs in blocks 4–7.
Time drift can therefore align with pair order.
Each case runs in a new container process with an external 120-second watchdog.
Every process exits zero. Source and binary hashes remain unchanged across the capture.

Both paths execute 5,000 selected calls to the same `CpuWorkload.Run` source method.
The profiler rewrites that method with injected probes before JIT compilation in the sampled path.
The generated code differs, so the comparison includes probe calls and changes to code generation.
Both paths require the accumulated checksum to equal `5000 * 49995000`.
The reference calls that method through a compiled delegate and reuses `CooperativeInteropChecks.Worker` through bound delegates.
The reference checks peer liveness before each selected batch and uses the same volatile peer stop condition.
Its outer loop is a scratch implementation, not a shared compiled loop.
It omits the CLR profiler, native registration, callbacks, collector, controller, and ledger work.
The sampled cases use the existing schema-4 command with zero speedup and `qualified-sample` cadence.
Their source replay passes through the captured `AnalyzeBoundSelectedPayment.verify_case` implementation.
All eight sampled cases retain zero credit, payments, and pauses.

| Block | Reference ms | Profile-zero ms | Ratio |
| ---: | ---: | ---: | ---: |
| 0 | 15.561 | 113.727 | 7.309 |
| 1 | 15.599 | 114.374 | 7.332 |
| 2 | 15.547 | 117.719 | 7.572 |
| 3 | 15.482 | 113.481 | 7.330 |
| 4 | 15.321 | 113.647 | 7.418 |
| 5 | 15.461 | 115.443 | 7.467 |
| 6 | 16.332 | 118.487 | 7.255 |
| 7 | 15.486 | 204.525 | 13.207 |

Median paired ratio: `7.37493320636678`.
No row is discarded. The cause of block 7's longer sampled elapsed time was not captured.
The timestamps enclose selected work, excluding warmup, setup, source completion, final debt drain, and shutdown.
Peer totals span different lifetimes in the two paths and are not matched-window progress.
The ratio is descriptive evidence of overhead in this diagnostic, not an exact observer-cost estimate.
The short reference bracket, separate outer loop, delegate call, and uncontrolled scheduling limit attribution.
The raw evidence and scratch sources reside in ignored local artifact directories.
A repository clone includes this report, but does not include those captures or drivers.

## Managed map diagnostic

Run: `artifacts/fixed-work-observer-map/20261015T181304513`.
Four new profile-zero processes enable `DOTNET_PerfMapEnabled=3` and retain each runtime map after process exit.
The runner verifies the same workload and native binary hashes as the elapsed comparison.
All four cases exit zero and pass existing source replay. Input hashes remain unchanged.
The map analyzer retains every sample assignment, matching symbols, and input hashes.
It labels no-match addresses unresolved and overlapping matches ambiguous.

Of 471 source samples, 279 have one map match, 192 remain unresolved, and none are ambiguous.
The selected `CpuWorkload.Run` method accounts for 80 map matches.
Other matched methods include ledger and map lookup, casts, interop, and string formatting.
The map's `[Optimized]` suffix describes JIT compilation, not `CpuWorkload.RunOptimized` or physically reduced work.

These are leaf instruction-pointer observations, not stack traces or exclusive component CPU measurements.
They include final-drain samples, and the post-run map does not establish each code range's lifetime at sample time.
Map emission can perturb execution. These cases are not pooled into the elapsed comparison.
The original CLR-bound sample classification and credit accounting remain unchanged.

## Next decision

The history optimization does not make the zero-speedup diagnostic inexpensive.
Measure unchanged-state debt capture and idle `prepareService` separately before changing their implementation.
Measure the native lifetime and polling path without removing its checks.
Keep the existing 2 ms debt, 50 microsecond child request, 5 ms handler, and 100 ms receipt gates.
The prior treatment failure remains retained. No treatment was rerun for this diagnostic.

See [the human explanation](../../../docsSrc/Explanations/bound-observer-overhead.md),
[counter history](counter-history.md), and [the cadence comparison](bound-selected-cadence.md).
