# Target-local ReJIT check

## Finding before this change

`CodeBinding.verify` would detect a ReJIT version of a bound method, because the runtime writes a new perf-map entry for it.
No check produced a ReJIT, so that detection was not shown. A ReJIT needs a CLR profiler that calls `ICorProfilerInfo4::RequestReJIT`.

## Decision

CozNet does not support ReJIT. A ReJIT of a bound method during a run must fail that run through `BindingError`. No forgiveness applies, and the bounds do not change.

The check uses a test profiler, `native/rejit/ReJitProbe.Linux.cpp`. It is not part of the CozNet library.
- It implements `DllGetClassObject`, `ICorProfilerCallback4`, and an `IClassFactory`, with the vtable order from `corprof.idl` in dotnet/runtime v10.0.0.
- At `Initialize`, it gets `ICorProfilerInfo4` and sets `COR_PRF_ENABLE_REJIT` and `COR_PRF_MONITOR_JIT_COMPILATION`. The second flag enables `ReJITCompilationFinished`. `GetReJITParameters` keeps the original IL. `JITCachedFunctionSearchStarted` and `JITInlining` keep the default answers, so the probe does not change which code runs.
- It exports `coz_rejit_request`, which takes a `FunctionID`, calls `GetFunctionInfo`, and then calls `RequestReJIT` for that method on a new native thread.
  A development run showed that `RequestReJIT` on a managed thread outside a callback returns `CORPROF_E_UNSUPPORTED_CALL_SEQUENCE`.
- It exports `coz_rejit_status`, which returns the count of finished ReJIT compilations, the count of ReJIT errors, and the last status.
- In CoreCLR, a `FunctionID` is the method descriptor, which `RuntimeMethodHandle.Value` returns.

The sample mode `rejit` binds `Work.selected` and `Work.peer`. The selected worker runs `Work.selected 1`, requests a ReJIT of `Work.selected`, and then runs `Work.selected 6000`.
The second call runs the ReJIT version, so its code is at a new address. The transition runner runs this mode with the test profiler loaded through `CORECLR_ENABLE_PROFILING`.

## Acceptance criteria

These criteria are fixed before the first run after this change. They add to criteria 1 to 33 and change none.

34. In the `rejit` check, `coz_rejit_request` returns 0, and the probe records at least one finished ReJIT compilation with status 0 and no ReJIT error.
35. After the run, the perf map holds more than one entry for `Work.selected`, and the run result has a non-empty `BindingError` and `Passed` false.
    The sample exits 0 only when criteria 34 and 35 hold. The runner reports this as the lifecycle value `rejitPassed`, and the transition check passes only when it is true.
36. After the commit, every source hash in each new manifest equals the SHA-256 of the committed blob.
37. Each suite runs once on the tree that the reviews check. No run is repeated to replace a failure.

See [target-local lifecycle](target-local-lifecycle.md) and [target-local status](target-local-status.md).
