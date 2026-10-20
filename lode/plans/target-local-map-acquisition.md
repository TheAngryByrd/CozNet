# Target-local map acquisition

Status: proposed on 2026-10-20. Design and source audit only. It needs supervisor approval before any implementation. No native code exists for it, and no profiler was started.

## Goal

A small Linux CLR profiler component reads the native-to-IL map of one selected method and gives it, unchanged, to the existing [line attribution resolver](target-local-line-attribution.md). The facts are in [the research note](../../docsSrc/Research/clr-native-to-il-mapping.md), sections "Profiler APIs that return the map" and "Facts for map acquisition", pinned to dotnet/runtime `v10.0.0`.

## Scope of the first increment

- One method, selected by the caller before startup: a module file name and a MethodDef token.
- The method is not generic, its IL is not rewritten, and it is compiled once as optimized code. `DOTNET_TieredCompilation=0` and `DOTNET_ReadyToRun=0` stay required, as `CodeBinding` checks.
- Startup loading only. Attachment to a running process is out of scope.

## Loading and interfaces

- Startup loading uses `CORECLR_ENABLE_PROFILING=1`, `CORECLR_PROFILER={GUID}`, and `CORECLR_PROFILER_PATH`, as `native/rejit/ReJitProbe.Linux.cpp` and `native/VerifyTargetLocalTransitions.ps1:60-62` already do in the Linux container.
- Callback interface: the object answers `IUnknown`, `ICorProfilerCallback`, and `ICorProfilerCallback2`, because the runtime refuses a profiler without `ICorProfilerCallback2`. It answers no later callback version. So the runtime never calls the ReJIT callbacks of `ICorProfilerCallback4`.
- Info interface: in `Initialize`, the component asks the given `IUnknown` for `ICorProfilerInfo9`, which holds `GetNativeCodeStartAddresses`, `GetILToNativeMapping3`, and `GetCodeInfo4`. Without it, the outcome is `InfoUnavailable`, and the component asks for no events.
- Event mask: `COR_PRF_MONITOR_JIT_COMPILATION`, for `JITCompilationFinished`, and `COR_PRF_MONITOR_MODULE_LOADS`, for `ModuleUnloadStarted`. No high event mask. No `COR_PRF_DISABLE_INLINING`, `COR_PRF_ENABLE_REJIT`, `COR_PRF_DISABLE_ALL_NGEN_IMAGES`, or enter-leave flags.
- The selection is read once in `Initialize`, from `COZNET_MAP_MODULE` and `COZNET_MAP_TOKEN`. A missing or malformed value gives `SelectionInvalid`, and the component asks for no events.

## When it reads, and where

All reads run inside `JITCompilationFinished`. A profiler callback is a thread where the info calls are allowed, and the runtime sets the native address before the callback.
1. `GetFunctionInfo2` gives the module id and the token. A token that is not the selected one returns at once.
2. `GetModuleInfo2` gives the module file name. A name that is not the selected one returns at once.
3. `GetModuleMetaData(moduleId, ofRead, IID_IMetaDataImport)` and `GetScopeProps` give the MVID.
4. `GetNativeCodeStartAddresses(functionId, 0, 2, &count, starts)`.
5. `GetCodeInfo4(starts[0], 2, &regionCount, regions)`.
6. `GetILToNativeMapping3(starts[0], 0, &entryCount, nullptr)`, and then a second call with a buffer of that count.

Nothing runs in the perf signal handler. The callback copies into storage that the component allocates once in `Initialize`, with a capacity of 4096 map entries. It takes no lock of its own. Two threads can compile the selected method at the same time, and only one body is published. So the first callback claims the record with one atomic compare-exchange, and a later callback only sets `SecondNotification`. A later reader sees a record only after its claimant marks it complete with a release store.

## Outcomes

The component keeps one record. Each failed step keeps its HRESULT and stops the capture. Nothing is retried.

| Outcome | Cause |
| --- | --- |
| `NotSeen` | No selected method finished compilation. |
| `Captured` | All steps succeeded. |
| `InfoUnavailable` | No `ICorProfilerInfo9`. |
| `SelectionInvalid` | The selection variables are missing or malformed. |
| `MetadataFailed hr` | Step 3 failed. |
| `StartsFailed hr` | Step 4 failed, including `ERROR_INSUFFICIENT_BUFFER` for more than two bodies, with the count kept. |
| `CodeInfoFailed hr` | Step 5 failed. |
| `MapFailed hr` | Step 6 failed, including `CORPROF_E_DEBUGGING_DISABLED`. |
| `MapTooLarge count` | The map has more than 4096 entries. |
| `MapCountChanged first second` | The count of the second map call differs from the first. |
| `SecondNotification` | A second `JITCompilationFinished` for the selected method, from a concurrent compilation or a later one. The first record stays, and this flag is added. Step 4 of the first record shows the published bodies at its own time. |

