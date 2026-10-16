# Cooperative C ABI

`native/service/CooperativeApi.h` defines the internal `CozNet.Cooperative` shared-library interface.
It carries cooperative site identities. It does not reuse timer instruction-address records.
The public serial engine does not use this ABI.

## Session and roles

`coz_cooperative_open` accepts one nonzero code version, 1 through 64 target slots, and 1 through 100,000 journal children per slot.
One successful session is permitted per process. Closing it does not permit another session.
Unknown and closed handles are rejected before access to session storage.
C++ exceptions become status codes at the boundary.

The opening thread owns submission, cancellation, snapshots, release, revocation, clock reads, and close.
Each target registers, polls, and retires on its own thread.
Registration requires a nonzero worker identity and site identity. A slot cannot register twice.
The host must keep these threads alive and join separate owner threads before closing the session.
An owner that is also the controller must retire its slot before close.

`coz_cooperative_open_owner(code_version, targets, capacity, controller_slot, session)` reserves one slot for the opening thread.
The slot must be less than `targets`. Invalid slots fail before session creation.
Only the opening thread can register that slot. Other slots still require separate owner threads.
The reservation is immutable. All controller operations retain opening-thread affinity.
`coz_cooperative_open` retains the default separate-owner contract.
The [managed owner/controller mode](owner-controller.md) integrates this entry point.
Controlled CLR-bound execution passes on Windows and Linux. A sampled cost comparison remains pending.

An API lifetime guard excludes close and registration while other API calls execute.
Polls from different targets can execute together. This guard is separate from the code-revocation guard.
The API guard and code guard add unmeasured lock costs.
Calls that reach the shared entry boundary return `BUSY` on same-thread nesting, before either lock.
Argument checks can return `INVALID` before that boundary.
The API and target entry guards add thread-local accesses in both modes. Their cost has not been isolated.

## Operations

| Function suffix | Contract |
| --- | --- |
| `register` | Binds the current target thread and exact site identity to a slot. |
| `poll` | Processes at most one child. Returns the poll outcome separately from API status. |
| `submit` | Returns a nonzero generation for one finite lease. Command and budget must be nonzero. |
| `cancel` | Requests cancellation of the exact outstanding generation. |
| `snapshot` | Copies the journal and captures a native timestamp after the copy. |
| `release` | Requires a complete retired copy with a successful capture timestamp. |
| `retire` | Requires the registering thread and no unreleased request. |
| `revoke` | Attempts permanent revocation of the session's code version. Busy leaves it active. |
| `clock` | Returns nanoseconds in the same domain as child and snapshot timestamps. |
| `close` | Requires every registered target to be retired. |

Function names have the prefix `coz_cooperative_`.
The lease budget remains at most 2,000,000 ns. Each requested child remains at most 50,000 ns.
The target checks the measured child-duration limit of 5,000,000 ns.
A generation must fit in 32 bits. The ABI rejects larger values without truncation.
Cancellation and revocation still require owner polling to terminate pending work.

## Receipt readiness

`coz_cooperative_receipt_ready(session, slot, generation, ready)` observes an outstanding request without copying its journal.
Any thread can call it. The shared API lifetime guard excludes registration and close during the call.
The query reads atomic ownership and mailbox state. It does not access snapshot scratch storage.

`ready` must point to a `uint32_t`. The function clears it before checking the other arguments.
Zero, oversized, stale, and released generations return `INVALID`.
An invalid session, slot, or unregistered target also returns `INVALID`.
A valid outstanding generation returns `OK` with `0` while pending or executing, or `1` when its mailbox is quiescent.

Readiness is a hint. A failed or cancelled journal can also be ready.
The query does not authorize release, establish a receipt timestamp, or accept payment.
The controller must still copy the full journal and validate the receipt before accounting.

## Records and status codes

`CozCooperativeChild` is 88 bytes. Its eleven fields are unsigned 64-bit values.
They record ordinal, request, armed time, start, finish, measured wait, worker, code version, site, wait status, and validity.
The validity field has offset 80. Site fields retain the observed identity, including a mismatch that deferred the child.

`CozCooperativeSnapshot` is 88 bytes. `captured_ns` has offset 80.
It contains generation, command, budget, child count, measured payment, visibility, stop, error, transport error, possible delivery, and capture time.
The transport error is signed 64-bit. All other fields are unsigned 64-bit.
Child and snapshot state values follow the [target-local lease contract](target-local-lease.md).
Poll results are `idle = 0`, `serviced = 1`, `rejected = 2`, and `failed = 3`.

