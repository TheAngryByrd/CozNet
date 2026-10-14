# Target-local lease contract

`TargetLease` is an internal portable C++ state machine.
The Linux timer fixture uses it for one finite lease per process.
It is not yet a .NET backend or a public API.

## Plan and storage

`TargetLeasePlan::parse` requires nonzero generation and command values.
The budget is from 1 through 2,000,000 ns.
The eligible instruction range must be nonempty and must not wrap.

The lease uses caller-owned journal storage with 1 through 100,000 child slots.
It allocates no memory and calls no OS functions.
The caller must keep the lease and journal alive until retirement.
The lease cannot be copied, moved, or restarted.
A controller can initialize an unpublished lease. After publication, one target owns all state transitions.
An exclusive mailbox recovery token can transfer a never-executed lease to the controller after a definitively rejected initial arm.
`snapshot` supports concurrent readers with separate destination buffers.
Other accessors remain restricted to the owner or a synchronized caller.

## Transitions

| Operation | Required phase | Result |
| --- | --- | --- |
| `start` | Unarmed | Retains arm ownership, including an accepted arm with invalid timing. |
| `rejectInitialArm` | Waiting, no child admitted | Requires a positive transport error and seals an arm-failed terminal snapshot without wait credit. |
| `arrive` | Waiting | Checks generation and fresh instruction eligibility. Creates one admission. |
| `prepare` | Executing | Records wait evidence and decides whether another timer can be armed. |
| `publish` | Prepared | Validates final timing and publishes the child after timer rearming. |
| `cancelPending` | Waiting | Stops new child admission but retains pending delivery ownership. |
| `quiesced` | Terminal | Records the backend's delivery-quiescence proof. |
| `retire` | Terminal and quiesced | Ends ownership. No restart is available. |

Admission and completion values belong to a specific lease address, generation, command, and ordinal.
Stale or duplicate values cannot prepare or publish another child.
A foreign generation cannot consume a pending delivery.

## Accounting

Each admission requests at most 50,000 ns of the remaining fixed budget.
Measured overshoot remains in the total.
Cancellation and deferred eligibility produce no new wait.
Cancellation after a wait preserves that wait.
An interrupted wait preserves valid measured time and stops further service.
A rearm failure preserves the completed child and its valid wait.

Invalid wait or handler timing remains in the raw journal but does not increase accepted measured time.
The measured handler interval must not exceed 5,000,000 ns.
The final handler clock reading follows journal preparation and rearming.
It excludes the final timestamp store, terminal publication, epilogue, and signal return.

An accepted rearm remains owned even if publication then fails or observes cancellation.
Terminal accounting does not prove timer quiescence.
`quiesced` is a backend notification, not an OS check performed by this portable class.
`rejectInitialArm` retains possible delivery ownership until the backend provides that proof.
It rejects zero or negative errors, later-child waits, and executing, prepared, terminal, or retired phases without mutation.
Before `quiesced`, the backend must establish that no delivery remains.
The normal path excludes producers, blocks delivery, disarms the timer, and drains pending signals.
Deleting the timer followed by a complete drain also establishes this proof.
The rejected-initial-arm fixture uses prior target-side quiescence and a specific no-mutation rejection path.
It does not treat an inactive timer alone as proof that no signal is pending.
Core retirement ends the lease lifecycle. It does not free journal storage or retire the target registration.
Storage reuse also requires mailbox quiescence and release of every reader guard.
Registration retirement still requires target-side timer deletion and drain outside the handler.

## Concurrent snapshots

`snapshot(destination, required)` copies a published child prefix into caller-owned storage.
An insufficient or overlapping destination returns no snapshot and leaves the destination unchanged.
`required` reports the selected prefix length. The destination must not overlap any journal storage.
The lease and journal must outlive every reader. Retirement does not free them or exclude concurrent callers.

Publication requires lock-free 64-bit atomics. Unsupported targets fail compilation.
The writer publishes active child counts with release ordering. Readers acquire that count before copying.
A child never changes after publication. Terminal metadata is sealed once and published separately.
A reader that sees an earlier active state can return an earlier valid prefix.
It cannot include the unpublished terminal child through the active-prefix path.

Snapshot visibility distinguishes `active`, `terminal`, `quiesced`, and `retired`.
Active snapshots conservatively retain delivery ownership through `may_have_delivery`.
Terminal snapshots preserve whether another delivery was outstanding when accounting stopped.
Quiesced and retired snapshots report no pending delivery.
Accepted measured time equals the sum of valid child waits in the copied prefix.
Raw invalid children remain available in terminal snapshots.

The native fixture timestamps each first copied child prefix after the copy completes.
Later snapshots retain each child's first receipt timestamp.
These are native receipt bounds. They do not establish .NET receipt timing.

## Lease mailbox

`LeaseMailbox` is a portable ownership core. It does not create timers or contain journal storage.
One lock-free 64-bit atomic holds the generation, phase, cancellation flag, and reader count.

| Operation | Required state | Result |
| --- | --- | --- |
| `begin` | Idle or quiescent, without readers | Claims configuration storage and returns a submission token for the next generation. |
| `publish` | Configuring, matching submission | Publishes configuration and preserves cancellation. |
| `abandonConfiguration` | Configuring, matching submission | Ends configuration without delivery. |
| `claim` | Ready, matching generation | Grants one execution token to the target. |
| `recover` | Ready, matching submission | Grants the controller an exclusive recovery token instead of target execution. |
| `cancel` | Configuring, ready, or active | Sets cancellation for that generation. |
| `cancelled` | Matching execution token | Reads the generation's cancellation flag. |
| `read` | Ready, active, or quiescent | Returns a reader guard that prevents storage reuse. |
| `quiesce` | Active, matching execution | Publishes the backend's delivery-quiescence proof. |
| `quiesce` | Recovering, matching recovery | Publishes completed controller recovery and the backend's delivery-quiescence proof. |
| `abandonUndelivered` | Ready, matching submission | Resolves a submission for which the backend proves no delivery can occur. |
| `close` | Idle or quiescent, without readers | Permanently closes the mailbox. |

