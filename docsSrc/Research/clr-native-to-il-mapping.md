# Native IP to IL offset and source line

This explanation describes how a sampled native instruction pointer (IP) in a JIT-compiled method can map to an IL offset range and then to a Portable PDB sequence point.
It extends the [native range contract](bound-native-mapping.md).
It assumes Linux x64, `DOTNET_TieredCompilation=0`, `DOTNET_ReadyToRun=0`, and a runtime perf map.
The runtime sources use tag `v10.0.0`, revision `60629d14374c56f1cb51819049ad1fa529307f8d`. [26]
Microsoft Learn pages for the older profiling methods are .NET Framework pages. This file says so where it uses them.
A statement marked "Inference" has no direct source.

## Profiler APIs that return the map

`ICorProfilerInfo::GetILToNativeMapping` takes a `FunctionID` and fills an array of `COR_DEBUG_IL_TO_NATIVE_MAP`. [1]
`ICorProfilerInfo2` does not add a mapping method. The interface body has no `GetILToNativeMapping` member. [1]
`ICorProfilerInfo4::GetILToNativeMapping2` adds a `ReJITID` parameter. [1] [L2]
`ICorProfilerInfo9` adds `GetNativeCodeStartAddresses`, `GetILToNativeMapping3`, and `GetCodeInfo4`. [1]
`GetILToNativeMapping3` and `GetCodeInfo4` select one native body by its code start address. [1]
`ICorProfilerInfo4::GetCodeInfo3` selects code by `FunctionID` and `ReJITID`. [1]
Learn describes `ICorProfilerInfo9` as an interface for functions with multiple native code versions, available since .NET Core 2.1. [L3]

Signatures, from `corprof.idl` [1]:

```cpp
HRESULT GetILToNativeMapping(FunctionID functionId, ULONG32 cMap, ULONG32 *pcMap, COR_DEBUG_IL_TO_NATIVE_MAP map[]);
HRESULT GetILToNativeMapping2(FunctionID functionId, ReJITID reJitId, ULONG32 cMap, ULONG32 *pcMap, COR_DEBUG_IL_TO_NATIVE_MAP map[]);
HRESULT GetNativeCodeStartAddresses(FunctionID functionID, ReJITID reJitId, ULONG32 cCodeStartAddresses, ULONG32 *pcCodeStartAddresses, UINT_PTR codeStartAddresses[]);
HRESULT GetILToNativeMapping3(UINT_PTR pNativeCodeStartAddress, ULONG32 cMap, ULONG32 *pcMap, COR_DEBUG_IL_TO_NATIVE_MAP map[]);
HRESULT GetCodeInfo4(UINT_PTR pNativeCodeStartAddress, ULONG32 cCodeInfos, ULONG32* pcCodeInfos, COR_PRF_CODE_INFO codeInfos[]);
```

### Which code version each call describes

`GetILToNativeMapping` calls `GetILToNativeMapping2` with `ReJITID` 0. [2]
`GetILToNativeMapping2` takes the first native code version of the IL version and then calls `GetILToNativeMapping3`. [2]
In `v10.0.0`, `GetILToNativeMapping2` discards the result of `GetILCodeVersion(pMD, reJitId)`. It iterates a null `ILCodeVersion`. [2]
A null IL filter makes the iterator yield the implicit default native version first. [3]
Therefore `GetILToNativeMapping2` ignores `reJitId` in this release and returns the default body.
`GetCodeInfo3` stores the result correctly, but it also takes the first native version only. [2]
The runtime comments say that callers must use `GetILToNativeMapping3` or `GetCodeInfo4` to address a specific native version. [2]
`GetNativeCodeStartAddresses` lists every native version with a non-null code address, under the code versioning lock. [2]
`GetILToNativeMapping3` resolves the `MethodDesc` from the start address and finds the debugger JIT record for that address. [4]
It rejects wrapper stubs. It handles dynamic methods only when `TrackDynamicMethodDebugInfo` is set. [4]
It returns `CORPROF_E_DEBUGGING_DISABLED` when the runtime has no debugger interface. [2]

