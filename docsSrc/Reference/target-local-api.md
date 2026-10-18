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
| `coz_target_finish(thread, payment_ip, result)` | Same worker | Closes the epoch, pays remaining debt at `payment_ip`, and retires the session. |
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

`src/CozNet/TargetLocalProfiler.fs` binds the C ABI for managed programs.

`PerfMap.tryRange lines symbol` returns `Ok (begin, end)` for exactly one `[Optimized]` perf-map entry that contains `symbol`.
It ignores entries with a zero size, an overflow, or a field that is not hexadecimal. Zero or several matches return `Error`.
`PerfMap.currentProcess ()` returns `/tmp/perf-<pid>.map`.

`TargetLocalProfiler.run speedup period selected eligible journalCapacity workers monitor` runs one profile:

1. It raises `PlatformNotSupportedException` unless the process is Linux x64. It then raises `ArgumentException` unless there are 1 to 64 workers. It raises `InvalidOperationException` if a managed layout differs from `coz_target_layout`, or if the library has no `coz_target_layout` export.
   It raises `ArgumentException` if a `PaymentIp` is outside every eligible span.
2. It opens the experiment and starts one thread for each `TargetWorker { Work; PaymentIp }`.
3. Each thread registers, waits at a barrier, starts sampling, waits again, runs `Work`, finishes, copies its journal, and releases.
4. The calling thread runs `monitor` with a `TargetRunState`. `Working` is true while any worker runs `Work`. `Running` is true while any thread lives.
5. It joins the threads, closes the experiment, and replays every journal with `TargetLocalReplay.replay`.
   If `monitor` throws, a thread cannot start, or the caller is interrupted during the join, it releases the barrier, joins the started threads, and closes the experiment before it raises `AggregateException`.

`TargetProfileResult` holds `ElapsedNs`, `GlobalNs`, `EffectiveNs`, the experiment result, each thread outcome, all events, the replay result, and `Passed`.
`Passed` is true only when every thread completes with zero loss and quiescence, every handler interval is below 5 ms, no ledger failure or foreign signal occurs,
and the replay is valid with the native global delay and event count. An exception in `Work` makes the run fail.
See [the how-to](../How_Tos/profile-fork-join-program.md).

## Sample program

`samples/CozNet.TargetLocal` has three modes:

| Command | Output |
| --- | --- |
| `calibrate <out>` | Rounds of `Work.peer` for each millisecond |
| `plain <selectedRounds> <peerRounds> <out>` | Elapsed time without the profiler |
| `profile <speedup> <selectedRounds> <peerRounds> <out>` | Elapsed, global, and effective time, thread results, replay summary, and all events |
| `profile-gc <speedup> <selectedRounds> <peerRounds> <intervalMs> <out>` | The profile result, with a forced blocking gen-2 GC every `intervalMs` from the main thread |

The profile mode requires `DOTNET_TieredCompilation=0`, `DOTNET_ReadyToRun=0`, `DOTNET_PerfMapEnabled=3`, and `DOTNET_PerfMapShowOptimizationTiers=1`.
It uses `TargetLocalProfiler.run` and reads the optimized method ranges with `PerfMap.tryRange`.
A profile result passes only when both epochs are complete with zero loss, the replay is valid, its global delay and event count equal the native values, no ledger failure occurred, and every handler interval is below 5 ms.
Both modes measure elapsed time from the earlier thread start after a shared barrier to the later thread finish. Each result records both threads' timestamps.
The profile mode finishes each thread after `coz_target_finish`, so the closing payment is inside the measurement.

`TargetLocalPredictionVerify` compiles `libCozNet.TargetLocal.so` with `clang++ -O2` in its own step.
The CMake target `CozNet.TargetLocal` builds the same source, but its binary is not the qualified binary.

See [the tutorial](../Tutorials/Target_Local_Prediction.md), [the ledger](target-delay-ledger.md), and [the dispatcher](target-sample-dispatch.md).
