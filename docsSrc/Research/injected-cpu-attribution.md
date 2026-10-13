# CPU attribution for injected instructions

Date: 2026-10-13.
Runtime revision: `60629d14374c56f1cb51819049ad1fa529307f8d`.
This is a source review. No timing experiment or debugger session was run for this report.

## Decision

The reviewed profiler APIs do not expose an option to return raw rewritten-IL boundaries for this ordinary rewritten method.
Their normal path returns a debugger sequence map after instrumentation remapping and boundary removal.
That map cannot generally identify the injected countdown instructions exactly. [R1][R2][R3]

Test an explicit diagnostic mode that never registers the instrumentation map.
Retain the original-to-rewritten relationship privately and classify the returned rewritten-IL boundaries against injected instructions.
This mode must disclaim debugger source-stepping compatibility.
Keep the current registered map in the normal mode until that experiment establishes useful native-range separation.
Treat inline countdown CPU as unresolved instrumentation cost until the ranges are proved.

## Current CozNet behavior

`Profiler.cpp` records each original instruction's offset and its rewritten offset.
It submits those pairs through `SetILInstrumentedCodeMap`, with `fAccurate=TRUE`.
It does not submit separate entries for injected instructions. [L1]

`Sampler.h` calls `GetILToNativeMapping` after JIT compilation.
It matches sampled addresses to the returned native intervals and original IL offsets. [L2]
The Linux perf bridge currently credits an exact method-symbol match. [L3]
Neither path establishes a separate native interval classification for inline countdown code.

## Verified CLR data path

| Stage | Pinned implementation | Consequence |
|---|---|---|
| Profiler API | `GetILToNativeMapping` forwards to version 2. Version 2 resolves a native code address and calls version 3. [R1] | Changing API version selects a code version or address, not raw instrumented offsets. |
| Version 3 | Calls `g_pDebugInterface->GetILToNativeMapping`. Its arguments contain no instrumentation-map selector. [R2] | The caller cannot request `BoundsType::Instrumented` through this API. |
| Ordinary managed method | `Debugger::GetILToNativeMapping` obtains `DebuggerJitInfo` and exports its sequence map. [R3] | Results depend on the debugger's processed mapping. |
| Sequence-map construction | `DebuggerJitInfo::SetBoundaries` translates rewritten offsets to original offsets and removes adjacent duplicate original offsets. [R4] | Multiple rewritten regions can collapse into one returned original-IL interval. |
| Instrumentation translation | `TranslateToInstIL` rounds down to the preceding map entry. [R5] | Missing inserted offsets do not become independent unmapped intervals. |

For example, the runtime's own comment maps rewritten offset 44 back to original offset 6 between `(6,36)` and `(8,50)`.
It then removes the duplicate boundary for original offset 6. [R4]
A profiler cannot reverse that many-to-one transformation from the returned table alone.

The runtime keeps lower-level instrumented boundaries internally.
`DebugInfoManager` can select instrumented or uninstrumented data from its compressed debug-information store. [R14]
This internal availability is not an exposed profiler API contract.
Direct access would require unsupported runtime internals or a runtime change.

The dynamic-method branch in `Debugger::GetILToNativeMapping` directly requests instrumented boundaries. [R3]
CozNet rewrites an ordinary metadata method, so that branch does not apply.
Changing the workload into a dynamic method would not establish support for instrumenting existing application methods.

## Other API paths do not resolve the issue

### EventPipe and runtime events

The runtime's method IL/native-map event uses `WalkILOffsets` with `BoundsType::Uninstrumented`. [R7]
The reviewed .NET 10 event path therefore does not recover the missing raw rewritten offsets.
Code-version and address information remain useful for lifecycle tracking, but do not change this mapping choice.

### Debugger instrumentation map

`ICorDebugILCode2::GetInstrumentedILMap` returns the profiler-supplied relationship between rewritten and original IL offsets. [R8]
It does not add a raw native-range map.
CozNet already owns that IL relationship, so this API does not recover removed native boundaries.

### `fAccurate=FALSE`

