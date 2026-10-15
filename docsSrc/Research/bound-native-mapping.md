# Native ranges for bound cooperative probes

This explanation describes how a native method range can support the CLR-bound probe contract.
The native range contract supports a later restricted Linux perf integration.
It does not establish sampled capacity, source-line attribution, or prediction accuracy.
The runtime sources use revision `60629d14374c56f1cb51819049ad1fa529307f8d`.

## A range query is an observation

`GetCodeInfo2` obtains native extents from a `FunctionID`.
The pinned implementation converts that identifier to a `MethodDesc` and reads `GetNativeCode()`.
It then copies the associated native extents into the caller's buffer. [1]
The returned addresses do not retain a CLR object or pin native code.

`GetCodeInfo4` selects a native body by its start address.
`GetNativeCodeStartAddresses` enumerates native versions for a function and `ReJITID`.
These APIs matter when tiering produces several native versions. [2]
Their extra identity does not provide an unload lease.
Replacing `GetCodeInfo2` with `GetCodeInfo4` alone cannot solve lifetime races.

For one non-generic method, `GetCodeInfo2` is sufficient to observe the restricted body's native extent.
This conclusion requires disabled tiering, disabled precompiled execution, disabled inlining, no ReJIT, and permanent rejection of duplicate selected compilation.
It does not prove that these restrictions cover arbitrary application methods.
The [CLR lifetime explanation](cooperative-clr-lifetime.md) describes these restrictions and their limits.

## Publication follows successful compilation and range capture

`JITCompilationFinished` supplies the compilation status and a valid `FunctionID` for profiler information APIs. [3]
A successful callback can capture the range before publishing the binding as ready.
An early injected probe must reject admission until publication completes.
The CLR explicitly permits another thread to enter the method before the finished callback arrives. [3]

Duplicate compilation must invalidate the binding even when the runtime reports the same `FunctionID`.
The CLR documents repeated compilation attempts during class initialization. [3]
The first callback cannot restore readiness after a second callback invalidates the binding.
Publication therefore needs a state transition that loses to permanent invalidation.

The initial contract can accept exactly one nonempty extent.
It must reject zero addresses, zero sizes, address overflow, and any returned count other than one.
The runtime can return separate hot and cold extents. [4]
A one-element buffer does not prove that the method has one extent.
The pinned implementation can fill that buffer, report two extents, and return `S_OK`. [4]
Multiple extents require explicit support rather than silently accepting the first extent.

`fIsSafeToBlock` does not authorize waiting for application progress.
The runtime can hold JIT or `MethodDesc` locks while it makes the callback. [5]
`GetCodeInfo2` uses an asynchronous profiler entrypoint, but its range lookup can report `CORPROF_E_ASYNCHRONOUS_UNSAFE`. [1]
Every failed query must prevent publication.
Rejecting `fIsSafeToBlock == FALSE` is a conservative scope restriction, not a documented prerequisite of `GetCodeInfo2`.
The callback must not join workers, invoke managed cleanup, or wait for an early probe.

## Immutable metadata and permanent invalidation

The mapping stores copied scalar values:

- Instrumentation generation and probe site count.
- Module-load identity, `FunctionID`, and method token.
- Native start address and native size.
- Permanent invalidation state.

The mapping belongs to the same binding that supplies injected probe literals.
A managed wrapper must compare the complete binding identity before accepting its range.
Range publication must precede readiness publication.
Readers must observe readiness before and after copying or resolving the range.
An immutable record or atomic fields must prevent data races during competing publication and invalidation.

`ModuleUnloadStarted` invalidates the selected binding permanently.
The interface states that the `ModuleID` becomes invalid after the callback returns. [6]
The pinned runtime does not issue `FunctionUnloadStarted`. [5]
That callback alone cannot protect the mapping.
No later load can reactivate a record, even if the runtime reuses an identifier or address.

Unload notification does not pin code between two checks.
A check can succeed immediately before invalidation.
The mapping policy must therefore reject an affected experiment at the next acceptance boundary.
It must retain native ownership until cancellation and cleanup complete.
The existing [bound cooperative contract](../Reference/bound-cooperative.md) separates measurement acceptance from cleanup ownership.

## A perf record needs more than an address match

The future sample path must accept records only within an admitted collection epoch.
The epoch begins after range publication and a successful binding check.
It excludes records retained from an earlier collection interval.
The controller checks the same binding when it accepts samples, admits payment, accepts receipts, and completes the experiment.
Any observed invalidation rejects the experiment.
This policy avoids assigning a buffered sample to a later body at the same address.

A range match uses the half-open interval `[start, start + size)`.
The resolver must treat an unmatched address as unresolved.
It must not classify an unmatched address as selected work or infer a different method from the current binding.
For the first implementation, whole-method resolution means only that the sampled IP lies inside the observed method extent.

The mapping does not identify an injected probe site.
It does not exclude countdown instructions or other injected IL from selected-method CPU.
It also does not identify source lines, caller frames, native callees, or suspended work.
The runtime's extent calculation can include associated data, such as switch tables. [1]
A synthetic address inside the extent is therefore weaker evidence than a real sampled instruction pointer.

Sample ownership, timestamps, loss, and frozen endpoint membership remain separate requirements.
Successful address resolution cannot replace those checks.
Cross-platform code can retain the immutable binding and range contract.
Linux perf remains a platform-specific sample source.

## What other Coz implementations establish

Native Coz resolves sampled instruction pointers and callchain addresses through its memory map.
Its callchain path subtracts one from return addresses to locate call sites. [7]
That convention must not be applied to a direct sampled IP.
The native map does not establish a CLR lifetime contract.

JCoz obtains JVM frames through `AsyncGetCallTrace`.
It matches the selected method identifier and bytecode-location ranges. [8]
This supports separating runtime identity from experiment accounting.
It does not demonstrate that a CLR native range remains valid after unload.

## Evidence needed before sampled admission

The range contract requires checks for failed queries, extra extents, overflow, duplicate publication, and publication competing with invalidation.
Runtime checks must obtain the extent from the actual rewritten target on each claimed platform.
They must reject mapping after a real duplicate-module event and after selected-module unload.
Synthetic boundary addresses can verify interval arithmetic, but cannot establish sample provenance.

A later Linux perf check must collect real target IPs after admission and resolve them against the same binding.
That check must preserve loss, ownership, timing, and cleanup evidence.
A passing check would establish the restricted mapping path.
It would still leave intervention capacity, overhead, and physical prediction as separate gates.

## Primary sources

1. [CoreCLR range lookup and `GetCodeInfo2`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/proftoeeinterfaceimpl.cpp#L2158-L2484).
2. [Native version enumeration and `GetCodeInfo4`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/proftoeeinterfaceimpl.cpp#L6379-L6603).
3. [JIT callback ordering, duplicate attempts, and completion status](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/inc/corprof.idl#L1170-L1222).
4. [Hot and cold extent copying and returned count](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/proftoeeinterfaceimpl.cpp#L2281-L2341).
5. [Function unload and JIT callback contracts](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/eetoprofinterfaceimpl.cpp#L3097-L3181).
6. [Module unload contract](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/inc/corprof.idl#L1076-L1093).
7. [Coz instruction-pointer and callchain matching](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp), function `profiler::match_line`.
8. [JCoz frame capture](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L573-L609) and [selected-location matching](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L468-L480).