### When the calls are valid

The runtime publishes the code with `SetNativeCode` inside `JitCompileCodeLocked`. [5]
The wrapper then fires the ETW `MethodJitted` event, then `JITCompilationFinished` or `ReJITCompilationFinished`. [5]
The runtime writes the perf map line after the profiler callback returns. [5]
So the map and the code start address are available inside `JITCompilationFinished`.
A binding captured in that callback exists before the perf map shows the method.

### Thread rules

`GetILToNativeMapping`, `GetILToNativeMapping2`, `GetILToNativeMapping3`, `GetCodeInfo3`, and `GetCodeInfo4` use `PROFILER_TO_CLR_ENTRYPOINT_SYNC_EX`. [6]
A SYNC call is valid inside a profiler callback, or on a thread that the profiler created. [6]
The SYNC check passes automatically when the thread has no EE `Thread`. [6]
`GetNativeCodeStartAddresses` uses `ASYNC_EX`. It may run at any time from any thread. [6]
`GetILToNativeMapping3` has a `THROWS` contract and can take locks. [2]
Its first call can run debugger lazy initialization under the debugger lock. [4]
Therefore it is not safe in a signal handler. Call it from a callback or from a profiler worker thread.

## Map structure and geometry

`COR_DEBUG_IL_TO_NATIVE_MAP` has three `ULONG32` fields: `ilOffset`, `nativeStartOffset`, and `nativeEndOffset`. [7]
`CorDebugIlToNativeMappingTypes` defines `NO_MAPPING = -1`, `PROLOG = -2`, and `EPILOG = -3`. [7]
Learn gives only one-line field descriptions. It does not define ordering or end-offset semantics. [L4]
The source answers these questions.

- **Offsets are relative.** The offsets start at the method's code start. Cold code continues the offset space after the hot size. [8]
- **End is exclusive.** `SetBoundaries` sets each entry's `nativeEndOffset` to the next entry's `nativeStartOffset` in native order. It skips call-instruction entries. [9]
- **The last entry ends at the code size.** The runtime marks the last native entry `NATIVE_END_OFFSET_UNKNOWN`. `ExportILToNativeMap` replaces its end with the total code size. [9] [10]
- **The total size includes hot and cold code.** `m_sizeOfCode` comes from `getSizeOfTotalCode()`. [9]
- **No gaps or overlaps exist in native order.** The end offsets come from the next start, so the ranges tile the method. Inference: this holds only for entries that the map returns, because call-instruction entries are removed.
- **The returned array is sorted by IL, not by native offset.** After it computes the ends, the runtime sorts the map with `MapSortIL`. [9]
- **The IL sort order is fixed.** `PROLOG` entries come first, then normal IL offsets, then `EPILOG`, then `NO_MAPPING`. Ties sort by native start. [11]
- **Call-instruction entries are removed.** The runtime moves them to a separate call-site map and does not count them. [9]
- **One IL offset can map to several native ranges.** The runtime merges only an entry with the same IL offset as the entry before it in native order. [9]
- **ReJIT maps are translated.** When an instrumented IL map exists, the runtime translates to original IL offsets and drops entries between mapped offsets. [9]

To map an IP, sort a copy by `nativeStartOffset`. Then find the entry with `start <= ip - codeStart < end`.

## EventPipe and ETW events

### The live event

`MethodILToNativeMap` is event 190 on `Microsoft-Windows-DotNETRuntime`. [12]
It uses `JittedMethodILToNativeMapKeyword`, mask `0x20000`, at level Verbose. [12] [L5]
Version 1 has these fields: `MethodID`, `ReJITID`, `MethodExtent`, `CountOfMapEntries`, `ILOffsets[]`, `NativeOffsets[]`, `ClrInstanceID`, `ILVersionID`. [12]
The event carries native start offsets only. It has no end offsets. [12] [13]
The `ReJITID` argument is the native code version id, not the IL version id. `ILVersionID` carries the IL version id. [13]
`MethodExtent` is always 0. The comment says that JIT-compiled methods currently have one hot extent. [13]

