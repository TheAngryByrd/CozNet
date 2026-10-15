# Injected probe identity

`COZNET_PROBE_IDENTITY=bound` enables the internal startup binding for `CpuWorkload.Run`.
The target setting must be `COZNET_PROBE_TARGET=linux-perf`.
The mode requires registered IL mapping and accepts the existing probe intervals.
An absent identity setting or `legacy` preserves the original parameterless probe path.
Other identity values reject profiler initialization.

## Identity fields

The rewrite calls `ProbeBound(UInt64 generation, UInt32 site)` with embedded literals.
The single admitted instrumentation generation is 1.
Site 1 identifies entry. Later sites identify backward-branch probes in IL order.
The controlled workload has one backward branch, so its loop site is 2.
The lifetime record associates these literals with a module-load instance, `FunctionID`, and method token.
It does not reuse a record after invalidation.

These fields identify rewritten IL. They do not identify a native tier or OSR body.
They also do not authenticate callers that deliberately invoke the helper with forged arguments.

## Lifetime states

| Value | State | Meaning |
| --- | --- | --- |
| 0 | Empty | No selected compilation has started. |
| 1 | Compiling | The first selected compilation owns the record. |
| 2 | Rewritten | IL replacement and site registration completed. |
| 3 | Ready | The selected JIT compilation and native range capture succeeded. |
| 4 | Invalid identity | A required identity or site count was zero. |
| 5 | Duplicate compilation | Another selected compilation attempted to use the record. |
| 6 | Compilation failed | Rewriting, compilation, or native range capture failed. |
| 7 | Module unloaded | The selected module started unloading. |
| 8 | Shutdown | The profiler started shutdown. |

States 4 through 8 are permanent. The first invalidation reason wins.
An unrelated module unload leaves an established selected-module record unchanged.
An unload during the initial identity publication can invalidate conservatively before the module field becomes visible.
Invalidation uses lock-free atomics. The unload callback does not wait for managed work or native payment.

## Native exports

`CozGetProbeLifetimeState()` returns the state value, or -1 outside bound mode.
`CozGetProbeBinding(...)` returns the generation, module, function, method token, and site count only in Ready state.
It clears output fields when no binding is available.
`CozIsProbeBindingCurrent(generation, site)` rejects foreign generations, zero sites, out-of-range sites, and invalidated records.
`CozGetProbeCodeRange(generation, module, function, method, sites, address, size)` returns the single native method range for an exact binding.
It clears both outputs on rejection. The range uses an exclusive end address.
The profiler captures this range through `GetCodeInfo2` before it publishes Ready.
Failed queries, multiple extents, zero values, and address overflow reject publication.

A successful query is an observation. It does not pin code or prevent invalidation after the query returns.
The optional [bound cooperative client](bound-cooperative.md) consumes this record around payment admission and receipt acceptance.
It checks lifetime again at completion and preserves failed evidence for cleanup.

## Compilation policy

Bound mode disables tiered compilation, precompiled images, and inlining through the startup profiler event mask.
The inlining restriction affects the whole process and changes optimization behavior.
Overhead and physical prediction remain unmeasured for this mode.
The existing stack-sampling export returns `E_NOTIMPL` in bound mode.
The bound range is separate from the legacy sampler's IL map.
It identifies a whole-method extent. It does not identify injected instructions or source lines.

## Managed instruction map

`ClrBoundInstructionMap.Capture` copies the native range through a `ClrProbeLifetime` guard.
`ClrNativeRange.Parse` rejects zero values and overflow before the map can use the range.
`Resolve` checks lifetime before and after interval lookup, including unmatched addresses.
An observed invalidation permanently rejects later lookups.
The immutable range remains available as evidence after rejection.

This resolver does not admit samples by itself.
A collector must start after map capture and reject records from earlier collection epochs.
Sample acceptance and experiment completion also require lifetime checks.
The internal [bound sample collection](bound-samples.md) enforces these admission and lifetime checks with a fresh Linux task-clock source.
Its controlled Linux checks collect real selected-method samples without runtime perf-map output.
Sampled payment capacity and prediction remain unverified for this path.

The prototype supports one controlled method only.
It rejects another matching module or compilation instead of creating a replacement generation.
It does not support ReJIT, general method selection, or tier/OSR attribution.

See [the lifetime explanation](../Research/cooperative-clr-lifetime.md) and [the verification procedure](../How_Tos/verify-probe-identity.md).