`COR_IL_MAP.fAccurate` describes confidence in an IL mapping. It is not a documented instrumentation-ownership tag. [R9]
The inspected translation code reads `oldOffset` and `newOffset`, but does not use `fAccurate` to classify or exclude ranges. [R5][R6]
`COR_DEBUG_IL_TO_NATIVE_MAP` also has no returned accuracy or ownership field. [R9]
Changing this flag cannot supply the required sample filter through the reviewed path.

### Sentinel or synthetic original offsets

The map contract says every `oldOffset` refers to an offset in the original unmodified IL.
It requires increasing order and entries for the original PDB sequence points. [R10]
Using `NO_MAPPING`, a fabricated offset, or alternating sentinel entries as original offsets violates that model.
Those values must not become a profiler-private channel.
The API's special native-map values do not grant permission to insert fake source offsets into the instrumentation map.

### Read before registering the map

`fStartJit` means the first map-setting call for a `FunctionID`, not a requirement to call during `JITCompilationStarted`.
The pinned IDL states this explicitly. [R10]
The setter's inspected validation does not enforce a JIT-start-only callback. [R15]
Thus the reason to reject delayed installation is cache consistency, not the parameter's name.

Consider the proposed order:

1. Rewrite IL during `JITCompilationStarted`, without registering its map.
2. Request the native map during `JITCompilationFinished`.
3. Register the original-to-rewritten map afterward.

Step 2 can expose rewritten offsets because the instrumentation relationship is absent when the debugger first builds its sequence map.
However, `LazyInitBounds` runs once per `DebuggerJitInfo`, guarded by `m_fAttemptInit`. [R16]
The map getter calls that initialization before returning the cached entries. [R17]
Step 3 stores the module's instrumentation map and a flag; it does not rebuild those existing cached entries. [R11]
Later requests can therefore continue to expose rewritten offsets where consumers expect original offsets.
The debugger also consumes instrumentation mappings when translating source sequence points. [R12]
Temporarily clearing, replacing, or delaying the map risks inconsistent cached maps and source stepping.
Do not adopt that ordering trick as the implementation.

A separate process can test this exact sequence as a diagnostic experiment.
Record the map before installation, the map after installation, and source stepping with a debugger attached.
Expect no automatic cache refresh from the inspected setter. Do not treat an `S_OK` return as proof of refresh.
It must not be described as proof of debugger-safe attribution in the actual profiling process.
Nor does an IL/native sequence map promise a distinct range for every original or injected instruction.

### Omit registration for the whole diagnostic process

This is different from delayed installation.
Never call `SetILInstrumentedCodeMap` for the test method, and never replace the map after a getter has cached it.
Without a supplied map, the inspected sequence-map path retains rewritten IL offsets instead of applying the original-offset translation. [R3][R4]
This uses the existing profiler getter without a private runtime API or fabricated offsets.
It is a plausible diagnostic prototype and should be tested.

Retain each original instruction's rewritten offset and every injected instruction's rewritten offset in profiler-owned data.
Read the native map in `JITCompilationFinished` before sampling starts.
Classify its intervals as proven application code, proven injected code, mixed, or unknown.
Translate proven application locations through the private map for CozNet source reporting.
Do not export that private translation as evidence that an attached debugger can step correctly.
The debugger and other runtime-map consumers will lack the normal original-IL relationship in this mode.

Raw mapping granularity is the remaining feasibility question.
For optimized methods, the debugger requests stack-empty and call-site boundaries, rather than every PDB or IL instruction boundary. [R18][R19]
The JIT also removes some mappings that share a native offset. [R20]
Thus bypassing instrumentation remapping removes one source of information loss, not all sources.

In particular, a conditional backward branch can already have operands on the IL evaluation stack.
A countdown inserted immediately before that branch does not necessarily introduce an empty-stack boundary at the start of the gate.
Its gate and the preceding application instructions can therefore lack a distinct requested boundary.
This is a placement-dependent risk, not a finding that all gates must merge.
Inspect the actual rewritten IL stack states and JIT disassembly before classifying each interval.

If mixed ranges remain, test gate placement at empty-stack basic-block boundaries in the restricted loop.
Do not introduce per-edge helper calls or disable JIT optimization merely to improve mapping without measuring their separate perturbation.
Reject exact-exclusion claims if any credited interval still contains injected instructions.

