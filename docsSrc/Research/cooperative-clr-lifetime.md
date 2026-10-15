# Why cooperative probes need a CLR lifetime contract

This explanation describes the identity and lifetime requirements for controlled injected probes.
The [opt-in binding](../Reference/probe-identity.md) implements literal identities and permanent invalidation.
The [bound cooperative client](../Reference/bound-cooperative.md) checks this lifetime around payment admission and receipt acceptance.
Sampled capacity and physical prediction remain incomplete.
It does not claim that CozNet pins CLR code or supports arbitrary application methods.
The sources below use the runtime revision already pinned by the native build: `60629d14374c56f1cb51819049ad1fa529307f8d`.

## An injected identity is not a native code version

A probe needs an identity that belongs to the instrumented body that called it.
A query for the current global version cannot provide this property.
An old body could call the helper after the profiler replaces that global value.

The bound rewrite embeds an immutable instrumentation generation and a site identifier in each probe call.
The entry site and each backward-branch site can have separate identifiers.
The worker identity remains a property of the registered owner thread.
The profiler associates the generation with a module-load instance, method token, and selected `FunctionID`.
Tokens alone are insufficient because they belong to one module.

This generation identifies rewritten IL, not a unique native body.
The CLR separates IL versions from native versions.
Several native versions can belong to one IL version, including tiered and OSR code.
`GetCodeInfo4` accepts a native start address specifically because tiering can produce different native versions. [1][2]

A future implementation with tiering needs native-version attribution in addition to injected literals.
Changing a global generation cannot retrofit an identity into an already compiled body.

## JIT callbacks do not form a single serialized publication

The CLR permits more than one `JITCompilationStarted` and `JITCompilationFinished` pair for one method.
Reentrant class initialization can cause repeated compilation attempts.
The documented replacement contract requires `SetILFunctionBody` for both starts, although both can use the same IL block. [3]

Another thread can enter a function before the first thread receives `JITCompilationFinished`.
Thus, a finished callback is not a universal barrier before any probe execution. [3]
The controlled binding should reject service before admission completes.
It should not wait inside an early probe for a callback to finish.

For the first restricted implementation, a duplicate selected compilation should permanently invalidate experiment admission.
This is an explicit unsupported case, not a complete implementation of repeated compilation.
The invalid state must prevent subsequent callbacks from publishing a valid binding again.
Warmup must complete before the controller admits sampled work.

## Unload callbacks cannot pin code

The interface contract invalidates a `ModuleID` after `ModuleUnloadStarted` returns.
It similarly describes invalidation of a `FunctionID` after `FunctionUnloadStarted`. [4]
However, the pinned CoreCLR implementation does not issue `FunctionUnloadStarted`.
Its implementation asserts that the callback is no longer issued and returns. [5]

The implementation must therefore observe module unload for this ordinary managed-method path.
`COR_PRF_MONITOR_MODULE_LOADS` enables the module callbacks. [6]
Cached metadata and native ranges cannot remain usable after the selected module starts unloading.
An unload callback provides notification, not a profiler-controlled lease on CLR memory.

The lifetime record remains allocated for the process lifetime in the initial single-method prototype.
Unload changes an atomic, permanent invalidation flag.
No later module with a reused runtime ID can reactivate the record.
The record retains copied scalar identities and failure information, rather than dereferenceable CLR objects.

## Revocation and drain are different operations

`fIsSafeToBlock` does not promise that application threads can make progress while a callback waits.
The interface warns that blocking can make the runtime wait and distort results. [3]
The pinned implementation also states that JIT and `MethodDesc` code can hold locks during JIT callbacks. [7]

The unload callback only publishes permanent invalidation.
It does not acquire an application lock, invoke managed code, drain an owner, or join a thread.
The controller performs cancellation, journal capture, release, and owner shutdown outside the callback.

