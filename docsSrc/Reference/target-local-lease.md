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
`snapshot` supports concurrent readers with separate destination buffers.
Other accessors remain restricted to the owner or a synchronized caller.

## Transitions

| Operation | Required phase | Result |
| --- | --- | --- |
| `start` | Unarmed | Retains arm ownership, including an accepted arm with invalid timing. |
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
The backend must exclude producers, block delivery, disarm the timer, and drain pending signals before calling it.
Deleting the timer followed by a complete drain also establishes this proof.
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
| `cancel` | Configuring, ready, or active | Sets cancellation for that generation. |
| `cancelled` | Matching execution token | Reads the generation's cancellation flag. |
| `read` | Ready, active, or quiescent | Returns a reader guard that prevents storage reuse. |
| `quiesce` | Active, matching execution | Publishes the backend's delivery-quiescence proof. |
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
Before `quiesce`, the backend must exclude rearming, disarm delivery, drain pending events, and finish all old storage accesses.
The target must make no old storage access after successful quiescence publication.
Before `abandonUndelivered`, the backend must prove that no delivery is armed, pending, or executing.
Mailbox closure does not establish handler return or authorize registration destruction.
Atomic retry loops have no fixed latency bound. A backend must include their cost in its handler measurement.

The portable checks exercise cancellation, stale identities, reader exclusion, generation exhaustion, and read-versus-reuse races.
The [native reuse fixture](../How_Tos/run-target-lease-reuse.md) connects the mailbox to one Linux timer across 16 generations.
It requires a retired core snapshot before reconstructing the lease after mailbox quiescence and reader release.
No live .NET backend consumes the mailbox yet.

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
