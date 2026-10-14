# Target-local timer delivery

This explanation evaluates Linux one-shot timers for finite peer leases.
The proposed transport removes native owner execution between children. It does not establish CLR signal safety or service capacity.

## Delivery mechanism

Linux `timer_create` supports `SIGEV_THREAD_ID` for a specific OS thread in the caller's process. [1][5]
The identifier is an OS thread ID, not `pthread_t`.
The prototype would create a `CLOCK_MONOTONIC` timer for each registered target.
It would use a reserved signal and a stable registration cookie in `sigev_value`.
Timer creation would occur outside the handler.

An `itimerspec` with a positive `it_value` and zero `it_interval` arms one expiration. [2]
A zero `it_value` disarms the timer. It does not request immediate delivery.
POSIX lists `timer_settime` as async-signal-safe. [4]
Thus, the handler can request another one-shot expiration through that API.
This API property does not make other handler operations safe or establish their CLR compatibility.

The target handler would validate the registration and active lease before intervention.
Each invocation would perform at most one child wait of at most 50,000 ns.
It would prepare the child result before it arms another expiration.
After rearming, it would publish final handler timing and the complete child journal before return.
The signal must remain blocked throughout the handler, without `SA_NODEFER`.
Thus, an early expiration cannot enter the next handler before publication.
Only an eligible, completed child with remaining budget would authorize another child.
Deferred, interrupted, cancelled, or invalid outcomes would stop continued service.
The owner would observe journals and manage the lease, without sending each subsequent child.

This changes the current transport ownership contract.
It needs a separate backend rather than concurrent handler calls into the existing owner-only request API.
The finite budget, generation identity, and settlement rules can remain portable.
Timer creation, signal delivery, and retirement belong in the Linux backend.

## Returning does not guarantee application execution

By default, the kernel blocks the delivered signal during its handler. [8]
A self-queued signal can remain pending until the handler returns and restores the signal mask.
The kernel checks pending signals on return toward user execution. [8]
It can deliver the next handler before the interrupted application makes useful progress.
Therefore, separate handler invocations do not prove an application execution interval between children.

A positive one-shot timer delay avoids intentional immediate queuing.
It still does not guarantee application execution.
The timer can expire while the target remains in the handler, its return path, or a scheduler delay.
An arbitrarily short timer would reproduce the pending-signal problem.
A larger interval reduces that risk but also reduces service capacity.
The prototype must retain interrupted addresses and timing evidence rather than infer resumed application execution from handler return.

## Pending delivery and retirement

The `timer_delete` contract leaves pending-signal treatment unspecified. [3]
Deletion alone cannot prove delivery quiescence.
Linux v6.6 deletes the timer and releases its preallocated signal record. [6]
If that record is already queued, `sigqueue_free` retains it until dequeue. [7]
These pinned sources explain why a stale timer signal can outlive deletion.
They do not identify the kernel binary on another host.

Safe retirement therefore needs an explicit protocol on the target thread.
The design must first prevent new leases, external arms, and handler rearming.
The target then blocks the reserved signal outside a handler.
After successful timer deletion, it drains the blocked signal with zero-timeout `sigtimedwait` until `EAGAIN`. [9]
Interrupted drain calls need continued draining. Other errors cannot count as quiescence.
The drain must retain and classify every returned record.
Only then can retirement release target state, restore its signal mask, and permit registration reuse.

This sequence requires exclusive ownership of the signal and exclusion of concurrent producers.
Without those conditions, an empty pending set is only a temporary observation.
The target must remain alive until retirement finishes.
Process-wide disposition restoration also requires all registered targets to finish retirement.
If deletion or draining fails, ownership must remain unresolved rather than freeing handler-visible storage.

The normal path must distinguish lease completion from target retirement.
A terminal journal does not prove that an armed or pending timer no longer exists.
Cancellation needs a terminal acknowledgment after delivery and rearming have been resolved.

## Reusing one timer across leases

Linux v6.6 source supports a possible reuse protocol. This source analysis does not prove behavior on the installed kernel.
The [native reuse fixture](../How_Tos/run-target-lease-reuse.md) implements this protocol for one bounded child per generation.
It does not establish the remaining concurrent cancellation and failure-recovery contracts.

Successful disarm waits for an active timer callback to finish, provided no other producer rearms the timer. [10][11]
That callback can already have queued a signal. Disarm therefore needs a complete drain before the next lease can use the timer.
A zero-timeout `rt_sigtimedwait` dequeues pending records without temporarily unblocking the signal. [12]
The signal must remain blocked throughout this operation.

