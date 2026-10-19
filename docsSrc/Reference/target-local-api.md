# Target-local API

The target-local profiler has a native C ABI and a portable managed replay.

## Native C ABI

`native/service/TargetLocalApi.h` declares the ABI. `native/service/TargetLocalApi.Linux.cpp` implements it for Linux x64.
The library name is `CozNet.TargetLocal` (`libCozNet.TargetLocal.so`). Every function returns 0 or an `errno` value.

| Function | Thread | Effect |
| --- | --- | --- |
| `coz_target_layout(which)` | Any | Returns the native size of the span (0), event (1), thread result (2), or experiment result (3). |
| `coz_target_open(speedup, period, selected, selected_count, eligible, eligible_count, experiment)` | Any | Parses the ledger plan and installs the signal handlers. Returns `EBUSY` unless `SIGRTMIN + 8` and `SIGIO` have the default disposition. |
| `coz_target_register(experiment, journal_capacity, thread)` | Worker | Creates the session and ledger account for the calling thread. Fails with `EBUSY` after any thread started. |
| `coz_target_start(thread)` | Same worker | Enables sampling and notification. |
| `coz_target_post(thread, payment_ip, token)` | Same worker, after start | Masks the notification signal, drains the ring, catches up at `payment_ip`, and returns a release token. |
| `coz_target_wait_begin(thread, payment_ip)` | Same worker, after start | Masks the signal, drains, catches up, and blocks the account. The signal stays masked. |
| `coz_target_wait_end(thread, payment_ip, token, reason)` | Same worker, while waiting | Wakes the account with reason 0 (dependency), 1 (timeout), 2 (cancelled), or 3 (spurious), then unmasks. |
| `coz_target_finish(thread, payment_ip, result)` | Same worker, not waiting | Closes the epoch, pays remaining debt at `payment_ip`, and retires the session. |
| `coz_target_events(thread, events, capacity, count)` | Any, after finish | Copies the thread journal. Returns `ENOBUFS` if `capacity` is too small. |
| `coz_target_release(thread)` | Any, after finish | Frees the thread. If handler quiescence failed, it returns `EBUSY` on every call and keeps the memory. The experiment then cannot close. |
| `coz_target_close(experiment, result)` | Any, after all releases | Reports the final global delay and restores the signal handlers. |

`selected` and `eligible` are arrays of `CozTargetSpan { begin, end }` with at most 16 entries each.
`payment_ip` must be inside an eligible span. A worker uses the start of its own work method.
`journal_capacity` must be larger than the settle reserve (45) and not more than 2^22.

`coz_target_finish` returns `EIO` if the epoch is incomplete, retirement fails, or the dispatch log dropped an entry.
The result has no field for a dropped log entry. If `complete` is 1 and `retire_error` is 0, a dropped entry caused the `EIO`. The result still holds all fields.

| `CozTargetThreadResult` field | Meaning |
| --- | --- |
| `complete`, `lost`, `failure` | Epoch completion, final loss, and epoch failure reason |
| `accepted`, `deliveries`, `dispatches` | Accepted samples, signals, and dispatches |
| `max_handler_ns`, `total_handler_ns` | Longest handler interval, and the sum of all handler intervals |
| `local`, `events` | Final local delay and journal event count |
| `started_ns`, `finished_ns` | `CLOCK_MONOTONIC` at start and after the closing payment |
| `final_debt` | Debt that remains after the closing payment |
| `retire_error`, `quiescent` | Retirement error and handler quiescence |

`CozTargetExperimentResult` holds the global delay, the sequence count, foreign and `SIGIO` signal counts, the first ledger failure, and the restore error.

## Managed replay

`src/CozNet/TargetLocalReplay.fs` is portable F#.

`TargetReplayPlan.tryCreate speedup period selected eligible` parses the same plan as the native library.
Spans are `(begin, end)` tuples with half-open ranges.

`TargetLocalReplay.replay plan workers events` returns `Ok TargetReplaySummary` or `Error message`.
It requires these conditions:

