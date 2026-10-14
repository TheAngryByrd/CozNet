# Target-local timer delivery evidence

The native fixture delivers separate bounded children without observer execution between them.
It also drains a pending timer signal before target exit.
This is delivery evidence, not a sampled-service or CLR result.

Timer-only run: `artifacts/target-lease-delivery/20261014T182005715-b5524bbd`.
Command: `dotnet run --project build/build.fsproj -- -t LinuxTargetLeaseDelivery`.
The manifest retains source and binary hashes and the image ID. The final input check passes.

| Observation | Chain | Pending retirement |
| --- | ---: | ---: |
| Result | Pass | Pass |
| Child records | 10 | 0 |
| Requested budget | 500,000 ns | No delivered wait |
| Measured wait | 500,019 ns | 0 ns |
| Largest measured handler interval | 62,028 ns | No handler |
| Largest arm-to-terminal-observation interval | 20,097,825 ns | No child |
| Pending records drained after deletion | 0 | 1 |
| Timer deleted and handler restored | Yes | Yes |

The application progress counter increases between all nine consecutive child pairs in this chain.
This observation does not guarantee progress between future timer deliveries.
The observer sleeps for 20 ms after initial arming. It does not send subsequent children.
Timer gaps are not counted as wait. Measured overshoot remains in the total.
The final handler clock reading follows journal preparation and rearming.
It excludes final timestamp publication, epilogue, and signal return.

Two earlier runs remain under the same artifact root:

- `20261014T181832030-cc935b3a` predates explicit atomic publication of the initial arm timestamp.
- `20261014T181920939-4c7c0d89` predates moving journal preparation before the final handler clock reading.

Both earlier runs report pass, but neither represents the final implementation.
The later executions followed code corrections. They were not retries of unchanged workloads.

The fixture uses one registration and one finite chain per process.
It does not establish cancellation during rearm, stale-cookie rejection, registration reuse,
rearm-failure recovery, first managed receipt, CLR transitions, sampled capacity, or physical prediction accuracy.
Those checks remain prerequisites for integrating a target-local service backend.

See [the Linux timer contract](../../../../docsSrc/Research/target-local-timer-delivery.md)
and [the run procedure](../../../../docsSrc/How_Tos/run-target-lease-delivery.md).

## Portable lease integration

Run `artifacts/target-lease-delivery/20261014T183138470-d4c55999` uses `TargetLease` for admission,
wait preparation, child publication, cancellation before delivery, and retirement.
The target arms the initial timer while the signal is blocked. It publishes lease state before unblocking.
The initial interval is 1 ms. Later timer intervals remain 10,000 ns.

Both native cases pass. The chain records ten children and 500,084 ns of accepted measured wait.
Its largest measured handler interval is 52,368 ns. Its largest arm-to-terminal-observation interval is 20,078,023 ns.
The application progress counter increases across all nine consecutive child pairs.
Pending retirement records no child wait and drains one pending timer signal.
Both leases reach the retired phase with no lease error.

The runner also passes the portable lease contract suite on Linux.
The expanded deterministic suite and the five existing owner scenarios pass on both Windows and Linux.
The expanded checks add interrupted-wait evidence, failed initial arms, exact handler-bound acceptance,
and stale command tokens in reused storage. These additions do not change the executed timer or lease implementation.
The native timing case was not repeated for these test-only additions.

The portable model tests cancellation before delivery, before rearm, and after accepted rearm.
These are deterministic state transitions, not evidence of all native cancellation interleavings.
Native repeated registration, rearm-failure injection, immutable concurrent prefixes,
first managed receipt, CLR transitions, sampled capacity, and prediction accuracy remain incomplete.

## Receipt boundary correction

The earlier timer-only and portable-core runs timestamped terminal notification before the final record copy.
Their observer intervals do not establish a post-copy receipt bound.
The current fixture records `firstNativeReceipt` after a successful snapshot copy and preserves that timestamp on later reads.
This correction does not establish first managed receipt.

## Concurrent snapshot result

Run `artifacts/target-lease-delivery/20261014T184414277-f622220e` adds concurrent prefix copying.
All three native cases and the Linux portable contract suite pass.
The Windows portable contract suite and existing owner scenarios also pass.

| Case | Children | Measured wait | Maximum measured handler | Maximum first native receipt |
| --- | ---: | ---: | ---: | ---: |
| chain | 10 | 500,038 ns | 51,402 ns | 20,178,462 ns |
| snapshot-chain | 10 | 500,105 ns | 51,590 ns | 1,056,785 ns |
| pending-retirement | 0 | 0 ns | No child | No child |

The concurrent case records 6,927 snapshot reads, including the final copy after join.
All copied prefixes retain their accepted child records and measured sums.
Each `firstNativeReceipt` follows the first successful copy that includes that child.
The pending-retirement case drains one record. All three cases delete their timers and restore the handler.
The manifest records unchanged inputs for the execution.