The optional constructor argument identifies the last completed generation. The default is zero.
Generation values increase through `UINT32_MAX`. `begin` rejects exhaustion instead of wrapping.
Cancellation does not carry into the next generation.
The mailbox rejects stale tokens, foreign tokens, and duplicate claims.

A reader guard is movable but not copyable. Its destruction releases exactly one reader.
The mailbox and protected storage must outlive all tokens and guards.
Readers must release every storage reference before destroying their guard.
The guard protects lifetime only. Journal reads still require `TargetLease::snapshot` or equivalent synchronization.

Before `publish`, the controller must finish configuration writes. It must publish before arming delivery.
Before `quiesce`, the backend must prove delivery quiescence and finish all old storage accesses.
The normal target path excludes rearming, disarms delivery, and drains pending events.
The target must make no old storage access after successful quiescence publication.
Before `abandonUndelivered`, the backend must prove that no delivery is armed, pending, or executing.
Mailbox closure does not establish handler return or authorize registration destruction.
Atomic retry loops have no fixed latency bound. A backend must include their cost in its handler measurement.

`recover` competes atomically with target `claim`. Exactly one can succeed for a ready generation.
While recovering, the mailbox rejects new execution, cancellation, readers, reuse, closure, and duplicate recovery.
Existing reader guards remain valid and still prevent reuse after recovery quiescence.
The controller must finish core failure publication, delivery cleanup, core retirement, and every old storage access before recovery quiescence.
`abandonUndelivered` does not grant a recovery token. It cannot authorize controller mutation of a published core.
The recovery token controls writer ownership. It does not prove that the operating system rejected or drained delivery.
Portable recovery checks pass on Windows and Linux.
The Linux `reject-initial-arm` fixture connects recovery to a rejected timer syscall.
Odd generations use `it_value.tv_nsec = 1000000000` and require the raw result `-EINVAL`.
The controller acquires recovery ownership before changing the lease.
It records `timer_gettime`, requires an inactive timer, seals the arm failure, and retires the core before mailbox quiescence.
The proof also requires prior target-side disarm and drain, with no intervening successful arm or other signal producer.
The no-mutation argument relies on reviewed Linux v6.6 and Microsoft WSL2 `5.15.153.1` release sources.
It does not verify installed binaries or cover every timer error. See [the source analysis](../Research/target-local-timer-delivery.md#recovering-a-rejected-initial-arm).
Each following even generation must complete normally.
Rejected generations have no children and no child receipt timestamp. `headerCaptured` records the separate header observation.

The portable checks exercise cancellation, stale identities, reader exclusion, generation exhaustion, and read-versus-reuse races.
The [native reuse fixture](../How_Tos/run-target-lease-reuse.md) connects the mailbox to one Linux timer across 16 generations.
It requires a retired core snapshot before reconstructing the lease after mailbox quiescence and reader release.
No live .NET backend consumes the mailbox yet.
The `cancel-before-delivery` fixture mode cancels odd generations before publication and arming.
It requires zero wait and cancelled stop for those generations, followed by normal completion in each even generation.
This mode does not exercise concurrent cancellation during handler execution.

The `cancel-active-handler` mode cancels odd generations after a measured child wait, while the lease remains in its executing phase.
The handler publishes the active generation. A valid run requires a cancellation observation within 1 ms.
This handshake occurs before `prepare`. Its time counts toward the 5 ms handler bound, but not toward credited wait.
The cancelled child retains its measured wait. Each following even generation must complete normally.
Credited wait must equal `handshakeStarted - waitStarted`. This excludes handshake time from intervention accounting.
The fixture records the controller call interval and the handler's cancellation observation.
The handler can finish before the controller reads its post-call clock. That clock need not precede child completion.
This mode does not test interruption of a physical wait, cancellation during rearm, or CLR execution.

## Managed receipt tracking

`TargetLeaseReceipts` is an internal .NET receipt tracker owned by one controller.
Its raw header and child records are data transfer values, not a published C ABI.
It validates lease identity, prefix length, immutable child records, visibility progression, and capture time.
It checks each new child's first receipt through the existing `ServiceReceiptTiming` parser.
The upper receipt time must use the same native nanosecond clock as child and snapshot timestamps.
A raw `Stopwatch` timestamp is not a valid substitute.

A later snapshot preserves the original receipt even after the 100 ms interval has elapsed.
Returned receipt prefixes do not grow when new observations arrive.
`LastRaw` contains a copy of the input before validation.
`FirstFailure` retains the first rejected capture. A failed tracker rejects all later observations.

A native clock failure can leave an invalid child's finish time at zero.
That child cannot receive a valid receipt timing value. The tracker rejects the capture and retains it in `LastRaw` and `FirstFailure`.
Raw retention does not imply acceptance into the received prefix.

This tracker proves receipt bounds for supplied observations. It does not validate full wait accounting,
instruction eligibility, CLR safety, or native lifetime. Those checks remain separate.
Portable deterministic checks cover the tracker. It is not yet connected to a live timer C ABI.

Live first managed receipt, selected sampling exclusion,
and reusable registrations remain integration requirements.
The current native fixture checks first native observer receipt only.

See [the timer design](../Research/target-local-timer-delivery.md)
and [the native fixture procedure](../How_Tos/run-target-lease-delivery.md).