- The sequences of all events are exactly 1 through the event count.
- Sorted by value, each publish observes the previous publish, and the first publish observes 0.
- Each event keeps the prior local delay that the replay computes for its worker.
- Each observed global delay is 0 or a published value.
- Each sample earns the selected credit if its address is selected, and 0 otherwise.
- Each child requests 1 to 50,000 ns, at an eligible address, and not more than its observed debt. Its wait status is success.
- The journal holds no `Failure` event. A ledger failure rejects the replay.
- Each deferral follows positive debt and records that debt. Reason 0 needs an ineligible address, and reason 1 needs an eligible address.

The replay types are public so that the sample can use them. They are experimental, and their fields can change.

`TargetLocalReplay.effectiveDuration elapsed summary` returns `elapsed - GlobalDelay`, or an error if that value is not positive.

## Managed profiler

`src/CozNet/TargetLocalLinux.fs` binds the C ABI for managed programs and holds the Linux readers for run-queue delay, thread CPU time, and thread usage. `src/CozNet/TargetLocalProfiler.fs` holds the perf-map binding, `TargetBarrier`, and the run loop. The perf-map parser and the replay are portable, but `run` requires Linux x64.

`PerfMap.tryRange lines symbol` returns `Ok (begin, end)` for exactly one `[Optimized]` perf-map entry that contains `symbol`.
It ignores entries with a zero size, an overflow, or a field that is not hexadecimal. Zero or several matches return `Error`.
`PerfMap.currentProcess ()` returns `/tmp/perf-<pid>.map`.

`TargetLocalProfiler.run speedup period selected eligible journalCapacity workers monitor` runs one profile:

1. It raises `PlatformNotSupportedException` unless the process is Linux x64. It then raises `ArgumentException` unless there are 1 to 64 workers. It raises `InvalidOperationException` if a managed layout differs from `coz_target_layout`, or if the library has no `coz_target_layout` export.
   It raises `ArgumentException` if a `PaymentIp` is outside every eligible span.
2. It opens the experiment and starts one thread for each `TargetWorker { Work; PaymentIp }`.
3. Each thread registers, waits at a barrier, sets its thread state, starts sampling, waits again, runs `Work`, finishes, copies its journal, and releases.
   The thread state is set before sampling starts, so its first-call compilation is not in the sampled interval.
4. The calling thread runs `monitor` with a `TargetRunState`. `Working` is true while any worker runs `Work`. `Running` is true while any thread lives.
5. It joins the threads, closes the experiment, and replays every journal with `TargetLocalReplay.replay`.
   If `monitor` throws, a thread cannot start, or the caller is interrupted during the join, it releases the barrier, joins the started threads, and closes the experiment before it raises `AggregateException`.

`TargetThreadOutcome.RunDelayNs` is the change in kernel run-queue delay (`/proc/thread-self/schedstat`, field 2) from the release of the start barrier to the finish, so it includes the wake latency at that barrier. It is -1 when the file cannot be read.
Each worker opens the file once at registration and reads it once there, so the read code is compiled outside the timed work. Each later read is a `pread` at offset 0.
`TargetThreadOutcome.WaitRunDelayNs` holds the change in run-queue delay across each completed `TargetBarrier` wait of that worker, in order. A failed read records -1.
The interval starts before `WaitBegin` and ends after `WaitEnd`, so it includes the time from the release to the wake.
A worker keeps at most 1024 values. `WaitRunDelaysDropped` counts each later wait.
`TargetThreadOutcome.StartRunDelayNs` is the change in run-queue delay from the read before the start barrier to a read after that barrier returns.
The second read comes before `StartedTicks`, so it is outside the measured span. It is -1 when a read fails.
The difference `RunDelayNs - StartRunDelayNs - sum(WaitRunDelayNs)` is an upper bound on the run-queue delay during `Work` outside the waits.
`TargetThreadOutcome.StartTrace` holds three `TargetStartPoint` values: after the native start, before the start barrier, and after the start barrier returns.
Each point holds `Ticks` from `Stopwatch`, `CpuTimeNs` from `CLOCK_THREAD_CPUTIME_ID`, `RunDelayNs` from `/proc/thread-self/schedstat`, and `MinorFaults`, `MajorFaults`, `VoluntarySwitches`, and `InvoluntarySwitches` from `getrusage(RUSAGE_THREAD)`.
A failed read records -1. A thread that cannot register has an empty trace. All three points come before `StartedTicks`.

