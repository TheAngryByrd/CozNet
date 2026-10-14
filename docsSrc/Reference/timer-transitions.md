# Timer transition workload

`LinuxTimerTransitions` runs four scenarios with and without forced GC.
The native timer and transition fixture require Linux x64.
`TimerWorkloadPlan` and its reach checks contain no platform calls.
`ISignalTransitionOperations` isolates native wait, release, thread identity, and address classification operations.

## Schedule

Each scenario uses 128 synthetic CPU-credit steps of 10,000 ns.
The 50-percent virtual speedup produces 5,000 ns of selected credit per step.
An all-deferred case accumulates 640,000 ns of unpaid debt.
Each accepted journal settles through `TimerAccountingAdapter`.
Deferral preserves unpaid debt.
The selected ledger thread has a separate arithmetic worker and native timer registration.
Before each peer reservation, selected timer leases pay any selected debt caused by peer overshoot.
After each completed peer lease, a peer catch-up check publishes measured overshoot.
The selected worker then pays that debt before the next credit step.
Paid scenarios must record at least one selected payment with positive confirmed wait.
Each credit step establishes a baseline in the current sampling epoch before adding its synthetic CPU increment.

| Scenario | Required reach | Payment rule |
| --- | --- | --- |
| `jit` | At least ten completed dynamic methods and an interrupted executable JIT image address | Every journal defers with zero wait |
| `native-wait` | Native fixture reports entry into its controlled wait | Every journal defers with zero wait |
| `managed-wait` | Target reports wait entry and reaches `WaitSleepJoin` | Every journal defers with zero wait |
| `transitions` | Completed arithmetic/native loops, paid selected code, and an interrupted native fixture address | Only the selected arithmetic range can receive paid delay |

Every scenario requires more than one timer generation.
Controlled waits must complete exactly once after release.
The controller releases blocked waits before joining the target.
Failed target joins retain native ownership and wait events.

## Records

`initialIntervalNanoseconds` records the selected initial timer interval.
`LinuxTimerTransitions` uses the default 1,000,000 ns.
`LinuxFastTimerTransitions` uses 10,000 ns with the same scenario plans, reach checks, and bounds.
The manifest also records the interval and the exact arguments for each case.

`completedOperations` counts completed arithmetic calls, dynamic methods, controlled waits, or transition loops, according to `scenario`.
`stableWaitReached` records the controlled-wait entry check.
`addresses` counts final journal addresses by classification.
`addressMap` records executable image ranges, permissions, device/inode identities, and SHA-256 hashes.
The selected arithmetic range comes from the retained initial and final perf maps.
`records` contains command budgets, progress counters, command intervals, and raw timer snapshots with first managed receipts.
`selectedPayments` contains separate selected-side budgets, confirmed waits, intervals, and raw snapshots.
`selectedPendingSnapshots` and `selectedPendingInterval` retain an unfinished selected payment.
`selectedRetired`, `selectedJoined`, and `selectedReleased` report selected-target cleanup.
The selected worker must join and release its final journal before the client closes.

The requested wait limit is 50 µs per child.
The handler interval limit is 5 ms.
The first managed receipt limit is 100 ms.
The lease budget limit is 2 ms.
Each forced collection call must finish within 250 ms.
At least one complete forced collection must fit inside a recorded command interval.

## Scope

Command intervals bracket native submission return and final receipt processing.
A contained collection does not prove overlap with an individual native wait or runtime suspension.
Stable wait entry does not prove that every timer interrupts a blocked syscall.
Address classification identifies executable images, not the exact runtime operation at each address.
Synthetic credit does not establish sampled capacity or prediction accuracy.
These checks do not establish general CLR safety.
Portable fault checks verify selected-worker lifecycle ordering and failure outcomes.
Native syscall fault injection and exhaustive runtime race coverage remain unverified.
Registration errors do not identify whether native ownership became active. The worker conservatively retains ownership if retirement also fails.
The [retained matrix](../../reliability/features/native-signal/timer-transitions.md) passes seven cases and fails the forced-GC transition case.
Peer wait overshoot can make synthetic selected credit insufficient.
That run used the earlier harness, which rejected selected-side wait requests.
The revised matrix passes all eight cases and records selected payments in both transition cases.
The arithmetic regressions also pass with and without forced GC.
These controlled results do not extend the public engine's supported workload model.
The [10 µs matrix](../../reliability/features/native-signal/timer-fast-transitions.md) also passes all eight cases.
Both transition cases include selected payment. Every forced-GC case reaches the existing command-overlap condition.
The matrix also passes all eight cases after the mailbox-retirement fix.
These results do not establish sampled forced-GC capacity.

See [the procedure](../How_Tos/run-timer-transitions.md) and [timer journal accounting](timer-accounting.md).
See [the worker lifecycle](timer-worker-lifetime.md) for cleanup states and fault-check scope.
