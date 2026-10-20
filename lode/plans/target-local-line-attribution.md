# Target-local line attribution

Status: proposed on 2026-10-19. It needs supervisor approval before any implementation. No code exists for it.

## Question

Can CozNet map a sampled native IP inside a captured optimized code version to an IL range, and then to a portable-PDB sequence point, with enough identity to reject stale code?

Verdict: yes, approximately, with a CLR-supplied map. The perf map and `MethodInfo` do not give a native-to-IL map. The source anchor of [candidate discovery](target-local-discovery.md) is the first visible sequence point of a method, and it is not a line of a sample. This design does not extend it.

The research note is [CLR native-to-IL mapping](../../docsSrc/Research/clr-native-to-il-mapping.md). It pins dotnet/runtime `v10.0.0` (`60629d14374c56f1cb51819049ad1fa529307f8d`).

## Cited contracts

- `ICorProfilerInfo9::GetNativeCodeStartAddresses` lists the native bodies of a function, and `GetILToNativeMapping3(start, ...)` gives the map of one body, selected by its start address ([proftoeeinterfaceimpl.cpp L6504-6550](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/proftoeeinterfaceimpl.cpp#L6504-L6550)).
- `GetILToNativeMapping2(functionId, reJitId, ...)` calls `GetILCodeVersion(pMD, reJitId)` and discards the result in v10.0.0, so it cannot select a ReJIT body ([L4857-4925](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/proftoeeinterfaceimpl.cpp#L4857-L4925)). I checked this line in the pinned source.
- Map entries are `COR_DEBUG_IL_TO_NATIVE_MAP`: an IL offset, and native start and end offsets relative to the code start. The end is exclusive. The IL offset can be `NO_MAPPING` (-1), `PROLOG` (-2), or `EPILOG` (-3). The array is sorted by IL offset, not by native offset, and one IL offset can have several native ranges ([functioninfo.cpp L1005-1200](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/debug/ee/functioninfo.cpp#L1005-L1200)).
- For optimized code, the runtime asks the JIT only for stack-empty and call-site boundaries, and it does not use the PDB sequence points ([debugger.cpp L3252-3300](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/debug/ee/debugger.cpp#L3252-L3300)). So a mapped IL range of optimized code is coarse.
- Inlined code maps to the IL offset of its call site in the caller. Rich inline debug information needs `DOTNET_RichDebugInfo=1`, and no profiler API returns it.
- The JIT tracks this debug information by default, in Release and with no debugger. `COR_PRF_ENABLE_JIT_MAPS` is deprecated.
- The event `MethodILToNativeMap` (190, keyword `0x20000`) gives only native start offsets, at most 7000 entries, and an in-process `EventListener` cannot read its arrays. So the event is a weaker source than the profiler call.
- Portable PDB sequence points have strictly ascending IL offsets, and a hidden point has line `0xFEEFEE`. The specification does not define the IL range of a point. The rule "the greatest point with `Offset <= ilOffset`" is an inference, which `tests/CozNet.Instrumentation/Program.cs:75-78` already uses.

## What exists

| Part | File | Use |
| --- | --- | --- |
| Map query of one body | `native/Sampler.h:106-121`, `native/Profiler.cpp:281-297` | `GetCodeInfo2` and the v1 `GetILToNativeMapping` after `JITCompilationFinished`, for one fixed method. A pattern only, not a reusable owner. |
| Map checks | `tests/CozNet.NativeMap/Program.cs:49-90`, `tests/CozNet.LinuxPerf/RawIlCandidateMap.cs:56-83` | Range checks: `start <= end <= size`, no gap or overlap. |
| Original and rewritten IL | `native/Profiler.cpp:199-204` | `SetILInstrumentedCodeMap`. The CLR then returns original IL offsets ([runtime-probes.md](../profiling/runtime-probes.md)). |
| A profiler that loads on Linux | `native/rejit/ReJitProbe.Linux.cpp` | A raw-vtable profiler that the transition suite loads in the Linux container. |
| PDB reader with identity | `src/CozNet/TargetDiscovery.fs` `TargetSourceAnchor` | Embedded or id-matched sibling PDB, with typed errors. |
| Code binding | `src/CozNet/TargetLocalProfiler.fs` `CodeBinding` | Tiering and ReadyToRun off, one `[Optimized]` perf-map range for each method, and `verify`. |

The target-local path has no profiler and no IL map. The legacy profiler maps only one fixed method.

## Recommendation: one narrow milestone

A portable resolver over immutable, supplied native-to-IL and PDB evidence. Live acquisition of the map is a separate, later milestone.

Reasons:
- The supported runtime API, `GetILToNativeMapping3`, is callable only inside a profiler callback or on a profiler thread, and it is not safe in a signal handler. So acquisition needs a profiler component in the target-local process, which does not exist. That is a native change and a live run.
- The resolver rules (identity, intervals, special entries, and sequence points) can be fixed and checked now, with no runtime. The later acquisition then only has to produce evidence that the resolver accepts.
- No IL rewriting is needed. The runtime map exists for unrewritten optimized code.

## Design

New file `src/CozNet/TargetLineAttribution.fs`, after `TargetDiscoveryWorkflow.fs`. It is portable and makes no OS call except to read a PDB file through `TargetSourceAnchor`'s reader.

### Code-version identity

```fsharp
type TargetCodeVersion = private {
    ModuleVersionId: Guid      // the MVID of the module
    MethodToken: int           // a MethodDef token, 0x06xxxxxx
    CodeStart: uint64          // the native start address of this body
    CodeSize: uint64           // the hot size. Cold code is rejected.
    NativeVersionId: uint64    // 0 for the default version
}
```

- The map evidence must carry all five values. A body is the pair of `CodeStart` and `CodeSize` with its method identity.
- The resolver accepts a sample only when its IP is inside `[CodeStart, CodeStart + CodeSize)`, and when that range equals the bound perf-map range of the same symbol in the discovery outcome. Otherwise it gives `StaleCodeVersion`. This rejects a sample against a body that moved or that another body replaced, as far as the ranges show. It does not prove the code lifetime, as with discovery.
- The PDB is matched by the MVID and the PDB id of the module, through the existing reader. A different MVID gives `ModuleMismatch`.

### Map representation

```fsharp
type TargetNativeMapEntry = { IlOffset: TargetIlOffset; NativeStart: uint32; NativeEnd: uint32 }
and TargetIlOffset = Il of uint32 | Prolog | Epilog | NoMapping

type TargetNativeMap = private { Version: TargetCodeVersion; Entries: TargetNativeMapEntry array }  // sorted by NativeStart

module TargetNativeMap =
    val tryCreate: version: TargetCodeVersion -> entries: TargetNativeMapEntry seq -> Result<TargetNativeMap, TargetNativeMapError>
```

`tryCreate` copies and sorts the entries by native start, and it rejects:
- `EmptyMap`, `InvertedRange` (`NativeEnd <= NativeStart`), `OutsideBody` (`NativeEnd > CodeSize`), and `UnknownSpecialOffset`.
- `Overlap`: two native ranges that share an address.
- `Gap`: a native address in `[0, CodeSize)` that no entry holds. The runtime gives a complete tiling of the hot body, so a gap means a wrong or truncated map, for example a capped event.
- One IL offset with several native ranges is accepted, because the runtime gives that form.

### Resolution

```fsharp
type TargetLineResult =
    | Line of document: string * line: int * column: int * ilRange: (uint32 * uint32 option)
    | InProlog | InEpilog | Unmapped
    | HiddenSequencePoint of ilOffset: uint32
    | NoSequencePoint of ilOffset: uint32

module TargetLineAttribution =
    val resolve: map: TargetNativeMap -> points: TargetSequencePoints -> ip: uint64 -> Result<TargetLineResult, TargetLineError>
```

- The resolver finds the one entry with `NativeStart <= ip - CodeStart < NativeEnd`.
- `Prolog`, `Epilog`, and `NoMapping` give `InProlog`, `InEpilog`, and `Unmapped`. They never give a line.
- An IL offset selects the sequence point with the greatest IL offset at or below it. A hidden point gives `HiddenSequencePoint`, and the resolver does not move to a neighbor point. No point at or below the offset gives `NoSequencePoint`.
- `Line` keeps the IL range of the point, from its offset to the next point, or open at the end. So a caller can see that the result is a range, not one instruction.
- `TargetSequencePoints` is a parsed, immutable copy of the points of one method from the matched PDB. The existing reader gets a function that returns all points, not only the first visible point.

### Cases that the resolver rejects

- Inlining: an inlinee shows as the call-site IL offset of the caller. The resolver cannot tell inlined code from the call. Each result of a method that has inlined callees carries the limitation `InlineesAttributedToCallSite`. The evidence must state whether the JIT inlined into the method. When it does not state it, the limitation is always present.
- Shared generics: a `__Canon` body and its token map are not confirmed. The resolver rejects a generic method or a method of a generic type with `SharedGenericUnsupported`, as `TargetSymbol` does.
- Several code versions: with tiering and ReadyToRun off, a method has one default body. A non-zero `NativeVersionId`, a second body for one method in the evidence, or cold code gives `MultipleCodeVersionsUnsupported`.
- Rewritten IL: the target-local path does not rewrite IL. If evidence comes from a rewritten method, it must say so, and the resolver rejects it with `RewrittenIlUnsupported`. The legacy profiler gets original offsets through `SetILInstrumentedCodeMap`, but this milestone does not take that path.

### Ownership and lifetime

- The caller owns the evidence, and the resolver copies it. A map is valid only for its `TargetCodeVersion`.
- Every result carries the limitations `OptimizedBoundariesAreCoarse`, `SequencePointRangeIsInferred`, `MapTimingNotProven`, and `NoCallchain`. Optimized code has only stack-empty and call-site boundaries, so a line can cover several source statements.

### Platform

- The resolver and its checks are portable.
- Live acquisition is a later milestone. It needs a profiler component that answers `ICorProfilerCallback9` or uses a profiler thread, calls `GetNativeCodeStartAddresses` and `GetILToNativeMapping3` after `JITCompilationFinished`, and exports the evidence. `ReJitProbe.Linux.cpp` shows that such a profiler loads in the Linux container. The capture path then stays Linux x64 only.

## Acceptance criteria

Proposed. They would add to criteria 1 to 213 and change none.

214. `TargetNativeMap.tryCreate` checks cover each error case, an unsorted input that it sorts, one IL offset with two native ranges, and a copy that later input changes cannot alter.
215. `resolve` checks cover the first and the last address of a range, the end address, an address before the body and at the body end, each special entry, and a body whose start differs from the bound range (`StaleCodeVersion`).
216. Sequence point checks use the PDB of the test assembly and hand-built points: an exact offset, an offset between points, an offset after the last point, an offset before the first point, and a hidden point, which gives `HiddenSequencePoint` and not a neighbor line.
217. A PDB of another MVID gives `ModuleMismatch`. A generic method, a non-zero native version, a second body, cold code, and rewritten IL each give their error.
218. Every result carries the four limitations, and `InlineesAttributedToCallSite` when the evidence does not exclude inlining.
219. All evidence in the checks is labeled synthetic. No profiler, native change, or live run is part of this milestone.

## What this leaves open

- Live map acquisition with a profiler component on Linux, which needs its own approval.
- Shared generics, ReJIT bodies, tiered code, cold code, and rich inline debug information.
- Whether optimized-code boundaries are fine enough to be useful for a selected line. That needs a live measurement.
- Every qualification blocker.

See [target-local discovery](target-local-discovery.md), [the discovery workflow](target-local-discovery-workflow.md), and [the research note](../../docsSrc/Research/clr-native-to-il-mapping.md).