Quiescence publication must follow disarm, drain, and every old lease access.
It must be the old handler's final shared-state action for that lease.
Otherwise, the old handler could disarm the new lease, drain its signal, or overwrite its journal.
The controller must also wait for all old journal readers before it reuses storage.
A generation-bearing mailbox must serialize submission, cancellation, and retirement. A boolean ready flag cannot prevent reuse of stale state.

The registration cookie remains constant across timer arms. It cannot identify a lease by itself.
The disarm and drain barrier establishes which generation owns the next delivery.
New configuration must become visible before the timer is armed.
An arm failure must preserve ownership and report a failed submission without waiting for a handler that cannot arrive.

Lease quiescence does not prove that the old handler has returned.
It cannot authorize freeing the registration, alternate stack, or process-wide signal disposition.
Target retirement still needs execution outside the handler.
Disarm can wait for a kernel callback, so its duration belongs in the measured handler interval.
Disarm and drain time must not count as intervention wait.

The Linux x64 backend needs the native kernel signal-mask size of eight bytes for `rt_sigtimedwait`. [12][13]
The glibc signal-mask type has a different size.
The glibc `sigtimedwait` wrapper also uses cancellation machinery and rewrites `SI_TKILL` provenance. [14]
Direct syscalls avoid those wrapper behaviors but still require correct ABI layouts and error handling.
These constraints belong in the Linux backend. Shared lease states and reader lifetime rules must remain portable.

## Signal provenance and journal identity

The kernel sets timer signal `si_code` to `SI_TIMER` and copies the configured `sigev_value`. [1][5]
The handler must verify the signal number, code, cookie, target registration, and active generation before it accesses lease state.
The cookie must remain valid until timer deletion and pending-signal draining complete.
A pointer into freed storage is not a valid identity check.
Timer identifiers and target identifiers can be reused, so neither alone proves the current generation.

One timer creation has one configured cookie.
Rearming does not attach a new child identity to the signal.
The target-local state must assign child sequence numbers and enforce one outstanding expiration.
A previous lease's pending expiration must not become the next lease's first child.
That requires explicit quiescence before lease reuse, or a timer with a fresh generation identity and safe retirement of the old timer.

Linux queues at most one signal record per timer and counts additional expirations as overruns. [1][7]
The one-shot design should not depend on overruns as separate children.
Unexpected overruns or foreign records require retained diagnostic evidence and rejection.
`SI_TIMER` is protocol provenance, not protection against a hostile participant inside the process.

## Preserved bounds and verification needs

| Bound | Required interpretation |
| --- | --- |
| 50 µs requested wait | Each handler pays at most one bounded child. |
| 5 ms measured handler | Measurement includes journal preparation and timer rearming before the final handler timestamp. |
| 100 ms acknowledgment | Every child remains subject to its first managed receipt bound. Native publication does not replace receipt. |
| 2 ms observed debt | Confirmed child waits can reduce active debt only after validated prefix accounting. Timer gaps do not count as waits. |

Handler duration measurement cannot include execution after its final timestamp.
The recorded boundary must remain explicit when it excludes epilogue and signal return.
Timer rearming failure must preserve the completed child and terminate continued service without losing lease ownership.
Cancellation can race with the last rearm. Retirement must resolve that race through blocked delivery and producer exclusion.

The [native delivery fixture](../How_Tos/run-target-lease-delivery.md) implements a single-use timer chain and pending-expiration retirement.
It uses direct Linux x64 timer syscalls. It now uses a [portable single-use lease core](../Reference/target-local-lease.md).
It does not implement a reusable lease transport.
The [retained native result](../../reliability/features/native-signal/service-submission/target-timer.md) contains ten bounded children and one separately drained pending signal.
It does not establish the remaining lifecycle contracts.
The next implementation must extend native lifecycle checks before it runs the CLR.
Required cases include pending expiration at deletion, cancellation during rearm, stale cookies, repeated registration, and foreign signals.
The checks must also cover a completed child followed by rearm failure and first managed receipt of a delayed journal.
Only after those contracts pass should a controlled CLR workload test eligibility, runtime transitions, and unchanged timing gates.
Passing those checks would not establish physical prediction accuracy or general signal safety.

See [owner scheduling](native-owner-scheduling.md) and [the finite service contract](../../lode/plans/native-debt-service.md).

## Controller cancellation while a handler executes

The `cancel-active-handler` fixture separates cancellation ownership from interruption of a physical wait.
The target first completes one measured child wait. It then publishes the active generation while the core remains executing.
The controller cancels that generation through the mailbox. The target observes cancellation before preparing the child.
This ordering checks that cancellation preserves completed work and prevents continuation of the lease.