These observations establish the tested native copy path. They do not establish live managed receipt or general CLR safety.

The portable .NET receipt tracker checks pass on Windows and in the Linux SDK container.
They cover immutable returned prefixes, first-receipt retention, malformed identities, late or failed clocks,
terminal mutation, backwards observations, and permanent rejection after the first failure.
These checks use synthetic native observations. No timer C ABI is connected to the tracker yet.

## Cancellation after rearm

Run `artifacts/target-lease-delivery/20261014T190205649-68b78e1f` includes `cancel-rearm`.
All four native cases and the portable lease contract suite pass. The manifest reports unchanged inputs.

The new case injects cancellation after a successful rearm in the first handler.
The handler observes the blocked pending expiration, disarms the timer, and drains one matching `SI_TIMER` record.
The next zero-timeout drain returns `EAGAIN`.

| Observation | Value |
| --- | ---: |
| Retained children | 1 |
| Accepted measured wait | 50,026 ns |
| Measured handler interval | 63,650 ns |
| Cancellation interval | 12,017 ns |
| First native receipt from child arm | 20,235,210 ns |
| Drained during cancellation | 1 |
| Drained during retirement | 0 |

The lease stops as cancelled and reaches retired state after target-side deletion.
The process restores the signal handler after target join.
Cancellation work remains inside handler timing and outside accepted intervention wait.
This controlled boundary does not establish concurrent cancellation, syscall failure recovery, repeated generations, or reader exclusion during reuse.
It does not establish live managed receipt or CLR safety.

## Repeated timer generations

Run `artifacts/target-lease-reuse/20261014T191340148-c9dd9e4f` connects the portable mailbox to one native timer and one lease storage slot.
The portable mailbox and lease suites pass. The native case passes all 16 generations with unchanged inputs.

| Observation | Value |
| --- | ---: |
| Completed generations | 16 |
| Children per generation | 1 |
| Requested wait per child | 50,000 ns |
| Measured wait range | 50,013–51,915 ns |
| Maximum measured handler interval | 54,067 ns |
| Maximum first native receipt interval | 1,060,248 ns |
| Reuse attempts rejected while pinned | 16 |
| Pending records at lease completion or retirement | 0 |

Each handler disarms and drains before it publishes quiescence.
The controller copies the journal under a reader guard, then releases the guard before beginning the next generation.
Mailbox quiescence is the handler's final shared-state action for that lease.
Every generation reconstructs the single-use lease in the same storage slot.
The final mailbox closes, the target deletes its timer, and the process restores its signal handler after join.

The fixture uses one 1 ms expiration per generation. It does not establish sampled capacity at that cadence.
It does not establish concurrent cancellation, injected syscall failure recovery, live managed receipt, CLR safety, or physical prediction accuracy.

This first reuse run did not require core retirement before reconstruction. It does not prove that lifecycle contract.
The pinned reuse attempts also do not isolate reader exclusion because mailbox quiescence can still be pending.
Portable mailbox checks separately verify reader exclusion after mailbox quiescence.

## Core retirement before reuse

Run `artifacts/target-lease-reuse/20261014T192121041-fe14353a` corrects the missing core retirement transition.
The target retires each core before its final mailbox quiescence publication.
The controller requires retired snapshot visibility before releasing its reader guard and beginning the next generation.
Final registration cleanup accepts an already-retired core but still deletes and drains the timer outside the handler.

All 16 generations report retired visibility 3. The portable contract suites and native case pass with unchanged inputs.
All 16 pinned reuse attempts are rejected. Retirement drains no pending signal.
The measured wait range is 50,013–68,108 ns. Overshoot remains accepted measured wait.
The maximum measured handler interval is 69,034 ns. The maximum first native receipt interval is 1,073,895 ns.
These observations establish the tested native core lifecycle, not live managed receipt or general CLR safety.

## Cancellation before delivery

Run `artifacts/target-lease-reuse/20261014T192616164-53f2fd39` uses `cancel-before-delivery` mode.
The controller cancels odd generations during configuration, before publication and timer arming.
The portable suites and all 16 native generations pass with unchanged inputs.

| Observation | Value |
| --- | ---: |
| Cancelled generations | 8 |
| Accepted cancellations | 8 |
| Total measured wait in cancelled generations | 0 ns |
| Following normal generations | 8 |
| Normal measured wait range | 50,016–434,378 ns |
| Maximum measured handler interval | 438,354 ns |
| Maximum first native receipt interval | 1,757,545 ns |

Cancelled generations report stop value 3. Normal generations report stop value 1.
All snapshots report retired visibility 3. The target deletes the timer. The process restores the handler after target join.
The large normal wait overshoot remains in accepted measured time. No threshold was relaxed.
The case demonstrates cancellation preservation before delivery and reset at the next generation.
It does not test cancellation during an active handler, failed-arm recovery, sampled capacity, live managed receipt, or physical prediction accuracy.
