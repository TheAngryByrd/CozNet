# Coz port and IL rewriting decision

Date: 2026-10-13. This is a source-based design recommendation. The proposed runtime adapter has not been built or validated.

## Decision

Continue CozNet as a new .NET project. Port the Coz experiment contracts. Replace the operating-system integration with a CLR adapter.

Use **selective IL rewriting as one part of a hybrid profiler**. Combine sampling, managed source mapping, cooperative delay probes, and explicit synchronization contracts.
Do not treat injected method timers as a complete implementation of Coz.

The current serial engine remains a separate experimental model. Its proportional control/treatment delays are not the sampled Coz algorithm.
Its [physical validation failure](../../reliability/features/serial/README.md) remains unresolved.

## What the other projects contribute

| Project | Useful reference | Port decision |
| --- | --- | --- |
| Native Coz | Experiment selection, sample credits, synchronization accounting, progress counters, output | Port contracts and selected algorithms with BSD notices. Replace platform code. |
| JCoz | Native runtime agent, asynchronous samples, method/source mapping, per-thread delay accounting | Adapt the architecture. Replace JVMTI and JVM stack capture. |
| Swift Coz | Swift marker API over native Coz macros | Use as API context only. It has no replacement experiment engine. |
| Cozgo | Go marker API over native Coz macros | Use as API context only. It does not establish goroutine accounting. |
| Anthony Lloyd's Causal | Explicit regions and proportional-delay experiments | Retain as a historical reference. Do not copy its unlicensed source. |

See the pinned evidence in [the implementation comparison](managed-coz-implementations.md) and [the initial research](causal-profiling-dotnet.md).
JCoz uses a JVMTI breakpoint for progress. The inspected path does not rewrite bytecode.
This demonstrates that progress instrumentation and causal sampling are separate design choices.

## Native Coz inventory

Native Coz revision: `256e3c389424d6a1691ee5d18b5ecb5a6c1001ec`.

| Component | Preserve | Replace or qualify |
| --- | --- | --- |
| Experiment controller | Sampled target selection, randomized speedup, zero-speedup baselines, adaptive duration | Keep tuning values configurable. Do not assume 1 ms sampling works equally across runtimes. [1] |
| Delay ledger | Selected samples credit local delay. Other runnable threads pay outstanding global delay. | Build a concurrency-safe CLR implementation. Separate desired delay from actual wait and overshoot. [2] |
| Synchronization | Reconcile before waking dependencies. Credit qualifying blocked intervals. | Define contracts for supported managed primitives. Timeout and dependency wake are distinct outcomes. [3] |
| Thread lifecycle | Initialize thread delay state and settle exit state | Use CLR thread lifecycle and OS-thread association. Tasks require a separate model. [3][7] |
| Progress | Named throughput counters and begin/end latency counters | Use stable registered handles. Do not pass changing names through one cached C macro call site. [4] |
| Measurements | Effective duration, selected samples, speedup, raw progress deltas | Retain additional raw timing, lost-sample, probe-gap, GC, and code-version data. [1] |
| Viewer/export | Baseline comparison and compatible records | Verify normalization and latency sign conventions with fixtures before claiming compatibility. [5] |
| Sampling and symbols | Relationship between executing code and selected source location | Replace perf/Mach and DWARF assumptions with platform sampling, CLR code maps, and portable PDBs. [6][7] |

Linux and macOS delay accounting differ. Linux credits actual waits to local delay.
The macOS path caps that credit and records overshoot separately. Do not combine those rules without a timing model. [2]

Cooperative probes must preserve the ordering of synchronization events.
For example, a worker must settle required delay before it signals completion to a dependent worker. [3]

## What IL rewriting solves

IL rewriting can insert calls at selected progress points, method boundaries, loop backedges, and supported synchronization sites.
These calls can register progress or let the executing thread settle delay debt.
This is a proposed use of the CLR body-replacement APIs, not evidence of causal accuracy. [8][9]

The CLR supports initial body replacement before JIT compilation.
ReJIT supplies replacement bodies for later invocations of compiled methods. It does not replace active frames. [8][9]
`RequestReJITWithInliners` also requests recompilation of callers that contain inlined copies. Ordinary `RequestReJIT` does not track those callers. [7]

| Approach | Benefit | Main limit | Decision |
| --- | --- | --- | --- |
| Offline rewriting | Simple inspection of transformed assemblies and PDBs | Requires modified artifacts. Dynamic code and late-loaded assemblies need separate coverage. | Useful for a controlled prototype. |
| Startup CLR profiler | Instruments selected methods before execution | Requires native profiler integration and correct IL transformation. | Preferred initial automatic instrumentation path. |
| ReJIT | Changes selected compiled methods at runtime | Active frames remain. Inliners, compilation completion, and warmup affect measurement. | Add after startup instrumentation works. |
| Sampling only | Observes CPU execution without dense probes | Does not itself provide safe, timely intervention points. | Necessary observation component, insufficient intervention component. |
| Method entry/exit hooks | Exposes function boundaries without arbitrary IL edits | Does not cover a long-running loop between boundaries. | Optional method-level mode, not line-level coverage. |