The handshake adds scheduling dependence. Its elapsed time belongs to the handler interval, not to the measured intervention wait.
The fixture requires cancellation observation within 1 ms and retains the existing 5 ms handler bound.
It records the controller call interval because the handler can finish before the controller reads its post-call clock.
Each following normal generation checks that cancellation does not carry across reuse.
The case does not establish physical-wait interruption, concurrent rearm cancellation, managed receipt, or CLR safety.
See [the active cancellation procedure](../How_Tos/run-target-lease-reuse.md#check-cancellation-during-handler-execution).

## Recovering a rejected initial arm

The `reject-initial-arm` fixture uses `it_value.tv_nsec = 1000000000` and requires the direct syscall result `-EINVAL`.
Linux v6.6 checks the timespec before timer lookup and backend mutation. [15][16]
Microsoft's `linux-msft-wsl-5.15.153.1` release source has the same rejection order. [17]
That release name matches the retained run's kernel identity. Matching names do not verify the installed binary.
This specific rejection cannot create a new expiration. It also cannot remove an old expiration.
Thus recovery depends on prior target-side disarm and drain, with no intervening successful arm or other producer.
An inactive value from `timer_gettime` does not by itself prove that the pending signal queue is empty.

The controller must win the generation-specific mailbox recovery transition before changing the published lease.
If target execution wins instead, the controller cannot claim recovery ownership.
The failed lease retains zero children, zero measured wait, and the original transport error.
Core retirement and final writer access precede mailbox quiescence. Reader guards still prevent storage reuse.
Registration deletion and drain remain target-side operations outside the handler.

This reasoning does not apply to every failed timer call.
For example, Linux v6.6 can return `EFAULT` while copying `old_setting` after the timer has already changed. [15]
The fixture passes a null `old_setting` pointer and retains kernel identity with each run.
The inspected source path does not verify the installed kernel binary or establish general CLR safety.
See [the recovery procedure](../How_Tos/run-target-lease-reuse.md#check-rejected-initial-arms).

## Primary sources

[1] [Linux timer creation contract](https://man7.org/linux/man-pages/man2/timer_create.2.html).

[2] [Linux timer arming contract](https://man7.org/linux/man-pages/man2/timer_settime.2.html).

[3] [Linux timer deletion contract](https://man7.org/linux/man-pages/man2/timer_delete.2.html).

[4] [Async-signal-safe function list](https://man7.org/linux/man-pages/man7/signal-safety.7.html).

[5] [Linux v6.6 target validation and signal metadata](https://github.com/torvalds/linux/blob/v6.6/kernel/time/posix-timers.c#L377-L518).

[6] [Linux v6.6 timer deletion](https://github.com/torvalds/linux/blob/v6.6/kernel/time/posix-timers.c#L1006-L1034) and [timer release](https://github.com/torvalds/linux/blob/v6.6/kernel/time/posix-timers.c#L422-L435).

[7] [Linux v6.6 queued record lifetime and delivery](https://github.com/torvalds/linux/blob/v6.6/kernel/signal.c#L1953-L2035).

[8] [Linux signal masks and handler execution](https://man7.org/linux/man-pages/man7/signal.7.html).

[9] [Linux synchronous signal consumption](https://man7.org/linux/man-pages/man2/sigwaitinfo.2.html).

[10] [Linux v6.6 disarm and cancellation retry](https://github.com/torvalds/linux/blob/v6.6/kernel/time/posix-timers.c#L813-L936).

[11] [Linux v6.6 active callback cancellation](https://github.com/torvalds/linux/blob/v6.6/kernel/time/hrtimer.c#L1307-L1344).

[12] [Linux v6.6 zero-timeout dequeue and signal-mask size](https://github.com/torvalds/linux/blob/v6.6/kernel/signal.c#L3604-L3693).

[13] [Linux v6.6 x86 kernel signal set](https://github.com/torvalds/linux/blob/v6.6/arch/x86/include/asm/signal.h#L10-L26).

[14] [glibc-2.39 signal wait wrapper](https://github.com/bminor/glibc/blob/glibc-2.39/sysdeps/unix/sysv/linux/sigtimedwait.c).

[15] [Linux v6.6 initial timer validation and result copy](https://github.com/torvalds/linux/blob/v6.6/kernel/time/posix-timers.c#L900-L959).

[16] [Linux v6.6 timespec validation](https://github.com/torvalds/linux/blob/v6.6/include/linux/time64.h#L89-L101).

[17] [Microsoft WSL2 release rejection path](https://github.com/microsoft/WSL2-Linux-Kernel/blob/33cad9854e0b15d9ee50b33a3f457ff31a37b291/kernel/time/posix-timers.c#L907-L967) and [timespec validation](https://github.com/microsoft/WSL2-Linux-Kernel/blob/33cad9854e0b15d9ee50b33a3f457ff31a37b291/include/linux/time64.h#L89-L98).