The event builder reads uninstrumented bounds and skips the `0xFFFFFFFF` end marker. [13] [14]
It drops call-instruction entries and merges some repeated IL offsets. [14]
It sorts by IL offset, then by native offset. [14]
It caps the count at 7000 entries after the sort. [14] [13]
If the cap applies, the event loses the highest IL offsets, not the last native ranges.
Then a consumer cannot rebuild correct end offsets by sorting on native offset.
When the cap does not apply, the end of each range is the next native start. The end of the last range is `MethodSize`. Inference: this matches the profiler rule in [9].

The live event fires from `ETW::MethodLog::MethodJitted`. [13]
The runtime calls `MethodJitted` only when `JitKeyword` (`0x10`) is enabled at Verbose. [5]
A session that enables only `0x20000` gets no live map events.

### Correlation with MethodLoadVerbose

`MethodLoadVerbose` is event 143, keyword `JitKeyword` or `NGenKeyword`. [12]
Its fields are `MethodID`, `ModuleID`, `MethodStartAddress`, `MethodSize`, `MethodToken`, `MethodFlags`, the three name strings, `ClrInstanceID`, and in V2 `ReJITID`. [12] [L5]
`MethodID` is the `MethodDesc` address in both events. [13]
`MethodStartAddress` and `MethodSize` are the hot start and hot size. [13]
`MethodToken` is the `MethodDef` token from `GetMemberDef`. [13]
`MethodFlags` bits 7 to 9 hold the JIT optimization tier. [13] [15]
The tier values are `Unknown`, `MinOptJitted`, `Optimized`, `QuickJitted`, `OptimizedTier1`, `OptimizedTier1OSR`, `InstrumentedTier`, and `InstrumentedTierOptimized`. [16]
The runtime fires V1, without `ReJITID`, when the native code version id is 0. [13]
The default native version has id 0. [3]
Inference: with tiering off and no ReJIT, a body has version id 0. Correlate events by `(MethodID, MethodStartAddress)`.

### In-process delivery

Starting with .NET Core 2.2, `EventListener` can receive CoreCLR runtime events. [L6]
The in-process path is `NativeRuntimeEventSource`. It decodes the EventPipe payload against the generated managed event signature. [17]
The generator stops each signature at the first field with a `count` attribute. [17]
The decoder decodes only the declared parameters. [17]
So an `EventListener` gets `MethodID`, `ReJITID`, `MethodExtent`, and `CountOfMapEntries`, but not the offset arrays.
The generator builds only the `Microsoft-Windows-DotNETRuntime` provider. It does not build the rundown provider. [17]
An `EventListener` therefore cannot obtain the map.

### Out-of-process delivery and rundown

`DiagnosticsClient.StartEventPipeSession` takes `requestRundown`, default `true`. [L7]
EventPipe runs rundown when a session stops, before it fully closes the session. [18]
Rundown calls `ETW::EnumerationLog::EndRundown`, which emits DCEnd events. [18] [13]
The default rundown keyword is `0x80020139`. It includes `JittedMethodILToNativeMapRundownKeyword` (`0x20000`). [18] [12]
`MethodDCEndILToNativeMap` is event 150 on `Microsoft-Windows-DotNETRuntimeRundown`. It uses the same fields. [12]
Rundown walks the JIT code heap and emits a map for each body whose start matches a known code version. [13]
So methods JIT-compiled before the session started get maps, but only when the session stops.
Rundown is not a live feed. The process must still run when the session stops.

## Code-version identity

Learn and the design doc define a code version as native code plus its side tables and identifying configuration. [19]
That configuration includes a version id, the IL, the generic instantiation, and the optimization settings. [19]
Each entrypoint binds to one code version at a time. Generic instantiations can have different entrypoints. [19]
Tiered compilation produces a Tier0 body first and a Tier1 body later for hot methods. [20]
On-stack replacement (OSR) creates alternative code versions, called OSR methods, from Tier0 code. [21]
The OSR design says that the JIT generates the same debug info mappings for OSR methods. [21]