Install instrumentation before measured experiments. Change experiment state through the probe fast path.
Do not request ReJIT for each speedup value. Its runtime suspension and possible GC would perturb the measurement. [9]

## What IL rewriting does not solve

1. **CPU attribution.** A method can wait or execute on behalf of many callers. Elapsed method time is not sampled CPU time.
2. **Intervention coverage.** Method-entry probes miss long loops. Loop probes still miss unmanaged execution and uninstrumented code.
3. **Synchronization semantics.** A call boundary does not identify whether a wait ended through a dependency, timeout, cancellation, or spurious wake.
4. **Async scheduling.** An `await` can release a worker. A later continuation can use another worker. Physical thread debt is not request lifetime.
5. **JIT effects.** Probes can change inlining and optimization. Zero-speedup overhead requires separate measurement.
6. **Source identity.** Rewritten IL offsets differ from original offsets. JIT code versions also differ.
7. **Runtime safety.** Arbitrary suspension can stop a thread while it owns a runtime lock. A stack API is not a safe general delay API. [10]

These are design constraints. The proposed adapter must demonstrate each supported case through runtime evidence.
NativeAOT requires a separate ahead-of-time instrumentation design. ReJIT is not its implementation path.

## CLR implementation constraints

Keep native address, code version, method identity, original IL offset, and source location distinct.
The CLR exposes native-code start addresses and mappings for specific JIT code versions. [7]
Retain an explicit original-to-rewritten IL map in CozNet. Verify source attribution after tier changes, ReJIT, and module unload.

The current interface file describes `ICorProfilerFunctionControl::SetILInstrumentedCodeMap` as unimplemented.
However, the runtime implementation contains map-storage logic. Do not infer runtime behavior from that comment alone. [7][11]
Test the exact .NET 10 runtime before relying on rewritten source maps.

`DoStackSnapshot` has platform and suspension constraints.
The runtime can return `E_NOTIMPL` for another thread when safe thread suspension is unavailable. [10]
Therefore, choose and validate a sampling backend per platform. Do not promise portable sampling from this API alone.

## Recommended implementation sequence

1. Specify and test the sampled delay ledger independently of the serial engine.
2. Build a startup profiler spike for one platform and the installed .NET 10 runtime.
3. Rewrite selected methods and loop backedges. Keep original IL maps and stable probe identifiers.
4. Measure disabled-probe cost, zero-speedup cost, maximum probe gaps, and source-map accuracy.
5. Combine independent CPU samples with cooperative delay checks. Exclude profiler waits from selected samples.
6. Add supported synchronization wrappers with explicit wake outcomes and deterministic ledger tests.
7. Compare predictions with real optimizations in serial and parallel workloads. Preserve null and negative results.
8. Add ReJIT, async scheduling, more platforms, and viewer export only after their individual contracts pass.

A long loop with no additional method calls is a required rejection test for entry-only instrumentation.
A producer/consumer workload is a required test for duplicated delay after a dependency wake.
A continuation that changes worker threads is a required test before any async support claim.

The hybrid design remains provisional until probe coverage and measurement validity pass.
If probes cannot deliver acceptable coverage without excessive distortion, investigate runtime-supported intervention instead of claiming general support.

## Sources

[1] [Native experiment controller](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L255-L489).

[2] [Native delay and sample processing](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L678-L849).

[3] [Thread creation and blocking contracts](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.h#L118-L222).

[4] [Native marker macros and synchronization hooks](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/include/coz.h#L139-L180).

[5] [Viewer aggregation and normalization](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/viewer/ts/profile.ts#L1052-L1168).

[6] [Native scope and platform requirements](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/README.md) and [license](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/LICENSE.md).

[7] [Current CLR profiling interfaces](https://github.com/dotnet/runtime/blob/d8ad3875aa545bb70067fbdeafdf1f9892a3d818/src/coreclr/inc/corprof.idl), especially thread callbacks, `ICorProfilerInfo9`, and `ICorProfilerInfo10`. This is a current source snapshot, not the tested .NET 10 runtime.

[8] [SetILFunctionBody](https://learn.microsoft.com/dotnet/framework/unmanaged-api/profiling/icorprofilerinfo-setilfunctionbody-method).

[9] [RequestReJIT](https://learn.microsoft.com/dotnet/framework/unmanaged-api/profiling/icorprofilerinfo4-requestrejit-method).

[10] [Current stack snapshot implementation](https://github.com/dotnet/runtime/blob/d8ad3875aa545bb70067fbdeafdf1f9892a3d818/src/coreclr/vm/proftoeeinterfaceimpl.cpp#L8473-L8660) and [stack snapshot contract](https://learn.microsoft.com/dotnet/framework/unmanaged-api/profiling/icorprofilerinfo2-dostacksnapshot-method).

[11] [ReJIT IL map implementation](https://github.com/dotnet/runtime/blob/d8ad3875aa545bb70067fbdeafdf1f9892a3d818/src/coreclr/vm/rejit.cpp#L321-L380).