Status values are stable across platforms.

| Value | Meaning |
| --- | --- |
| 0 | Success |
| 1 | Invalid input |
| 2 | Wrong role |
| 3 | Busy |
| 4 | Insufficient buffer |
| 5 | Clock failure |
| 6 | Allocation failure |
| 7 | Other failure |

These values are not OS error numbers.
Submission clears its generation output on rejection. Clock reads clear their output on rejection.
An insufficient buffer receives a snapshot header and capture time but no child copies.
It does not authorize release. A later complete copy can authorize cleanup.
Any nonzero status rejects the receipt, even if the call filled output records before it failed.
Release of a failed journal does not accept its evidence into the managed ledger.

## Clock and spin wait

Linux uses `clock_gettime(CLOCK_MONOTONIC)`. Windows uses `QueryPerformanceCounter` converted to integer nanoseconds.
Windows conversion requires a positive frequency no greater than 1 GHz and rejects overflow.
Other POSIX platforms use the same clock hook but remain unverified.
These timestamps must not be mixed with managed `Stopwatch` ticks or a different native clock domain.

The timing adapter spins until the requested duration expires.
It rejects zero, requests above 50,000 ns, failed clocks, and clock regression.
A limit of 1,000,000 clock reads prevents an indefinitely frozen clock from keeping the spin loop active.
This iteration limit cannot bound OS descheduling or a clock call that does not return.
The transport measures the complete wait separately and retains its existing duration gate.

## Receipt boundary and limits

The snapshot capture time precedes return to the caller.
A managed client must read the native clock after return to obtain an upper bound for first managed receipt.
The [managed client](cooperative-managed.md) preserves the first bounds and rejects receipts beyond 100 ms.
Native capture alone does not establish managed receipt, sampled capacity, or physical prediction.

The internal managed client uses this ABI. No injected CLR probe binding uses it yet.
Code revocation excludes guarded transport work only. Actual CLR code lifetime and probe provenance remain host responsibilities.
See the [transport contract](cooperative-target.md) for shutdown and lifetime limits.

## Optional child CPU API

`coz_cooperative_register_cpu` has the same inputs and ownership rules as `register`.
It enables a separate diagnostic journal for that slot.
Existing registration and the two 88-byte record layouts remain unchanged.
The internal managed client enables this capability with `observeCpu = true`.

`coz_cooperative_cpu_snapshot(session, slot, generation, children, capacity, required)` copies diagnostics after mailbox quiescence.
It requires the controller thread and an outstanding generation registered with CPU capture.
An active generation returns `BUSY`. Disabled capture or a stale generation returns `INVALID`.
A short buffer returns `BUFFER`, sets `required`, and writes no child records.
A successful diagnostic copy does not authorize release.
Copy the original service snapshot before release, even when the service failed.

`CozCooperativeCpuChild` is 136 bytes.
Its first seven unsigned 64-bit fields are generation, command, ordinal, worker, code version, site, and requested duration.
Its two 40-byte readings are `before` and `after`.
Each reading contains unsigned 64-bit status, clock identity, nanoseconds, and resolution, followed by a signed 64-bit error.
Status values are `unsupported = 0`, `observed = 1`, and `failed = 2`.
Clock values are `none = 0` and `linux_thread = 1`.
Only `observed` readings contain CPU values. A zero value with another status is not a CPU measurement.

Linux reads `CLOCK_THREAD_CPUTIME_ID` and its reported resolution on the owner thread.
A failed native query retains its error number.
Invalid or overflowing clock conversion records `EOVERFLOW`.
Windows and other platforms currently report unsupported CPU capture.
This status does not disable the existing wall-clock wait.

The [target contract](cooperative-target.md#optional-child-cpu-observations) defines diagnostic retention and timing effects.
The internal client attaches `Cpu` to retired raw receipts and cleanup copies.
Active receipts and disabled capture use `null`.
A capture records slot, generation, native status, required count, raw children, and a separate capture failure.
The client reads the receipt upper clock before CPU capture.
Diagnostic copy time still contributes to the broader managed receipt interval.
Capture errors do not replace the original service result.
Selected-payment replay checks these records when `childCpuEnabled` is true.
It rejects missing records, changed identities, regressing CPU clocks, and CPU deltas beyond the enclosing child wall interval.
Explicit failed or unsupported readings remain unavailable measurements and receive separate summary counts.
These checks assume exact clock accounting. They do not establish calibrated accuracy.