The qualification runners hash each text source after they replace CRLF with LF, and each manifest records `sourceHash`. A hash then equals the SHA-256 of the committed blob.
`TargetLedgerEvent.Time` is the native event time. For a sample, it is the perf sample time.

`TargetProfileResult` holds `ElapsedNs`, `GlobalNs`, `EffectiveNs`, the experiment result, each thread outcome, all events, the replay result, and `Passed`.
`Passed` is true only when every thread completes with zero loss and quiescence, every handler interval is below 5 ms, no ledger failure or foreign signal occurs,
and the replay is valid with the native global delay and event count. An exception in `Work` makes the run fail.
See [the how-to](../How_Tos/profile-fork-join-program.md).

## Causal profile

`TargetCausalDesign.tryCreate binding period eligible candidates speedups blocks seed rotation progress` parses the experiment design.
The seed fixes one base order of the experiments. Block b runs that order rotated left by `rotation + b - 1` places. `rotation` must be from 0 to the experiment count of a block minus 1.
`progress` is `EndToEnd`, `Throughput durationMs`, or `Latency durationMs`, with a duration from 1 to 60000 ms. It returns `Ok TargetCausalDesign` or `Error` with a list of `TargetCausalDesignError` values:
`NoCandidates`, `DuplicateCandidate`, `UnboundCandidate`, `IneligibleCandidate`, `UnboundEligible`, `NoSpeedups`, `DuplicateSpeedup`, `SpeedupOutOfRange`, `NoBlocks`, `InvalidRotation`, `InvalidDuration`, and `PlanRejected`.
`TargetCausalDesign.describe` gives the message for one error. A design has a private representation, and it keeps copies of its input arrays.

`TargetCausalProfile.run design journalCapacity workers` runs the design in one process. `workers` takes a new `TargetProgressCounter` and returns new workers for each experiment.
For `Throughput`, workers call `Visit` once for each unit of work and stop when `Stopped` is true. The driver calls `Stop` when the duration has passed after a worker starts to work, and also when its monitor fails.
For `Latency`, workers call `Begin` at the start of each transaction and `End ticket` at its end, with the ticket that `Begin` returned. `End` also counts a visit. Do not call `Visit` in a `Latency` experiment. After `Stopped` is true, end every transaction that has begun, because a transaction that never ends is left out of `LatencyNs`. An experiment without latency gives no prediction. The driver stops a `Latency` experiment as it stops a `Throughput` experiment.
A `Latency` prediction is `1 - (VL / V) / (VL0 / V0)`, where VL is `VirtualLatencyNs` and V is `Visits`. Pinned Coz applies Little's law to the transactions in flight at the end of an experiment instead.
`VirtualLatencyNs` sums, for each transaction that ends, its wall latency minus the global delay added from its begin to its end. The profile rebuilds the global delay from the publish events of the journal: at a time t, it is the largest value of the publishes at or before t.
A transaction whose virtual latency would be negative counts as 0, and `NegativeTransactions` counts it. A thread pays its debt at its later settlements, and the 2 ms bound limits the size of the debt, not the time until it is paid. The ledger stamps a publish after it raises the global delay. So delay can move between neighboring transactions. The transaction times use `Stopwatch` and the journal uses `CLOCK_MONOTONIC`, which is the same clock on Linux.
A watcher thread also calls `Stop` 1 s plus ten times the duration after the experiment starts, so a run that never reaches its monitor still ends.
An experiment that the watcher stops does not pass, and its error names the safety stop.
Each experiment calls `TargetLocalProfiler.runBound` with one candidate as the selected symbol.