The component does not drop, sort, or merge map entries, and it keeps every region and the body count. A second body, a cold region, a zero-length entry, and a gap stay in the evidence for the resolver to reject or report.

## Lifecycle

- JIT completion: the only capture point.
- Unloading: `ModuleUnloadStarted` for the module of a captured record sets `UnloadedBeforeRead`. The first increment uses a module that is not collectible, so this flag is expected to stay false.
- ReJIT and code replacement: the component cannot request ReJIT. Tiering is off. Another body would show as a body count above 1 at capture time. A body that appears after capture is not detected. The record states the time of capture and claims nothing after it.
- Shutdown: `Shutdown` sets `Closed`. A read after it gives the record with `Closed` set.

The capture identity is the function id, the module id, the MVID, the token, the code start, the regions, and the body count at capture time. It does not prove the code lifetime. It does not prove that the code stayed at that address for the sampled run.

## Evidence transfer

- The component exports one C function, `coz_map_read(CozMapCapture* out, CozMapEntry* entries, uint32_t capacity, uint32_t* count)`. It copies the record and the raw entries. `CozMapCapture` starts with a layout version and its own size.
- The sample loads the library from `CORECLR_PROFILER_PATH` with `NativeLibrary.Load`, after `runBound` returns. So the read stays out of the timed work. The selected method compiles when `bind ()` calls it once, before the run.
- A new portable parser, `TargetMapCapture.tryParse`, turns the copied bytes into `TargetCodeEvidence`. It checks the layout version and size, and it keeps the raw `uint32` entries. Its label is `captured: <outcome> pid <pid> profiler <library sha256>`.
- The evidence then goes through `TargetLineAttribution.tryPrepare` with the discovery outcome of the same run. The existing checks compare the region with the bound perf-map range, the MVID and the token with the PE, and the map with the IL body.

## Observer effects

- `JITCompilationStarted`, `JITCompilationFinished`, and `JITInlining` callbacks run for every method that compiles. They add JIT time. The component answers `JITInlining` with `TRUE`, so it keeps every inline decision.
- The first `GetILToNativeMapping3` call initializes the lazy data of the debugger interface under its lock.
- A loaded profiler is present for the whole process. The source audit found no JIT flag that `COR_PRF_MONITOR_JIT_COMPILATION` sets. Other effects of a present profiler on the timed run are not measured.

## Recommended implementation scope

One increment with no profiler startup and no live run:
1. `native/mapprofiler/MapProfiler.Linux.cpp`: the profiler object, the callbacks, the outcome record, and `coz_map_read`, in the raw-vtable style of `ReJitProbe.Linux.cpp`.
2. `native/mapprofiler/MapCaptureChecks.Linux.cpp`: deterministic checks with a fake `ICorProfilerInfo9` that returns chosen values and HRESULTs. They cover each outcome, an insufficient buffer, a count change, a second notification and a concurrent claim, a cold region and a second body kept raw, a map with special values, zero-length entries, and gaps kept unchanged, and the unload and shutdown flags.
3. The build of the library and the checks in the existing compiler image.
4. `src/CozNet/TargetMapCapture.fs`: the portable parser, with checks on synthetic byte layouts, including a wrong version, a short buffer, and each outcome.
5. Docs: the Reference section of the export and the parser, and the limits in the Explanation.

A later increment, with its own approval, would start the profiler once in the Linux container for the existing two-method fixture.

## Acceptance criteria

Proposed. They would add to criteria 1 to 219 and change none.

220. The native checks cover each outcome in the table, with the HRESULT kept, and run in the compiler image with a pass count above 0.
221. The native checks show that the component keeps raw entries, regions, and the body count unchanged, and never retries.
222. The parser checks cover a wrong layout version, a short buffer, each outcome, and a captured record that `TargetLineAttribution.tryPrepare` accepts with a matching synthetic outcome.
223. No profiler startup, no change of the existing native library, and no live run.

## What this leaves open

- Late attachment, ReJIT bodies, tiered code, cold code, shared generics, and dynamic methods.
- A measurement of the observer effect on the timed run.
- Every qualification blocker.

See [target-local line attribution](target-local-line-attribution.md) and [the research note](../../docsSrc/Research/clr-native-to-il-mapping.md).
