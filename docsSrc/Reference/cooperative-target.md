# Cooperative target transport

`CooperativeTarget` is an internal portable C++ transport for finite cooperative leases.
It uses `LeaseMailbox` and `TargetLease`.
The [cooperative C ABI](cooperative-interop.md) supplies production clock and spin-wait operations through `CooperativeTiming`.
The [managed client](cooperative-managed.md) supports manual owner polling. No injected .NET probe binding uses it yet.

## Registration and lifetime

`registerCurrentThread` binds a parsed `CooperativePollSite` to the current `std::thread::id`.
The caller supplies a distinct, nonempty controller thread identity, journal capacity, code-lifetime object, and timing adapter.
Capacity must be from 1 through 100,000 children.
The site's code-version identity must match the code-lifetime object.
Registration rejects revoked code.

The worker identity in the site is a logical label. It is not an OS thread identifier.
The host must assign labels consistently and route each registration to its own target object.
The object checks actual calling-thread identity independently of those labels.
The owner must retire the registration before thread exit. Thread-identity reuse after an abnormal exit remains unsupported.
The target and timing adapter must outlive every call. The host must join the owner before destroying target storage.

`CooperativeCodeLifetime` retains a code-version identity and a shared lifetime guard.
`tryRevoke` returns false while another thread holds that guard. A false result does not revoke code.
A successful call marks the scope inactive and excludes new submissions and registrations.
An already submitted request then cancels when the target polls.
Revocation is permanent and repeatable. The host must retain executable code until revocation succeeds.
Success excludes guarded transport work. It does not prove that no application frame still executes that code.
The host must also obey CLR active-frame and unload rules.
This hook does not automatically pin CLR code, intercept ReJIT, or verify probe origin.

## Controller operations

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
`wait(requested)` returns completed, interrupted, or failed status. Both timing hooks must be nonthrowing and finite.
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