A method is eligible for tiering only when `g_pConfig->TieredCompilation()` is true. [22]
For an ineligible method, the tier is `Optimized`, or `MinOptJitted` when JIT optimization is disabled. [16]
Inference: with `TieredCompilation=0`, a method has no Tier0 body and no OSR body.
These sources still allow more than one body:

- ReJIT creates a new IL version and new native code. [19]
- A competing JIT can lose the publication race. The losing thread returns the winner's code. [5]
- A collectible assembly can unload, and the runtime can reuse an address. The [native range contract](bound-native-mapping.md) covers this.

The identity tuple for one body is: `FunctionID` or `MethodID`, native code start address, hot size, `ModuleID`, `MethodToken`, and the native version id.
`GetNativeCodeStartAddresses` returning exactly one address supports the one-body claim at that time. [2]

## Inlining

The JIT records a normal IL mapping from `gtStmtDI.GetRoot()`. [23]
`GetRoot` follows the inline context up to the call site in the root method. [23]
So code from an inlinee maps to the IL offset of the call in the caller.
The standard map never contains inlinee IL offsets.

Rich debug info keeps the inline tree and inlinee IL offsets. [24]
The JIT adds rich mappings only when `RichDebugInfo` is not zero. [23] [24]
The runtime stores rich data only when `DOTNET_RichDebugInfo` is set. [24]
The data appears only as `JittedMethodRichDebugInfo`, event 189, on `Microsoft-Windows-DotNETRuntimePrivate`. [12] [13]
The payload is a binary blob with inline tree nodes and `(NativeOffset, Inlinee, ILOffset, Source)` mappings. [13] [24]
No `ICorProfilerInfo` method returns rich debug info. Inference: the `corprof.idl` search found no such member. [1]
The runtime config describes `RichDebugInfo` as unsupported. [24]

## Shared generics

`GetILToNativeMapping2` rejects a typical method definition with type parameters still open. [2]
A profiler `FunctionID` therefore names an instantiated or shared `MethodDesc`.
`GetFunctionInfo` returns the token from `GetMemberDef`. [2]
`MethodLoadVerbose` sets `MethodToken` from `GetMemberDef` for generic methods. Flag `0x4` marks shared generic code. [13] [15]
The Portable PDB `MethodDebugInformation` table is indexed by `MethodDef` row id. [25]
So the `MethodDef` token selects the sequence points for every instantiation.
Inference: a `__Canon` shared body has one map. All instantiations that share it share that map. This doc does not directly cite the `__Canon` `FunctionID` rule.

## Portable PDB sequence points

A sequence point has an IL offset, start line, start column, end line, end column, and document. [25]
A hidden sequence point has start line and end line `0xfeefee`, and start column and end column 0. [25]
IL offsets of non-hidden sequence points are strictly ascending. [25]
A `document-record` changes the document for the records that follow it. [25]
The first point takes its document from `MethodDebugInformation.Document` or from `InitialDocument`. [25]
The spec does not state the IL range that a sequence point covers.
Inference: a point covers IL from its offset up to the next point's offset. This matches the debugger usage.
To find the line, choose the point with the greatest `Offset <= ilOffset`. If that point is hidden, report no line.

`System.Reflection.Metadata` exposes `MethodDebugInformation.GetSequencePoints()`. [L8]
`SequencePoint` has `Offset`, `StartLine`, `StartColumn`, `EndLine`, `EndColumn`, `Document`, `IsHidden`, and the `HiddenLine` field. [L8]
The Learn example maps a `MethodDef` token to the `MethodDebugInformation` handle by row number. [L8]

## Availability without a debugger or profiler

