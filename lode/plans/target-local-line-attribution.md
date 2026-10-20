# Target-local line attribution

Status: approved on 2026-10-19 with the corrections in the Decision section, and implemented in `src/CozNet/TargetLineAttribution.fs`. No live run has used it.

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

## Decision

The supervisor approved on 2026-10-19 the portable resolver over supplied evidence, with five contract corrections. This revision records them before the implementation.
- No profiler component, no native change, no IL rewriting, and no live run.
- One preparation boundary ties the map, the method identity, the PDB, and the discovery outcome together. `resolve` takes only the prepared resolver.
- A range or identity that does not match gives `RangeMismatch` or `IdentityMismatch`. It does not claim that the code is stale. Matching addresses do not prove the code lifetime, the existence of an entry at each sample time, or the absence of address reuse.

## Exported map shape

The profiler path of `GetILToNativeMapping3` is `Debugger::GetILToNativeMapping` for a method that is not dynamic. It exports the sequence map of the `DebuggerJitInfo` of the body through `ExportILToNativeMap` ([debugger.cpp L2820-2935](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/debug/ee/debugger.cpp#L2820-L2935)).
`DebuggerJitInfo::SetBoundaries` builds that map ([functioninfo.cpp L1095-1200](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/debug/ee/functioninfo.cpp#L1095-L1200)):
- It keeps the JIT entries in their order. It drops an entry whose IL offset equals that of the entry before it.
- It sets the end of each entry to the start of the next entry that is not a call instruction. It marks the end of the last entry as unknown.
- It sorts by IL, with `PROLOG` first, then the IL offsets, then `NO_MAPPING`, and call-instruction entries last. Then it removes the call-instruction entries from the count ([debugger.h, `MapSortIL`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/debug/ee/debugger.h)).
- `ExportILToNativeMap` replaces an unknown end with the total code size of the body ([utils.cpp L128-150](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/debug/shared/utils.cpp#L128-L150)). That size is `getSizeOfTotalCode`, hot and cold together ([functioninfo.cpp L1254](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/debug/ee/functioninfo.cpp#L1254)).

What this establishes, and what the resolver does:

| Shape | Source | Resolver |
| --- | --- | --- |
| End is exclusive and equals the next start in native order | The end assignment above | Accepted. |
| Last entry ends at the total code size | `ExportILToNativeMap` replaces only an end marked unknown, and only the last JIT entry has that mark. When the last JIT entry is not a call instruction, it is exported and ends at the code size. | Accepted. An end above the code size is `UnsupportedMapShape`. |
| Zero-length entry | Two JIT entries with one native offset give it | Accepted. It never holds an address. |
| Array sorted by IL, not by native offset | `MapSortIL` | The resolver sorts a copy by native start. |
| One IL offset with several native ranges | IL sort of native entries | Accepted. |
| Native range before the first entry | The first JIT entry can start above 0 | Accepted as an uncovered interval. `resolve` gives `Uncovered`, never a neighbor line. |
| Native range of a removed call-instruction entry | The end search skips call entries, so a call entry between two other entries stays inside the earlier one. When the last JIT entries are call instructions, the search stops at the last entry, the last exported entry ends at the start of the last trailing call, and the call entries are removed. Earlier trailing calls stay inside that entry. So the range from the start of the last trailing call to the end of the code is not in the export. Review rounds 1 and 2 found this case. | That trailing range is uncovered, and `resolve` gives `Uncovered`. |
| Overlap of two ranges with a size above 0 | The end assignment assumes that the JIT order is native order. The source does not prove that order. | `UnsupportedMapShape`, not malformed evidence. |
| Dynamic method | That path uses instrumented boundaries | Out of scope. A dynamic method has no metadata token. |

Fixture with provenance: the retained Linux maps in `reliability/features/runtime-probes/data/native-mapping/linux` came from `GetILToNativeMapping` on .NET 10.0.0. For example, `linux-perf-registered-branch.json` tiles `[0, 78)` from a `PROLOG` entry to an `EPILOG` entry. That method was rewritten, and its offsets are original offsets through a registered map. So the checks use it only as an example of the shape, labeled `retained: runtime-probes linux-perf-registered-branch`.

## Identity

The future acquisition can supply these values, and the evidence holds only these:

| Field | Future source |
| --- | --- |
| `ModuleVersionId` | The MVID from the module metadata, through `GetModuleMetaData` |
| `MethodToken` | `GetFunctionInfo2` |
| `CodeStart` | `GetNativeCodeStartAddresses` |
| `Regions` | `GetCodeInfo4(CodeStart)`: the hot region and any cold region |
| `NativeBodyCount` | The count of `GetNativeCodeStartAddresses` for the function and IL version 0 |
| `Entries` | `GetILToNativeMapping3(CodeStart)`, as raw `uint32` values |
| `Label` | The source of the evidence, for example `synthetic: ...` or `retained: ...` |

There is no native version id. A ReJIT id, a runtime-event version id, and a code start are different values. A caller value of 0 would not prove one unrewritten body.

Checks at preparation:
- `Regions` must hold exactly one region, else `ColdCodeUnsupported`. The region must start at `CodeStart`.
- `NativeBodyCount` must be 1, else `MultipleBodiesUnsupported`.
- The region must equal the range of the candidate in the discovery outcome, else `RangeMismatch`. `CodeStart + size` must not overflow.
- The PE file must have the MVID, else `IdentityMismatch`. Its assembly name must equal the candidate assembly, else `IdentityMismatch`.
- The token must be a MethodDef row of the PE, with the type name, the method name, and the parameter types of the candidate symbol, else `IdentityMismatch`. The parameter types come from the method signature in the verified subset of `TargetSymbol`. A type outside that subset, a second wrapper such as `int32[]&`, a pinned type, a type with a modifier, or a `void` or `typedref` parameter gives a signature mismatch. So an overload with the same name is rejected, and a modifier never hides a difference. Review rounds 1 and 2 added these checks.
- A generic method or a method of a generic type gives `SharedGenericUnsupported`, from the generic parameter tables of the PE.
- The PDB is the embedded PDB, or the sibling PDB with the id of the CodeView entry, through the existing reader.
- The IL body size is the length of the IL code of the PE method body, without the method header. A method without a body gives `NoIlBody`. Review round 1 found that `MethodBodyBlock.Size` includes the header.
- The PE facts are read before the PDB. A bad PE body gives `AssemblyMalformed`, and a module without an assembly manifest gives `AssemblyMalformed`, not a PDB error.
- IL rewriting is a caller precondition: the record carries `IlRewriteNotExcluded`.

## Raw map decoding

- The raw IL offset is a `uint32`. `0xFFFFFFFF` is `NO_MAPPING`, `0xFFFFFFFE` is `PROLOG`, and `0xFFFFFFFD` is `EPILOG`.
- Any other value at or above the IL body size gives `IlOffsetBeyondBody` with the value. So an unknown special value is rejected, and it never resolves to the last line.
- A native start above its end gives `InvertedRange`.

## Sequence points

- The reader returns all points of the method: the IL offset, whether it is hidden, the document name, and the start and end line and column.
- The point of an IL offset is the visible or hidden point with the greatest offset at or below it. Its IL interval ends at the next point, or at the IL body size for the last point.
- A hidden point gives `HiddenSequencePoint`. An offset before the first point gives `NoSequencePoint`.
- The interval rule is an inference. Each result carries `SequencePointRangeInferred`.

## API

`src/CozNet/TargetLineAttribution.fs`, after `TargetDiscoveryWorkflow.fs`:

```fsharp
type TargetCodeRegion = { Start: uint64; Size: uint64 }

type TargetCodeEvidence = {
    Label: string
    ModuleVersionId: Guid
    MethodToken: int
    CodeStart: uint64
    Regions: TargetCodeRegion list
    NativeBodyCount: int
    Entries: struct (uint32 * uint32 * uint32) array   // raw IL offset, native start, native end
}

type TargetLineResolver   // opaque: Label, Symbol, Limitations, Uncovered (copies)

module TargetLineAttribution =
    val tryPrepare: outcome: TargetDiscoveryOutcome -> symbol: string -> assemblyPath: string -> evidence: TargetCodeEvidence
                    -> Result<TargetLineResolver, TargetLinePrepareError list>
    val resolve: resolver: TargetLineResolver -> ip: uint64 -> TargetLineResult

type TargetLineResult =
    | Line of TargetLineLocation   // document, start and end line and column, IL start, IL end
    | InProlog | InEpilog | Unmapped | Uncovered
    | HiddenSequencePoint of ilOffset: uint32
    | NoSequencePoint of ilOffset: uint32
    | OutsideBody
```

The limitations of each resolver: `SuppliedEvidence label`, `OptimizedBoundariesAreCoarse`, `SequencePointRangeInferred`, `InlineesAttributedToCallSite`, `IlRewriteNotExcluded`, `MapEntryTimingNotProven`, `CodeLifetimeNotProven`, and `NoCallchainAttribution`. `InlineesAttributedToCallSite` is always present, because no supplied field can prove that no callee was inlined.

`TargetSourceAnchor` gets an internal function that opens the PE and the matched PDB once and returns the MVID, the assembly name, the method row, the IL body size, and all sequence points. Its public behavior does not change.

## Usage

```fsharp
// The evidence is supplied. No live acquisition exists yet.
let evidence = { Label = "synthetic: two-entry map"; ModuleVersionId = mvid; MethodToken = token; CodeStart = start
                 Regions = [ { Start = start; Size = size } ]; NativeBodyCount = 1
                 Entries = [| struct (0xFFFFFFFEu, 0u, 8u); struct (0u, 8u, uint32 size) |] }
match TargetLineAttribution.tryPrepare outcome symbol assemblyPath evidence with
| Ok resolver -> TargetLineAttribution.resolve resolver sampleIp
| Error errors -> ...
```

## Acceptance criteria

These criteria replace the proposed criteria 214 to 219, before the implementation. They add to criteria 1 to 213 and change none.

214. `tryPrepare` checks cover `CandidateNotDiscovered`, `RangeMismatch`, `ColdCodeUnsupported`, `MultipleBodiesUnsupported`, an overflow of the code range, `IdentityMismatch` for the MVID, the assembly, and the method name, `SharedGenericUnsupported`, and the PDB errors.
215. Decoding checks cover the three special `uint32` values, an IL offset at the IL body size, an unknown special value, an inverted range, an end above the code size, an overlap, a zero-length entry, an uncovered start, one IL offset with two native ranges, and an unsorted input.
216. `resolve` checks cover the first and the last address of an entry, the end address, an address before and after the body, `Uncovered`, and each special entry.
217. Sequence-point checks use the PDB of the test assembly: an exact offset, an offset between points, the last interval bounded by the IL body size, an offset before the first point, and a hidden point that does not fall to a neighbor line.
218. A retained runtime map, labeled with its source, prepares with a matching synthetic identity and resolves each of its entries. Caller arrays changed after `tryPrepare` do not change the resolver, and its accessors return copies.
219. Every resolver carries the eight limitations. The case names differ from the discovery cases with similar meaning, so that an unqualified name is not ambiguous. All evidence in the checks is synthetic or retained, and labeled. No profiler, native change, or live run is part of this milestone.

## Implementation

- `src/CozNet/TargetLineAttribution.fs` holds the evidence, the errors, the limitations, the resolver, and `tryPrepare` and `resolve`.
- `TargetSourceAnchor.tryReadMethod` is internal. It opens the PE and the matched PDB once, and it gives the MVID, the assembly name, the type and method name, the generic flag, the IL body size, and all sequence points. `tryFindIn` uses the same reader and does not change.
- `tests/CozNet.Tests/TargetLineAttributionTests.fs` holds 10 checks. One of them, a PE file without metadata, runs only on Windows. The fixture method `LineFixture.Loop` has an IL body of 40 bytes and a hidden sequence point at IL offset 27. On 2026-10-20 the test project gave these values in both Debug (`Optimize=false`) and Release (`Optimize=true`), with portable PDBs.
  All 248 project tests passed in each configuration, with 10 line tests and 41 discovery tests in Release.
  The F# compiler does not promise this layout. The first line test checks the IL body size and the hidden point before the other tests use them, so a layout change from a compiler or build setting fails visibly. It does not pass silently.
- The retained runtime map belongs to another method. The check uses it only for its shape: it prepares, it tiles the body, and each IL offset resolves inside the IL body. Its lines have no meaning for the fixture.
- Two mutations were checked by hand. An end-inclusive lookup fails 4 checks, and a resolver that ignores hidden points fails 1.

## What this leaves open

- Live map acquisition with a profiler component on Linux, which needs its own approval.
- Shared generics, ReJIT bodies, tiered code, cold code, and rich inline debug information.
- Whether optimized-code boundaries are fine enough to be useful for a selected line. That needs a live measurement.
- Every qualification blocker.

See [target-local discovery](target-local-discovery.md), [the discovery workflow](target-local-discovery-workflow.md), and [the research note](../../docsSrc/Research/clr-native-to-il-mapping.md).
