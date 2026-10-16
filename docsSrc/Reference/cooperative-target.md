# Cooperative target transport

`CooperativeTarget` is an internal portable C++ transport for finite cooperative leases.
It uses `LeaseMailbox` and `TargetLease`.
The [cooperative C ABI](cooperative-interop.md) supplies production clock and spin-wait operations through `CooperativeTiming`.
The [managed client](cooperative-managed.md) supports manual owner polling. No injected .NET probe binding uses it yet.

## Registration and lifetime

`registerCurrentThread` binds a parsed `CooperativePollSite` to the current `std::thread::id`.
The caller supplies a nonempty controller thread identity, journal capacity, code-lifetime object, and timing adapter.
The default `CooperativeOwnerRole::separate` requires a distinct controller thread.
Explicit `CooperativeOwnerRole::controller` requires the registering thread to be the controller.
Unknown role values fail registration. Controller operations retain the same thread checks in both modes.
Capacity must be from 1 through 100,000 children.
The site's code-version identity must match the code-lifetime object.
Registration rejects revoked code.

The worker identity in the site is a logical label. It is not an OS thread identifier.
The host must assign labels consistently and route each registration to its own target object.
The object checks actual calling-thread identity independently of those labels.
The owner must retire the registration before thread exit. Thread-identity reuse after an abnormal exit remains unsupported.
The target and timing adapter must outlive every call. The host must join a separate owner before destroying target storage.
A controller-owned target requires retirement and no active call before destruction.

`CooperativeCodeLifetime` retains a code-version identity and a shared lifetime guard.
`tryRevoke` returns false while another thread holds that guard. A false result does not revoke code.
A successful call marks the scope inactive and excludes new submissions and registrations.
An already submitted request then cancels when the target polls.
Revocation is permanent and repeatable. The host must retain executable code until revocation succeeds.
Success excludes guarded transport work. It does not prove that no application frame still executes that code.
The host must also obey CLR active-frame and unload rules.
This hook does not automatically pin CLR code, intercept ReJIT, or verify probe origin.

## Controller operations

Registration, revocation, submission, polling, cancellation, journal copies, release, and retirement reject same-thread nested transport entry.
This applies across target objects before lock acquisition. Other threads can still use their permitted roles.
Atomic receipt-readiness and progress observations do not grant controller authority.

| Operation | Contract |
| --- | --- |
| `submit(command, budget)` | Requires active code and no unreleased request. Returns the new mailbox generation. |
| `cancel(generation)` | Requests cancellation for the matching outstanding generation. |
| `snapshot(generation, destination, required)` | Copies the published journal prefix under a reader guard. |
| `release(generation)` | Requires a copied retired journal and mailbox quiescence. Allows another submission. |

Only the controller thread can call these operations successfully.
Invalid command, budget, generation, role, or state returns no result or false.
An initial zero clock reading rejects submission before mailbox ownership starts.
The budget remains from 1 through 2,000,000 ns.
Release permits cleanup of a failed journal after copying it. It does not validate or settle that journal in the managed ledger.

## Target operations

`poll(observedSite)` requires the registering thread and rejects recursive polling.
An idle poll performs no clock read or wait.
An active poll claims the matching mailbox generation and holds the code guard through one child.
The child uses explicit site admission and the existing lease timing checks.
Mismatched sites defer without payment. Cancellation before admission also performs no wait.
Cancellation during a wait preserves valid measured payment.

| Result | Meaning |
| --- | --- |
| `idle` | No request is announced. |
| `serviced` | One child was processed, including a deferred or cancelled child. Read the journal for its outcome. |
| `rejected` | The caller is foreign, the target is retired, or polling is recursive. |
| `failed` | Admission, wait evidence, publication, or ownership failed. Retain the journal and failure. |

Terminal processing clears the announcement and execution token before publishing mailbox quiescence.
Old completion therefore cannot clear a newer generation's announcement.
The controller cannot reuse storage until it copies the retired journal and releases the request.
`retire` requires the owner thread and no unreleased request. It closes the mailbox permanently.
Shutdown requires the owner to keep polling until outstanding requests terminate and the controller releases them.
Cancellation alone does not complete ownership. There is no controller-only force-close path when the owner stops polling.

## Timing and support limits

`now()` returns nonzero monotonic nanoseconds from one clock domain for controller and owner calls.
The timing adapter must support concurrent clock reads.
`wait(requested)` returns completed, interrupted, or failed status. All timing hooks must be nonthrowing and finite.
They must not reenter lifetime revocation or other transport operations. Recursive `poll` is explicitly rejected.
The transport measures elapsed wait itself and keeps invalid evidence separate from accepted payment.
A zero clock reading immediately before a wait prevents that wait and records a failed child with zero payment.
The controller must copy the failed journal before it can release the request.
Each requested child remains at most 50,000 ns. The measured child-duration limit remains 5,000,000 ns.
That limit rejects an overlong returned operation. It cannot interrupt a stuck timing hook.
An external watchdog remains necessary for live experimental processes.

Registration allocates journal storage. Allocation and shared-mutex operations can throw C++ exceptions.
There is no ABI exception translation in this class.
The transport does not implement the 100 ms managed receipt check, 2 ms debt gate, or prediction estimator.
A .NET adapter must retain those checks when it connects this transport to sampled experiments.

The [contract evidence](../../reliability/features/native-signal/cooperative-target.md) uses real worker threads and controlled clocks on Windows and Linux.
It does not establish live intervention capacity, overhead, general CLR safety, or prediction accuracy.

## Optional child CPU observations

`registerCurrentThread(..., observeCpu = true)` allocates a separate diagnostic journal before polling starts.
The default is `false` and performs no CPU query.
`CooperativeTiming.cpu()` returns a raw observation with status, clock identity, nanoseconds, resolution, and native error.
The default hook reports unsupported capability.

The owner reads this hook before and after each admitted wait attempt.
These reads lie outside the wall interval used for payment, but inside the broader child interval.
Their cost can cause the existing handler bound to fail.
The service result still comes from the original wall clock and wait status.
A failed CPU query does not become a failed service or a zero CPU measurement.

Each record retains generation, command, child ordinal, worker, code version, site, and requested duration.
Failed wait attempts retain observations, including a failed pre-wait wall-clock check.
Deferred children, cancelled admissions, and failed admissions perform no CPU query.
The diagnostic count can therefore differ from the service child count.
Match records by identity and ordinal, not array position.

`cpuSnapshot(generation, destination)` requires the controller and mailbox quiescence.
An active generation returns no result.
A successful call returns the required count.
An insufficient destination receives no records.
This snapshot does not authorize release. The existing service journal must still be copied.
Release ends access to both journals. The next submission resets the diagnostic count.

CPU observations do not establish clock accuracy or explain a service failure by themselves.
The CPU interval includes observation work outside the measured wait.
No CPU delta replaces wall-time payment or relaxes a service bound.