The runtime sets `CORJIT_FLAG_DEBUG_INFO` for every JIT compile when it has a debugger interface. The comment says "Debug info is always tracked". [27]
The runtime clears the flag for IL stubs and dynamic methods unless `TrackDynamicMethodDebugInfo` is set. [27]
The JIT reports IP mappings only when `compDbgInfo` is set. That value comes from the flag. [23] [28]
`COR_PRF_ENABLE_JIT_MAPS` is deprecated. The runtime always tracks maps and treats the flag as set. [1] [L1]
`COR_PRF_ENABLE_REJIT` is needed only for `RequestReJIT` and `RequestRevert`. [1]
The EventPipe map reads the stored debug info. It does not need a profiler. [13]
The profiler path asserts that a profiler is present. [4]

## Fidelity for optimized code

For a module with JIT optimizations enabled, `Debugger::getBoundaries` does not read PDB sequence points. [29]
It asks only for stack-empty and call-site boundaries. The comment says that the JIT would ignore PDB boundaries anyway. [29]
So map entries for optimized code fall at stack-empty IL points and call sites, not at PDB sequence points.
The JIT also removes mappings with equal native offsets. It keeps the higher IL offset unless the earlier mapping is a label. [23]
Learn says that release optimization can optimize sequence points away. [L9]
Learn says that optimized code has a less direct mapping to source. [L10]
An archived .NET Framework 4.0 page says that optimizations such as inlining and code motion severely affect correct mapping. [L11]

## Feasibility summary

**Verdict:** a sampled IP can map to an IL range and then to a sequence point. The result is approximate for optimized code.

**Route A: in-process profiler.**
In `JITCompilationFinished`, call `GetNativeCodeStartAddresses` and require exactly one address. [2]
Call `GetILToNativeMapping3` and `GetCodeInfo4` with that address, from the callback or a profiler thread. [2] [6]
Do not use `GetILToNativeMapping2` for ReJIT bodies, because it ignores `reJitId` in `v10.0.0`. [2]
This route gives end offsets and covers hot and cold code. [9]

**Route B: out-of-process EventPipe.**
Enable `Microsoft-Windows-DotNETRuntime` with `0x10 | 0x20000` at Verbose. Use `DiagnosticsClient` from another process. [5] [12] [L7]
Correlate `MethodILToNativeMap` with `MethodLoadVerbose` by `MethodID` and start address. [13]
Use rundown at session stop for methods compiled earlier. [18]
This route gives start offsets only. It loses entries after 7000. It covers hot code only. [13] [14]
`EventListener` cannot deliver the arrays. [17]

**Identity that rejects stale code:**
`MethodID` or `FunctionID`, code start address, size, `ModuleID`, `MethodToken`, native version id (0 under the assumed settings), plus the collection epoch and unload invalidation from the [native range contract](bound-native-mapping.md).
Reject a sample if the IP is outside `[start, start + size)` of the captured body.

**Granularity limits:**

- Ranges fall on stack-empty and call-site boundaries, not on PDB sequence points. [29]
- Inlinee code maps to the caller's call-site IL offset. [23]
- `PROLOG`, `EPILOG`, and `NO_MAPPING` ranges have no IL offset. Report them as unattributed.
- IL ranges can repeat in several native ranges. Sum them per sequence point.

**Open uncertainties:**

- The Portable PDB spec does not state the IL range rule for a sequence point.
- No source here proves that the shared `__Canon` body has exactly one `FunctionID` for all instantiations.
- The `GetILToNativeMapping2` `reJitId` defect needs a runtime check, and later releases can fix it.
- Whether the JIT splits hot and cold code for JIT-compiled methods on Linux x64 is not verified. The event comment says one hot extent. [13]
- The tiling claim excludes removed call-instruction entries. A runtime test must confirm complete coverage.
- No measurement here shows how much attribution error optimized code adds.

## Primary sources

Runtime base: `https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/`.