1. Only `tryCreate` makes a design, so `run` receives only valid candidates, speedups, blocks, and a period that the replay plan accepts.
2. Before the first block, it runs one warm-up experiment at speedup 0 with the first candidate. The result keeps it as `Warmup`, and no prediction uses it.
   Each block runs one baseline at speedup 0 and one experiment for each candidate and speedup, in a random order from `seed`.
   Before each experiment, it runs a full blocking collection, outside the measured span. `workers` must return workers with fresh state for each call.
3. Each `TargetCausalExperiment` holds `Block`, `Order`, `Candidate`, `Speedup`, `ElapsedNs`, `GlobalNs`, `EffectiveNs`, `Passed`, and `Error`.
   An experiment that raises an exception keeps the exception type and message. An experiment that does not pass keeps each cause that rejected it: binding, replay, ledger, close, signal, and thread errors, including handler intervals of 5 ms or more. `Passed` is then false.
4. Each `TargetCausalPoint` holds one prediction for each block, `1 - E / E0`, where E0 is the baseline of the same block.
   With visits V, the period of an experiment is `EffectiveNs / V`, and the prediction is `1 - P / P0`. For `EndToEnd`, V is 1.
   A prediction is `None` when the experiment or its baseline did not pass or has no visits. `ValidBlocks` counts the other blocks, and `MedianPrediction` is their median.

`TargetCausalProfile.run` requires Linux x64 through `runBound`. The design and the prediction code are portable.
Each `TargetCausalExperiment` also holds `Visits`, and `Threads` with one `TargetCausalThread` for each worker.
A `TargetCausalThread` holds `CpuNs`, `WallNs`, `HandlerNs`, `PaymentNs` (the sum of measured child payments, including the closing payment), `SkipNs` (the sum of wake skips), `Samples`, `Wakes`, and `RunDelayNs`.
`CpuNs` stops when the native finish disables counting. `WallNs` runs from the work start to the end of the native finish. A throughput experiment counts every visit from the first work start to the last finish.
`Arrivals` counts `Begin` calls and `LatencyNs` sums the wall time from each `Begin` to its `End`. `TargetProgressCounter.Transactions` returns the begin and end time of each transaction that ends, in nanoseconds. `EndToEnd` records 0 for both. `Throughput` records the counter values, which stay 0 unless workers call `Begin`.
`Failures` holds one `TargetCausalFailure` for each `Failure` event of the journal: `Worker`, `Detail`, `DebtNs` (the event amount), `TimeNs` (from the first journal event with a time), and `GapNs` and `PreviousKind` (from the previous event of the same worker with a time, in sequence order).
The list follows the journal sequence. `GapNs` can be negative, because a buffered sample can have an earlier time than the event before it.
The window of a failure holds the timed journal events of all workers that come before it in sequence and no more than 10 ms before it in time. `ContextEvents` counts them.
`Context` holds the 64 latest events of the window. Each `TargetCausalContextEvent` has `Worker`, `Kind`, `ObservedGlobal`, `LocalBefore`, `Amount`, `Requested`, `Detail`, and `OffsetNs` from the failure.
An event of another worker can have a positive `OffsetNs`, because the ledger takes its time before its sequence.
`Workers` holds one `TargetCausalWorkerContext` for each worker over the whole window: `Events`, `Children`, `FailedChildren` (children whose wait failed), `MeasuredNs` and `RequestedNs` of all children, `MaxOvershootNs` and `MaxOvershootOffsetNs` of the completed child with the largest measured minus requested time, `Publishes`, and `LastPublishLocal`, `LastPublishObserved`, and `LastPublishOffsetNs`. A missing child or publish gives null values.
Some native failures carry no time. Their `TimeNs` and `GapNs` are null, and `PreviousKind` is empty. A failure without a previous timed event of its worker also has a null `GapNs`. A failure without a time has an empty window.
`Collections` and `GcPauseNs` are the change of `GC.CollectionCount(0)` and of `GC.GetTotalPauseDuration()` across the experiment, after its forced collection. They count all threads of the process. An experiment that raises an exception records -1 for both.

## Code binding

`CodeBinding` checks that the selected and eligible code stays at one native range during a run.

