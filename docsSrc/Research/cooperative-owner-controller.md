# Synchronous cooperative owner and controller

This explanation examines an opt-in mode that places the session controller on the selected owner thread.
It describes the design and its remaining integration requirements, not a measured performance improvement.
The native mode passes controlled checks on Windows and Linux. Managed workload integration remains pending.
The [ABI reference](../Reference/cooperative-interop.md#session-and-roles) describes the current native surface.
The local source baseline is `b8bd3fcf8bd5616ae4182f2ee4615adcd2c53559`.
The [retained pause evidence](../Explanations/cooperative-pause-cost.md) motivates examining controller dispatch cost.
It does not establish how much cost this candidate would remove.

## Decision supported by source

A narrow prototype is feasible in principle.
Keep one immutable controller thread for the entire session.
An explicit mode can permit that controller to register and poll its own selected slot.
The peer retains its separate owner thread.
All controller operations still execute on the session creator.

This proposal does not give every target owner controller authority.
That broader design would introduce concurrent access to slot generations, copied journals, and managed receipt trackers.
Those fields currently depend on controller serialization. [1][4]

The candidate removes controller dispatch from selected submission, receipt acceptance, and release.
With the selected owner as session creator, peer controller operations also execute directly on that thread.
The peer continues to poll independently.
The existing workload already routes controller operations through one helper with a direct-call branch. [5]

The default must retain separate owner and controller threads.
The prototype needs an explicit registration policy and an immutable session mode.
Removing the two existing role checks alone is insufficient. [1][2]

## Why local payment is plausible

Native Coz processes samples before it checks local delay against global delay.
Its Linux path stops sampling, performs the wait, credits measured elapsed time, and resumes sampling. [8]
JCoz also pays debt on the sampled thread after adding selected-location credit. [9]

These sources support local payment as an architectural precedent.
They do not establish CLR safety or CozNet's bounded receipt protocol.
Neither inspected payment path requires another controller thread to approve each local debt payment. [8][9]
The [upstream comparison](native-owner-scheduling.md) describes the differences in sampling and intervention limits.

## Contracts that the candidate must preserve

| Contract | Consequence for the candidate |
| --- | --- |
| One session controller | Submission, cancellation, copies, release, revocation, and close remain creator-thread operations. |
| One owner per slot | Only the registered owner can poll or retire that slot. |
| One outstanding generation | A new command cannot replace an unreleased command. |
| Retired journal before release | Native quiescence and a complete copied journal remain necessary. |
| Bound probe origin | Payment still requires the registered worker, code version, and admitted loop site. |
| Lifetime checks | Guarded managed operations check CLR identity before and after execution. |
| Distinct acceptance and release | A release failure cannot hide an already returned accepted ledger state. |
| Sampling exclusion | Selected sampling remains stopped throughout the existing excluded callback. |

The native API and target enforce the first four contracts. [1][2]
The bound managed client enforces probe origin and lifetime checks. [3]
The selected workload establishes receipt acceptance and sampling boundaries. [5]

The prototype must preserve the 2 ms debt and lease bounds.
Each requested child remains at most 50 µs.
The measured handler bound remains 5 ms, and the managed receipt bound remains 100 ms.
Wall time remains authoritative for payment.
No scheduling gap earns delay credit merely because the controller now shares an owner. [5][10]

## Reentrancy is the critical new boundary

The native API acquires `api_gate` before dispatching an operation.
Polling then acquires the code lifetime shared lock and calls timing hooks. [1][2]
The current role separation prevents the owner from passing controller checks.
The proposed mode removes that separation for one thread.

A timing hook could otherwise reenter submission, snapshot, cancellation, release, registration, revocation, or close.
Some paths would reacquire an already held mutex before reaching a target check.
A check inside `CooperativeTarget::submit` alone cannot protect the API entry point. [1][2]

This hook concern applies directly to the native `CooperativeTiming` abstraction and its injected test implementations.
The production ABI constructs `CooperativeSpinTiming` internally and exposes no caller-supplied timing callback. [1][11]
Its current clock and wait methods do not call managed code or reenter the transport. [11]
Thus, source inspection does not establish a production ABI callback path for this hazard.

The candidate therefore needs a same-thread operation guard before API lock acquisition.
Nested calls must fail without waiting, mutating ownership, or reading mutable lease storage.
The guard must cover session operations as well as slot operations.
Direct native target checks also need to reject controller operations during an owner poll.
The existing nested-poll check remains necessary. [2]

The pinned baseline's code lifetime object needs particular care.
Its public `tryRevoke` attempts an exclusive lock without an explicit same-thread polling check.
A direct test hook must not attempt that lock while the same thread holds its shared guard. [2]
The current native implementation resolves this boundary with a target-domain entry guard before locking.
The guard also rejects revocation of a different code lifetime during the same thread's transport call.
See [the current target contract](../Reference/cooperative-target.md#controller-operations).

## Receipt and cleanup ordering

The current selected path submits, polls, reads and accepts, validates the receipt interval, and then releases.
The managed interval ends after receipt acceptance returns and before release begins. [5]
A direct controller call can preserve those exact boundaries.
Combining acceptance and release would change failure behavior and could move the deadline endpoint.
That combination is outside this candidate.

Native ownership survives managed lifetime rejection.
Cleanup cancels pending work, permits owner cleanup polls, copies retired evidence, and releases each generation. [3][4]
The selected owner then retires its slot.
The peer retires on its own thread and is joined before session closure. [6]

A controller that also owns the selected slot cannot join itself before close.
Its retirement is the required boundary for its own slot.
The existing workload already closes before the selected caller returns. [6]
The managed close name and documentation must distinguish peer joining from selected-owner retirement.
The new mode must not imply that the native API proves a host thread join.

Scheduler capture also needs an explicit decision.
The current workload registers separate controller and selected scheduler roles. [7]
In the candidate, those roles identify one native thread.
The capture must represent this identity explicitly or reject that option combination before setup.
Duplicate roles must not appear as independent scheduler evidence.

## Checks required before a timing comparison

These are integration requirements. The [native evidence](../../reliability/features/native-signal/cooperative-owner-controller.md) identifies completed native checks.
Managed receipt, CLR invalidation, scheduler identity, and matched timing checks remain pending for the new mode.

| Boundary | Required observable result |
| --- | --- |
| Default mode | Creator-thread registration remains rejected. Existing separate-thread checks remain valid. |
| Mode parsing | Unknown values fail before session creation. The mode cannot change after creation. |
| Role matrix | Creator can own the selected slot. Peer owner and unrelated threads cannot perform controller operations. |
| Sequential lifecycle | Submit, bounded polls, full receipt copy, release, reuse, retirement, and close succeed on the admitted thread. |
| Early release | Release before a complete retired copy fails without losing the generation. |
| Reentrant hooks | Nested poll and controller/session operations fail promptly without deadlock or ledger changes. |
| Mixed ownership | Selected controller operations and peer polls preserve generation and journal identity under controlled overlap. |
| CLR invalidation | Failure before, during, or after receipt acceptance preserves the existing acceptance and cleanup rules. |
| Receipt deadline | The endpoint remains before release. An expired interval remains rejected. |
| Release failure | Accepted state remains available to the owner. Native generation ownership remains available for cleanup. |
| Partial setup | Registration or startup failure permits bounded cleanup without assuming that all owners exist. |
| Portability | Native contract checks and managed interop checks pass on Windows and Linux. |

The first comparison must retain both modes and every failed result.
It must preserve work, cadence, bounds, measurement endpoints, and observer settings.
Dispatch timing needs a truthful direct-operation representation or an explicit unsupported combination.
Zero dispatch calls must not imply zero controller cost.
The source still counts some controller CPU while selected sampling is enabled. [5][7]

Removing dispatch changes where controller CPU executes.
It can change sampled CPU attribution and peer scheduling even if native wait accounting remains identical.
While the controller pays its own child wait, it cannot process peer submission, receipt, or release operations.
The first managed comparison must inspect peer receipt latency as well as selected pause cost.
The new API and target guards also add thread-local accesses in both modes.
Their cost remains unmeasured. The retained phase capture predates these guards.
Thus, lower pause cost would not establish unbiased prediction or general application support.
The [sample eligibility explanation](cooperative-sample-eligibility.md) describes the remaining attribution problem.

## Sources

Local links below pin the reviewed commit so later changes do not alter this analysis.

1. [Native API roles, session state, locks, snapshot, and close](https://github.com/TheAngryByrd/CozNet/blob/b8bd3fcf8bd5616ae4182f2ee4615adcd2c53559/native/service/CooperativeApi.cpp#L18-L268).
2. [Target registration, polling, revocation, and generation ownership](https://github.com/TheAngryByrd/CozNet/blob/b8bd3fcf8bd5616ae4182f2ee4615adcd2c53559/native/service/CooperativeTarget.cpp#L7-L175).
3. [Bound identity, acceptance, and cleanup](https://github.com/TheAngryByrd/CozNet/blob/b8bd3fcf8bd5616ae4182f2ee4615adcd2c53559/tests/CozNet.LinuxPerf/BoundCooperativeClient.cs#L15-L96).
4. [Managed generation tracking, receipt copy, and release](https://github.com/TheAngryByrd/CozNet/blob/b8bd3fcf8bd5616ae4182f2ee4615adcd2c53559/tests/CozNet.LinuxPerf/NativeCooperativeClient.cs#L104-L248).
5. [Controller helper and selected payment sequence](https://github.com/TheAngryByrd/CozNet/blob/b8bd3fcf8bd5616ae4182f2ee4615adcd2c53559/tests/CozNet.LinuxPerf/BoundSampledPaymentWorkload.cs#L132-L318).
6. [Selected retirement, peer join, and controller close](https://github.com/TheAngryByrd/CozNet/blob/b8bd3fcf8bd5616ae4182f2ee4615adcd2c53559/tests/CozNet.LinuxPerf/BoundSampledPaymentWorkload.cs#L530-L577).
7. [Separate controller and selected scheduler registration](https://github.com/TheAngryByrd/CozNet/blob/b8bd3fcf8bd5616ae4182f2ee4615adcd2c53559/tests/CozNet.LinuxPerf/BoundSampledPaymentWorkload.cs#L368-L398).
8. [Native Coz local delay payment and sample ordering](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L678-L758).
9. [JCoz selected credit and local sleep](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L643-L666).
10. [Existing intervention limits and their meaning](native-owner-scheduling.md#meaning-of-the-unchanged-bounds).
11. [Production timing implementation](https://github.com/TheAngryByrd/CozNet/blob/b8bd3fcf8bd5616ae4182f2ee4615adcd2c53559/native/service/CooperativeSpinTiming.cpp#L12-L75).
