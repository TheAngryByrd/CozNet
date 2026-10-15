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
The host must keep these threads alive and must join the owners before closing the session.

An API lifetime guard excludes close and registration while other API calls execute.
Polls from different targets can execute together. This guard is separate from the code-revocation guard.
The API guard and code guard add unmeasured lock costs.

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
The future client must preserve the first bounds and reject receipts beyond 100 ms.
Native capture alone does not establish managed receipt, sampled capacity, or physical prediction.

No managed client or CLR probe binding uses this ABI yet.
Code revocation excludes guarded transport work only. Actual CLR code lifetime and probe provenance remain host responsibilities.
See the [transport contract](cooperative-target.md) for shutdown and lifetime limits.