| Function | Result |
| --- | --- |
| `CodeBinding.configurationErrors ()` | A list of errors. It is empty only when tiered compilation is off, ReadyToRun is off, and the perf-map setting is `1` or `3`. |
| `CodeBinding.tryCapture lines symbols` | `Ok binding` when each symbol has exactly one perf-map entry, and that entry is `[Optimized]`. |
| `CodeBinding.verify lines binding` | `Ok ()` when a new capture gives the same ranges. |
| `binding.Range symbol` | The bound `(begin, end)` range. |

Tiering is off when `DOTNET_TieredCompilation` or `COMPlus_TieredCompilation` is `0`.
When neither is set, the `System.Runtime.TieredCompilation` switch must be false. An environment value overrides the switch.
A `[QuickJitted]`, `[OptimizedTier1]`, `[OptimizedTier1OSR]`, or second entry for a symbol rejects the capture.

`TargetLocalProfiler.runBound binding speedup period selectedSymbols eligibleSymbols journalCapacity workers monitor` is the checked entry point.
It raises `InvalidOperationException` when the configuration or the binding is not valid before the run.
After the run it verifies the binding again. A change sets `BindingError` and makes `Passed` false.
`TargetLocalProfiler.run` takes ranges and does not check the binding.

## Shutdown

`Environment.Exit` during a profile is a supported exit. The process exits, and the kernel releases the event, mapping, and signal state.
No CozNet cleanup runs, and `run` returns no result.

## Barrier

`TargetBarrier(participants)` is a reusable barrier. Outside a target-local profile, or on another platform, it is a plain barrier.
Inside `TargetLocalProfiler.run`, each worker uses the wait protocol:

1. The last worker to arrive calls `coz_target_post` and stores the token in the phase. It then releases the phase.
2. Each other worker calls `coz_target_wait_begin`, waits for the phase, and calls `coz_target_wait_end` with the phase token.

`SignalAndWait()` waits without a limit. `SignalAndWait(timeout, cancellation)` returns false when a worker that must wait times out or is cancelled.
The last arriver never waits, so it returns true even with a cancelled token.
Inside a profile, a worker that fails to register or start, a `Work` exception, a barrier error, or a monitor exception cancels every barrier wait in the run.
A worker that times out or is cancelled still counts as arrived for its phase, and its wake skips nothing.
More arrivals than participants in one phase raise `InvalidOperationException`. A failed native call fails the run with `barrier <error>`.
`Phase` returns the number of completed phases.

The replay checks every `block`, `wake`, and `post` event. A dependency wake must name the token of an earlier `post`.
No `publish`, `child`, or `deferred` event can belong to a blocked worker, and no worker can stay blocked at the end.
`TargetReplaySummary` reports `Blocks`, `DependencyWakes`, and `SkippedDelay`.

## Queue

`TargetQueue<'T>(capacity)` is a bounded FIFO queue for profiled workers. It raises `ArgumentOutOfRangeException` when `capacity` is below 1.
`TargetQueue<'T>(capacity, spin)` spins up to `spin`, from 0 to 1 ms, inside a `Dequeue` wait before it blocks, as `TargetLock(spin)` does. `Enqueue` does not spin. The spin and the block share one timeout budget. `SpunDequeues` counts the dequeues that take their item in a spin.

| Member | Result |
| --- | --- |
| `Enqueue(item, timeout, cancellation)` | `true` after it adds the item. `false` after a timeout or a cancellation, without the item. |
| `Dequeue(timeout, cancellation)` | `ValueSome item`, or `ValueNone` after a timeout or a cancellation. |
| `Enqueue(item)` and `Dequeue()` | Wait without a limit. They raise `OperationCanceledException` when the run aborts the wait. |
| `Capacity` and `Count` | The capacity and the number of items now in the queue. |

