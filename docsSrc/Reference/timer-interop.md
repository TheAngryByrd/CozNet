# Target-local timer interop

This page describes the experimental `CozNet.Timer` C ABI and its managed receipt client.
The backend runs on Linux x64. Windows checks cover structure layout and portable managed contracts.
They do not exercise native timer delivery on Windows.
The public engine still supports only serial, synchronous CPU regions.

## Session and target ownership

`native/service/TimerApi.h` defines the portable C interface.
`TimerApi.Linux.cpp` contains the Linux implementation.
Status zero means success. Nonzero statuses are positive Linux errno values.

| Function | Calling thread | Contract |
| --- | --- | --- |
| `coz_timer_open` | Controller | Opens one session with a nonwrapping instruction range. |
| `coz_timer_configure_initial_interval` | Controller | Selects 10,000 or 1,000,000 ns before any target registers. |
| `coz_timer_register` | Target | Registers one target slot and creates its timer. |
| `coz_timer_submit` | Controller | Starts a bounded lease and returns its generation. |
| `coz_timer_cancel` | Controller | Requests cancellation of the matching outstanding generation. |
| `coz_timer_snapshot` | Controller | Copies a published journal into caller-owned storage. |
| `coz_timer_release` | Controller | Releases an outstanding result after a complete retired snapshot. |
| `coz_timer_retire` | Registered target | Blocks delivery, deletes the timer, drains pending signals, and retires retained state. |
| `coz_timer_clock` | Controller | Reads the native monotonic clock in nanoseconds. |
| `coz_timer_close` | Controller | Closes a session after registrations and outstanding results are released. |

The session accepts 1–64 targets and 1–100,000 journal entries per target.
Total capacity cannot exceed 1,000,000 entries.
A target cannot use the controller thread. Each slot permits one registration during the session.
Only one session can open per process. Opening after close returns `EALREADY`.
The Linux backend reserves `SIGRTMIN + 7` and requires its previous disposition to be `SIG_DFL`.

A command identifier must be nonzero. Its budget must be 1–2,000,000 ns.
Each child requests at most 50,000 ns of wait.
The first timer interval defaults to 1 ms. The controller can select 10 µs before target registration.
Any prior registration makes later interval configuration fail with `EBUSY`, including after that target retires.
Other interval values fail with `EINVAL`. A non-controller caller receives `EPERM`.
Continuation intervals remain 10 µs.
These intervals do not guarantee delivery latency.

## Snapshot layout

All timestamps use the native monotonic clock. Sizes and offsets are in bytes.

| Structure | Size | Fields in order |
| --- | --- | --- |
| `CozTimerChild` | 72 | `ordinal`, `requested`, `armed`, `started`, `finished`, `waited`, `ip`, `wait_status`, `valid` |
| `CozTimerSnapshot` | 96 | `generation`, `command`, `budget`, `children`, `measured`, `visibility`, `stop`, `error`, `transport_error`, `may_have_delivery`, `captured_ns`, `backend_error` |

Fields are unsigned 64-bit integers except `transport_error` and `backend_error`, which are signed 64-bit integers.
`valid` has offset 64. `backend_error` has offset 88.
State values follow the [target-local lease contract](target-local-lease.md).

An insufficient child buffer returns `ENOBUFS` with the header and no child copy.
That header includes a nonzero native capture timestamp taken after the core snapshot.
It does not authorize result release.
A complete successful copy sets `captured_ns` after copying the children.
Release requires retired visibility and no possible delivery.
Retired visibility also requires mailbox quiescence for the same generation.
If core retirement precedes mailbox quiescence, the API reports quiesced visibility until the mailbox completes its transition.
This prevents release from authorizing reuse while the previous execution still owns the mailbox.
A later incomplete snapshot removes the previous release authorization.

## Managed receipt bounds

`NativeTimerClient.Read` reads the same native clock immediately after the full native copy returns.
`TargetLeaseReceipts` retains each child's first successful managed receipt bound.
Later snapshots cannot replace that bound.
The bound is the first managed clock value minus the child's `armed` timestamp.
It must not exceed 100,000,000 ns.

A late first receipt or failed clock read poisons the tracker.
A later valid clock does not repair it.
Raw captures remain available after rejection.
Cleanup can copy and release retired native state without accepting rejected timing evidence.

## Failure and cleanup

A failed initial arm can return a nonzero generation.
That generation retains ownership until target-side cleanup proves quiescence.
An errno value alone does not prove that delivery is impossible.
Cleanup failures retain registration state and prevent session closure.

`CloseAfterThreadJoin` requires completed target cleanup and released results.
The managed handle finalizer does not free unresolved native state.
The controlled runner parks a target after failed retirement and uses an outer process watchdog.
It retains readiness-event storage while a target remains alive after a join timeout.
If thread start fails, cleanup does not attempt to join the unstarted thread.

## Verification scope

Native checks cover two targets, chained waits, cancellation, reuse, and retirement before delivery.
The retirement fixture blocks timer delivery on the target before the controller submits the final generation.
Its zero-child assertion does not depend on target scheduling within the first timer interval.
Managed checks use an ineligible instruction range. They produce a deferred child with zero wait.
They check first-receipt preservation, late-receipt rejection, and actual `EPERM` from a clock call on another thread.
They do not establish CLR intervention safety, sampled capacity, or physical prediction accuracy.

See [How to verify timer interop](../How_Tos/verify-timer-interop.md) for commands and evidence fields.
See [timer journal accounting](timer-accounting.md) for the ledger adapter and its separate verification scope.
