# CLR-bound cooperative payment

`BoundCooperativeClient` connects the internal cooperative client to an admitted CLR probe binding.
It requires the controlled two-site binding described in [injected probe identity](probe-identity.md).
The public serial API does not use this path.

## Admission and failure

`ClrProbeBinding.Parse` requires a nonzero generation, module, function, valid method-definition token, and exactly two sites.
The parsed identity remains immutable. Current CLR availability is a separate runtime check.

`ClrProbeLifetime.Execute` checks availability before and after the guarded operation.
Each check compares the full native generation, module, function, method token, and site count with the captured binding.
An operation failure or invalid lifetime permanently records `FirstFailure`.
Concurrent failures cannot replace the first failure.
A later valid observation cannot repair the experiment.

Submission, owner registration, injected polling, and receipt acceptance use this guard.
Native submission can succeed before the final availability check fails.
The native client retains that generation for cleanup even when the caller receives no submission result.
Receipt acceptance computes a new immutable adapter and ledger state.
If the final check fails, the guard does not return those values. The caller retains its old state.

## Injected arguments

`InjectedProbes.BindBound` receives generation and site arguments directly from `ProbeBound`.
It cannot coexist with a legacy callback on the same thread.
The parameterless helper rejects a bound callback. The bound helper rejects a legacy callback.
Disposal clears the callback on its owning thread.

The client rejects a foreign generation or an unknown site.
Entry site 1 returns Idle without servicing a lease.
Loop site 2 uses the registered worker identity and the admitted generation in the native journal.
The controller binds ledger acceptance to that exact cooperative site.

## Cleanup and completion

Cleanup does not require a valid CLR lifetime.
`BeginCleanup` closes normal operations and requests cancellation for active native generations.
An owner can use `PollForCleanup` only after that cancellation succeeds.
These manual cleanup polls declare the registered identity and produce cancellation evidence.
They do not establish injected probe origin or qualify payment evidence.

The controller retains the complete final journal before release.
The owner retires after release. The host joins the owner before closing the native session.
Cleanup does not repair a failed experiment or settle a rejected ledger reservation.

`Complete` requires successful close and a final lifetime check.
Invalidation after ledger settlement therefore still rejects experiment completion.
It does not roll back an already returned immutable ledger state.
A successful completion check observes the lifetime at the completion boundary. It does not pin code after that boundary.

## Verification limits

The live cases use a real second matching module to invalidate the CLR record.
They cover invalidation before submission, before polling, before receipt acceptance, and after settlement.
The paid case verifies two independent native generations from loop probe 2.
Each case runs at intervals 1, 64, 256, and 1024.

Pure checks cover failure during a guarded operation and rejection after an adapter computes settlement.
Live invalidation during a native wait and same-function recompilation remain unverified.
The mode still disables tiering, precompiled images, and process-wide inlining.
Sampled capacity, overhead, and physical prediction remain unverified.

See [the procedure](../How_Tos/verify-bound-cooperative.md) and [the existing receipt limits](cooperative-managed.md).