The existing `CooperativeCodeLifetime::tryRevoke` is a transport guard.
Its failed try-lock leaves the transport active.
The C ABI also restricts its revocation operation to the controller.
These properties do not implement unconditional CLR invalidation.
A separate profiler lifetime record must remain invalid even if transport revocation reports busy.

Invalidation can race with a probe already in service.
A check before service alone does not resolve this race.
Admission, poll service, and receipt acceptance need the same generation and permanent invalidation state.
The initial policy should reject the affected experiment when invalidation occurs.
Cleanup must still retain ownership until native termination and release complete.
This policy rejects uncertain measurements without claiming that a CLR unload waits for the profiler.

## Restricted compilation policy

The initial supported target should remain one known, non-generic method in a controlled startup workload.
The helper and target must not be inlined.
The profiler can reject selected-method inlining through `JITInlining`.
The runtime also provides `COR_PRF_DISABLE_INLINING`, which affects the entire process. [8]
That broader option changes optimization behavior and requires explicit overhead measurement.

The startup profiler can disable tiering with `COR_PRF_HIGH_DISABLE_TIERED_COMPILATION` through the high event mask.
This flag is immutable after initialization. [6]
The workload must also exclude precompiled execution before relying on startup IL rewriting.
The runtime provides `COR_PRF_DISABLE_ALL_NGEN_IMAGES` for profiler startup configuration. [6]
Existing runner environment settings remain useful, but do not replace a verified admission contract.

ReJIT requires separate callbacks and identities.
`ReJITCompilationStarted` and `ReJITCompilationFinished` include a `ReJITID`.
`GetReJITParameters` operates on a module and method token. [9]
The initial binding does not request ReJIT or claim ReJIT support.
Tiered compilation, OSR, collectible modules, and concurrent selected compilation need dedicated runtime cases before support expands.

## Next implementation boundary

The opt-in binding combines an injected literal with a profiler-owned lifetime record.
The managed thread-local callback connects the probe to the cooperative client.
That callback alone supplies neither CLR provenance nor code lifetime protection.

The live binding checks cover literal observations, pre-compilation queries, module unload, and rejection of a second selected module.
The native state checks cover invalid identities, failed compilation, duplicate compilation, and competing publication and invalidation.
Same-function repeated JIT callbacks still need a controlled runtime case.
An unload case must prove invalidation without waiting for owner progress inside the callback.
The bound cooperative checks exercise probe service and complete shutdown on Windows and Linux.
The [native range contract](bound-native-mapping.md) ties one copied method extent to the admitted binding.
Sample-driven operation still needs collection epochs and acceptance checks that preserve this identity.
These checks establish the restricted transport contract.
They do not establish prediction accuracy or exact attribution of countdown CPU.

See [the IL rewriting decision](coz-port-and-il-rewriting.md) and [probe overhead options](probe-overhead-options.md).

## Primary sources

1. [CoreCLR native and IL code versions](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/codeversion.cpp#L60-L110).
   The source also exposes OSR information through `NativeCodeVersion::GetOSRInfo`.
2. [GetCodeInfo4 and native version selection](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/proftoeeinterfaceimpl.cpp#L6551-L6575).
3. [JIT callback ordering and replacement contract](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/inc/corprof.idl#L1170-L1222).
4. [Module unload contract](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/inc/corprof.idl#L1076-L1093) and [function unload contract](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/inc/corprof.idl#L1161-L1167).
5. [FunctionUnloadStarted is no longer issued](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/eetoprofinterfaceimpl.cpp#L3097-L3101).
6. [Profiler event masks and immutable flags](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/inc/corprof.idl#L400-L690).
7. [JIT callback lock and thread-mode contracts](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/eetoprofinterfaceimpl.cpp#L3104-L3183).
8. [JITInlining contract](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/inc/corprof.idl#L1299-L1320).
9. [ReJIT callbacks and parameters](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/inc/corprof.idl#L2353-L2398).