In a profiled worker, `Enqueue` posts a token after it takes a space, and stores the token with the item.
A `Dequeue` that waits ends its wait with the token of the item that it takes. Each dequeue then posts and stores the token with the space that it frees.
An `Enqueue` that waits takes a stored release token, and an `Enqueue` that does not wait takes one of the first `capacity` spaces first. So a waiting `Enqueue` ends its wait with the token of a release before its wake.
If a waiting `Enqueue` finds no stored token, it ends its wait as spurious and skips no delay.
With one producer and one consumer, the token is that of the release that freed its space.
With several producers or consumers, a waiting producer can take the token of a later or an earlier release, before its own wake. Its skip can then be too large or too small.
Create a new queue for each run, and use it only from registered workers of that run. Other use is not supported. It can fail the run, or skip delay by the wrong token.
A timeout or a cancellation skips no delay. The abort token of the run cancels each wait. Without a profiler handle, the queue makes no native call.

## Semaphore

`TargetSemaphore(initialCount)` is a counting semaphore for profiled workers. It raises `ArgumentOutOfRangeException` when `initialCount` is below 0.

| Member | Result |
| --- | --- |
| `Wait(timeout, cancellation)` | `true` after it takes a permit. `false` after a timeout or a cancellation, without a permit. |
| `Wait()` | Waits without a limit. It raises `OperationCanceledException` when the run aborts the wait. |
| `Release()` | Adds one permit. Any thread can call it. It raises `SemaphoreFullException` when the semaphore holds `Int32.MaxValue` permits, counting the permits that a waiter has taken but not yet recorded. |
| `CurrentCount` | The number of free permits. |

In a profiled worker, `Release` posts and stores the token in a FIFO list before it adds the permit.
A `Wait` that waits ends its wait with the oldest stored token. A `Wait` that does not wait takes one of the `initialCount` permits first.
With one releasing thread and one waiting thread, the token is that of the release that added the permit.
With several releasing or waiting threads, a waiter can take the token of an earlier or a later release, before its own wake. Its skip can then be too large or too small.
Create a new semaphore for each run, and use it only from registered workers of that run. Other use is not supported.
A timeout or a cancellation skips no delay. The abort token of the run cancels each wait. Without a profiler handle, the semaphore makes no native call.

## Lock

`TargetLock()` is a non-recursive mutual-exclusion lock for profiled workers. `TargetLock(spin)` spins up to `spin`, from 0 to 1 ms, inside its wait before it blocks.
The spin and the block share one timeout budget: the block gets only the part of `timeout` that the spin did not use. A spinning waiter is blocked for the ledger, as a sleeping waiter is. A spin uses CPU time, and it can shorten the time from a release to the next acquire.

| Member | Result |
| --- | --- |
| `Enter(timeout, cancellation)` | `true` after it takes the lock. `false` after a timeout or a cancellation, without the lock. |
| `Enter()` | Waits without a limit. It raises `OperationCanceledException` when the run aborts the wait. |
| `Exit()` | Frees the lock. It raises `SynchronizationLockException` when the calling thread does not hold the lock. |
| `IsHeld` | `true` while a thread holds the lock. |
| `Entries`, `WaitedEntries`, and `HandoffNs` | The entries, the entries that waited, and the time from a release to the acquire of a waiting entry, summed over the waiting entries. |

A second `Enter` by the holder raises `LockRecursionException`.
In a profiled worker, `Exit` posts a token and stores it before it frees the lock. An `Enter` that waits ends its wait with that token.
Only the holder writes the token, so the token is always that of the release that freed the lock. A timeout or a cancellation skips no delay.

## Sample program

`samples/CozNet.TargetLocal` has three modes:

