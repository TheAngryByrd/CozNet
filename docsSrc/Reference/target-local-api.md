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

`TargetCausalDesign.tryCreate binding period eligible candidates speedups blocks seed` parses the experiment design. It returns `Ok TargetCausalDesign` or `Error` with a list of `TargetCausalDesignError` values:
`NoCandidates`, `DuplicateCandidate`, `UnboundCandidate`, `IneligibleCandidate`, `UnboundEligible`, `NoSpeedups`, `DuplicateSpeedup`, `SpeedupOutOfRange`, `NoBlocks`, and `PlanRejected`.
`TargetCausalDesign.describe` gives the message for one error. A design has a private representation, and it keeps copies of its input arrays.

`TargetCausalProfile.run design journalCapacity workers` runs the design in one process. `workers` is a function that returns new workers for each experiment.
Each experiment calls `TargetLocalProfiler.runBound` with one candidate as the selected symbol.

1. Only `tryCreate` makes a design, so `run` receives only valid candidates, speedups, blocks, and a period that the replay plan accepts.
2. Each block runs one baseline at speedup 0 and one experiment for each candidate and speedup, in a random order from `seed`.
   Before each experiment, it runs a full blocking collection, outside the measured span. `workers` must return workers with fresh state for each call.
3. Each `TargetCausalExperiment` holds `Block`, `Order`, `Candidate`, `Speedup`, `ElapsedNs`, `GlobalNs`, `EffectiveNs`, `Passed`, and `Error`.
   An experiment that raises an exception keeps the exception type and message. An experiment that does not pass keeps each cause that rejected it: binding, replay, ledger, close, signal, and thread errors, including handler intervals of 5 ms or more. `Passed` is then false.
4. Each `TargetCausalPoint` holds one prediction for each block, `1 - E / E0`, where E0 is the baseline of the same block.
   A prediction is `None` when the experiment or its baseline did not pass. `ValidBlocks` counts the other blocks, and `MedianPrediction` is their median.

`TargetCausalProfile.run` requires Linux x64 through `runBound`. The design and the prediction code are portable.
The progress point is the end of the workload. Throughput progress points are not supported.

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
| `causal-profile <seed> <out>` | One block of `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75, with 6000 and 3000 rounds |

The profile mode requires `DOTNET_TieredCompilation=0`, `DOTNET_ReadyToRun=0`, `DOTNET_PerfMapEnabled=3`, and `DOTNET_PerfMapShowOptimizationTiers=1`.
It uses `TargetLocalProfiler.runBound` and gets the optimized method ranges from a `CodeBinding`.
A profile result passes only when both epochs are complete with zero loss, the replay is valid, its global delay and event count equal the native values, no ledger failure occurred, and every handler interval is below 5 ms.
Both modes measure elapsed time from the earlier thread start after a shared barrier to the later thread finish. Each result records both threads' timestamps.
The profile mode finishes each thread after `coz_target_finish`, so the closing payment is inside the measurement.

`TargetLocalPredictionVerify` compiles `libCozNet.TargetLocal.so` with `clang++ -O2` in its own step.
The CMake target `CozNet.TargetLocal` builds the same source, but its binary is not the qualified binary.

See [the tutorial](../Tutorials/Target_Local_Prediction.md), [the ledger](target-delay-ledger.md), and [the dispatcher](target-sample-dispatch.md).