## Practical alternatives

| Alternative | Benefit | Limit and decision |
|---|---|---|
| Non-inlined helper gate on each backedge | Most gate CPU has a separate method range that the collector can exclude. | Restores per-edge call overhead. Call preparation, call instructions, and return effects remain in the target. Measure it as a comparison, not exact exclusion. |
| Inline countdown plus rare non-inlined helper | Keeps expensive service outside the hot loop. The helper's known ranges can be excluded. | Countdown instructions remain mixed with selected code. Use explicit perturbation and accuracy limits until separation is proved. |
| Disassembly-based exclusion | Can identify a known gate in one fixed native-code version for a diagnostic workload. | Optimization can combine, move, or eliminate instructions. Pattern matches are not a portable ownership contract. Do not generalize one verified loop. |
| Native patchpoints or runtime extension | Could preserve injected-instruction ownership in the JIT and expose native ranges. | The runtime OSR patchpoint mechanism is internal, with JIT-owned control flow and state. It is not an existing profiler extension point. [R13] |
| Arbitrary suspension instead of polls | Avoids inline gate instructions. | The reviewed stack/suspension APIs do not establish a safe selective delay mechanism. This remains separate runtime work. [L4] |

Even perfect instruction exclusion would not remove instrumentation's effects on register allocation, optimization, scheduling, or cache behavior.
Measure the instrumented zero-speedup control against uninstrumented work.
Do not subtract one average overhead value and claim that each sampled observation is corrected.

## Concrete next implementation

1. Add an isolated diagnostic mode that omits instrumentation-map registration for the entire process.
2. Preserve the normal sorted map and source identity in the existing mode. Retain the same pairs privately in diagnostic mode.
3. Export rewritten IL, injected offset sets, raw native-map intervals, code-version identity, and JIT disassembly for one fixed loop.
4. Compare every proposed excluded range with disassembly. Keep merged and missing boundaries explicitly mixed or unknown.
5. Keep service helpers non-inlined and register their native ranges through the JIT lifecycle.
6. Report current method-level matches as instrumented-method samples rather than exclusive original-work CPU.
7. Keep the countdown comparison independent. Add measured control cost and prediction error to the support decision.
8. Require positive evidence for excluded ranges. Unknown and unmapped samples must never imply profiler work.
9. If raw boundaries remain insufficient, test restricted empty-stack placement before pursuing a runtime API or JIT extension.

The next prototype can improve cost while preserving honest limits.
The present API evidence does not prove the full requirement of excluding every injected instruction from selected CPU.

## Sources

[L1]: ../../native/Profiler.cpp
[L2]: ../../native/Sampler.h
[L3]: ../../tests/CozNet.LinuxPerf/SampledIntervention.cs
[L4]: probe-overhead-options.md
[R1]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/proftoeeinterfaceimpl.cpp#L4822-L4940
[R2]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/proftoeeinterfaceimpl.cpp#L6504-L6548
[R3]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/debug/ee/debugger.cpp#L2820-L2932
[R4]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/debug/ee/functioninfo.cpp#L1049-L1184
[R5]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/debug/ee/functioninfo.cpp#L1340-L1415
[R6]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/debuginfostore.cpp#L709-L739
[R7]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/eventtrace.cpp#L4748-L4777
[R8]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/inc/cordebug.idl#L6029-L6049
[R9]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/inc/cordebug.idl#L54-L91
[R10]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/inc/corprof.idl#L3002-L3035
[R11]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/debug/ee/functioninfo.cpp#L1901-L1909
[R12]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/debug/ee/debugger.cpp#L3181-L3202
[R13]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/jit/patchpoint.cpp#L11-L203
[R14]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/debuginfostore.cpp#L1115-L1140
[R15]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/proftoeeinterfaceimpl.cpp#L4457-L4521
[R16]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/debug/ee/functioninfo.cpp#L885-L943
[R17]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/debug/ee/debugger.h#L1550-L1567
[R18]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/debug/ee/debugger.cpp#L3252-L3300
[R19]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/inc/cordebuginfo.h#L22-L47
[R20]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/jit/codegencommon.cpp#L6287-L6367