| Command | Output |
| --- | --- |
| `calibrate <out>` | Rounds of `Work.peer` for each millisecond |
| `plain <selectedRounds> <peerRounds> <out>` | Elapsed time without the profiler |
| `profile <speedup> <selectedRounds> <peerRounds> <out>` | Elapsed, global, and effective time, thread results, replay summary, and all events |
| `plain-phases <pattern> <selectedPercent> <out>` | Elapsed time of 10 barrier phases without the profiler, with selected work scaled to `selectedPercent` |
| `binding <out>` | The configuration errors, the capture result, and the verify result after 100 ms of work, with the perf-map entries |
| `shutdown <out>` | Writes the work state to `<out>.marker` 30 ms after the work starts, then calls `Environment.Exit`. The exit code is 0 only when a worker is still working. |
| `profile-phases <speedup> <pattern> <out>` | The profile result for 10 barrier phases. Patterns are `alternating`, `selected-dominant`, and `peer-bound`. |
| `profile-gc <speedup> <selectedRounds> <peerRounds> <intervalMs> <out>` | The profile result, with a forced blocking gen-2 GC every `intervalMs` from the main thread |
| `rejit <out>` | The ReJIT check. It requires the test profiler from `native/rejit/ReJitProbe.Linux.cpp`. |
| `causal-profile <seed> <rotation> <out>` | One block of `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75, with 6000 and 3000 rounds |
| `throughput-profile <seed> <rotation> <out>` | One throughput block of 120 ms experiments. The workers repeat `Work.selected 40` and `Work.peer 20` with one visit after each call. |
| `plain-throughput <selectedRounds> <peerRounds> <out>` | The same loops without the profiler for 120 ms, with the elapsed time and the visits |
| `pipeline-profile <seed> <rotation> <out>` | One throughput block of 120 ms experiments. A producer runs `Work.selected 40` for each item of a `TargetQueue` of 4 items, and a consumer runs `Work.peer 20` and visits for each item. |
| `lock-profile <seed> <rotation> <out>` | One throughput block of 120 ms experiments. Two workers each run `Work.peer 20` outside a `TargetLock` and `Work.selected 40` inside it, and visit once per cycle. |
| `spin-transaction-profile <seed> <rotation> <out>` and `plain-spin-transaction <selectedRounds> <peerRounds> <out>` | The transaction workloads with a consumer that spins up to 1 ms in each `Dequeue` wait |
| `transaction-profile <seed> <rotation> <out>` and `plain-transaction <selectedRounds> <peerRounds> <out>` | A latency progress point around a `TargetQueue` of 4 items. Worker 0 begins a transaction and runs `Work.selected 40`, and worker 1 runs `Work.peer 8` and ends it. The records hold the count and the sum of the queue handoffs. Only the baseline handoff of a profile is free of paid delay. |
| `semaphore-profile <seed> <rotation> <out>` and `plain-semaphore <selectedRounds> <peerRounds> <out>` | Two workers that take turns through two `TargetSemaphore` objects. Worker 0 runs `Work.selected 24`, and worker 1 runs `Work.peer 12` and visits. |
| `spin-lock-profile <seed> <rotation> <out>` and `plain-spin-lock <selectedRounds> <peerRounds> <out>` | The lock workloads with a 1 ms spin |
| `plain-lock <selectedRounds> <peerRounds> <out>` | The same lock workload without the profiler for 120 ms, with the elapsed time and the visits |
| `plain-pipeline <selectedRounds> <peerRounds> <out>` | The same pipeline without the profiler for 120 ms, with the elapsed time and the visits |

The profile mode requires `DOTNET_TieredCompilation=0`, `DOTNET_ReadyToRun=0`, `DOTNET_PerfMapEnabled=3`, and `DOTNET_PerfMapShowOptimizationTiers=1`.
It uses `TargetLocalProfiler.runBound` and gets the optimized method ranges from a `CodeBinding`.
A profile result passes only when both epochs are complete with zero loss, the replay is valid, its global delay and event count equal the native values, no ledger failure occurred, and every handler interval is below 5 ms.
Both modes measure elapsed time from the earlier thread start after a shared barrier to the later thread finish. Each result records both threads' timestamps.
The profile mode finishes each thread after `coz_target_finish`, so the closing payment is inside the measurement.

`TargetLocalPredictionVerify` compiles `libCozNet.TargetLocal.so` with `clang++ -O2` in its own step.
The CMake target `CozNet.TargetLocal` builds the same source, but its binary is not the qualified binary.

See [the tutorial](../Tutorials/Target_Local_Prediction.md), [the ledger](target-delay-ledger.md), and [the dispatcher](target-sample-dispatch.md).