1. [`corprof.idl`: map types](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/inc/corprof.idl#L85-L103), [flags](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/inc/corprof.idl#L514-L527), [`GetILToNativeMapping`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/inc/corprof.idl#L3072-L3083), [`ICorProfilerInfo2`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/inc/corprof.idl#L3108-L3500), [`GetCodeInfo3` and `GetILToNativeMapping2`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/inc/corprof.idl#L3872-L3915), [`ICorProfilerInfo9`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/inc/corprof.idl#L4115-L4125).
2. [`proftoeeinterfaceimpl.cpp`: `GetCodeInfo3`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/proftoeeinterfaceimpl.cpp#L2487-L2560), [`GetFunctionInfo`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/proftoeeinterfaceimpl.cpp#L4753-L4815), [`GetILToNativeMapping` and `GetILToNativeMapping2`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/proftoeeinterfaceimpl.cpp#L4822-L4945), [`GetNativeCodeStartAddresses`, `GetILToNativeMapping3`, `GetCodeInfo4`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/proftoeeinterfaceimpl.cpp#L6397-L6603).
3. [`codeversion.cpp` iterator](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/codeversion.cpp#L462-L536) and [`codeversion.h` `GetVersionId`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/codeversion.h#L735-L746).
4. [`debugger.cpp` `Debugger::GetILToNativeMapping`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/debug/ee/debugger.cpp#L2820-L2935).
5. [`prestub.cpp` event and callback order](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/prestub.cpp#L757-L899) and [publication](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/prestub.cpp#L907-L1000).
6. [`proftoeeinterfaceimpl.cpp` entrypoint rules](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/proftoeeinterfaceimpl.cpp#L60-L74) and [macros](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/proftoeeinterfaceimpl.cpp#L212-L252).
7. Same as [1], lines 85-103.
8. [`debugger.h` offset translation](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/debug/ee/debugger.h#L1360-L1406).
9. [`functioninfo.cpp` `SetBoundaries`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/debug/ee/functioninfo.cpp#L1005-L1200) and [code size](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/debug/ee/functioninfo.cpp#L1251-L1254).
10. [`utils.cpp` `ExportILToNativeMap`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/debug/shared/utils.cpp#L128-L169).
11. [`debugger.h` `MapSortIL`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/debug/ee/debugger.h#L1733-L1818).
12. `ClrEtwAll.man`: [keyword](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/ClrEtwAll.man#L48-L49), [flags map](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/ClrEtwAll.man#L615-L624), [`MethodLoadVerbose_V2` template](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/ClrEtwAll.man#L2365-L2376), [map templates](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/ClrEtwAll.man#L2650-L2688), [`MethodLoadVerbose` events](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/ClrEtwAll.man#L3915-L3928), [event 190](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/ClrEtwAll.man#L3984-L3994), [rundown keyword](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/ClrEtwAll.man#L4338-L4364), [events 149 and 150](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/ClrEtwAll.man#L5202-L5224), [private provider](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/ClrEtwAll.man#L5448-L5476), [rich debug info](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/ClrEtwAll.man#L7415-L7418).
13. `eventtrace.cpp`: [V1 or V2 macro](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/eventtrace.cpp#L86-L92), [rundown](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/eventtrace.cpp#L2123-L2215), [`MethodJitted`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/eventtrace.cpp#L3525-L3558), [`SendMethodEvent`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/eventtrace.cpp#L4438-L4600), [`SendMethodILToNativeMapEvent`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/eventtrace.cpp#L4715-L4797), [`SendMethodRichDebugInfo`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/eventtrace.cpp#L4804-L4900), [code heap walk](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/eventtrace.cpp#L4990-L5070).
14. [`debugdebugger.h` `ILToNativeMapArrays`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/debugdebugger.h#L141-L216) and [`debugdebugger.cpp` sort and `AddEntry`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/debugdebugger.cpp#L1609-L1796).
15. [`eventtracebase.h` tier shift](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/inc/eventtracebase.h#L940-L952).
16. [`method.hpp` tier enum](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/method.hpp#L2248-L2260) and [`prestub.cpp` `GetJitOptimizationTier`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/prestub.cpp#L1174-L1226).
17. [`NativeRuntimeEventSource.cs`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/NativeRuntimeEventSource.cs#L24-L79), [generator cut and providers](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/libraries/System.Private.CoreLib/gen/NativeRuntimeEventSourceGenerator.cs#L324-L398), [payload decoder](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/EventPipePayloadDecoder.cs#L15-L138).
18. [`ep.c` default rundown keyword](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/native/eventpipe/ep.c#L27), [rundown at disable](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/native/eventpipe/ep.c#L640-L672), [`ep_rt_execute_rundown`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/eventing/eventpipe/ep-rt-coreclr.h#L789-L801).
19. [`code-versioning.md`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/docs/design/features/code-versioning.md#L85-L133).
20. [`tiered-compilation.md`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/docs/design/features/tiered-compilation.md#L53-L58).
21. [`OnStackReplacement.md` OSR methods](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/docs/design/features/OnStackReplacement.md#L120-L129) and [debug info](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/docs/design/features/OnStackReplacement.md#L580-L590).
22. [`method.cpp` tiering eligibility](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/method.cpp#L2865-L2885).
23. [`codegenlinear.cpp` root mapping](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/jit/codegenlinear.cpp#L425-L446), [`debuginfo.cpp` `GetRoot`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/jit/debuginfo.cpp#L152-L171), [`codegencommon.cpp` `genIPmappingGen`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/jit/codegencommon.cpp#L6272-L6374).
24. [`cordebuginfo.h` rich structures](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/inc/cordebuginfo.h#L411-L433), [`jitconfigvalues.h`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/jit/jitconfigvalues.h#L350), [`clrconfigvalues.h`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/inc/clrconfigvalues.h#L212), [`codeman.cpp`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/codeman.cpp#L1803).
25. [`PortablePdb-Metadata.md` sequence points](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/docs/design/specs/PortablePdb-Metadata.md#L104-L180).
26. [Tag `v10.0.0`](https://github.com/dotnet/runtime/releases/tag/v10.0.0).
27. [`jitinterface.cpp` dynamic methods](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/jitinterface.cpp#L10687-L10692) and [debug info flag](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/jitinterface.cpp#L12950-L12969).
28. [`compiler.cpp` `compDbgInfo`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/jit/compiler.cpp#L2129-L2130).
29. [`debugger.cpp` `getBoundaries`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/debug/ee/debugger.cpp#L3252-L3300) and [boundary types](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/inc/cordebuginfo.h#L25-L48).

Microsoft Learn:

- L1. [ICorProfilerInfo::GetILToNativeMapping (.NET Framework page)](https://learn.microsoft.com/en-us/dotnet/framework/unmanaged-api/profiling/icorprofilerinfo-getiltonativemapping-method).
- L2. [ICorProfilerInfo4::GetILToNativeMapping2 (.NET Framework page)](https://learn.microsoft.com/en-us/dotnet/framework/unmanaged-api/profiling/icorprofilerinfo4-getiltonativemapping2-method).
- L3. [ICorProfilerInfo9 interface](https://learn.microsoft.com/en-us/dotnet/core/unmanaged-api/profiling/icorprofilerinfo9-interface).
- L4. [COR_DEBUG_IL_TO_NATIVE_MAP structure](https://learn.microsoft.com/en-us/dotnet/core/unmanaged-api/debugging/icordebug/cor-debug-il-to-native-map-structure).
- L5. [Method runtime events](https://learn.microsoft.com/en-us/dotnet/fundamentals/diagnostics/runtime-method-events).
- L6. [What's new in .NET Core 2.2](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-core-2-2).
- L7. [Microsoft.Diagnostics.NETCore.Client API](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/microsoft-diagnostics-netcore-client).
- L8. [SequencePoint struct](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.metadata.sequencepoint).
- L9. [OptimizationLevel enum](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.optimizationlevel).
- L10. [JIT optimization and debugging (Visual Studio)](https://learn.microsoft.com/en-us/visualstudio/debugger/jit-optimization-and-debugging).
- L11. [CLR Debugging Architecture (archived .NET Framework 4.0 page)](https://learn.microsoft.com/en-us/previous-versions/dotnet/netframework-4.0/bb384548(v=vs.100)).
