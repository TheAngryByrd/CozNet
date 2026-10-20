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
A `TargetCausalThread` holds `CpuNs`, `WallNs`, `HandlerNs`, `PaymentNs` (the sum of measured child payments, including the closing payment), `SkipNs` (the sum of wake skips), `Samples`, `Wakes`, `RunDelayNs`, and `WindowPaymentNs`. `WindowPaymentNs` sums the overlap of each child payment of the thread with each transaction window, so a payment in two windows counts in both. It is 0 without transactions. The payment interval ends at the journal time, which is read after the wait returns, so a preemption can move the interval into a later window.
`CpuNs` stops when the native finish disables counting. `WallNs` runs from the work start to the end of the native finish. A throughput experiment counts every visit from the first work start to the last finish.
`Begin` and `End` first pay the debt of the calling target thread, as before a queue release, and then read the clock. `Visit` does not pay. `Arrivals` counts `Begin` calls and `LatencyNs` sums the wall time from each `Begin` to its `End`. `TargetProgressCounter.Transactions` returns the begin and end time of each transaction that ends, in nanoseconds. `EndToEnd` records 0 for both. `Throughput` records the counter values, which stay 0 unless workers call `Begin`.
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

## Candidate discovery

`src/CozNet/TargetDiscovery.fs` ranks the methods that a run sampled. It is portable managed code with no native profiler calls. The source anchors read the assembly and PDB files. Its checks are deterministic. One live functional check ran on the Linux backend, through the sample mode `discovery-check`. See [the discovery limits](../Explanations/target-local-discovery.md).

### Perf-map index

| Function or member | Result |
| --- | --- |
| `PerfMapIndex.tryCreate lines` | `Ok index`, or `MalformedLine (lineNumber, line)` for a line without a hexadecimal start, a positive hexadecimal size, and a label. `OverlappingRanges (first, second)` when two ranges overlap. Empty lines are skipped. |
| `index.Entries` | Each `PerfMapEntry`: `Start`, `End`, `Label`, and `Method`. |
| `index.TryFind ip` | The entry with `Start <= ip < End`, or `None`. |
| `PerfMapIndex.describe error` | A message for an error. |

A method label has the form `[instance ]<return type> [<assembly>] <canonical symbol>[<tier>]`. `Method` is `Some { Assembly; Symbol; Tier }` for that form, and `None` for other labels, such as stubs.

### Scope and limit

| Function | Result |
| --- | --- |
| `TargetDiscoveryScope.tryCreate include exclude` | `Ok scope`, `NoInclude` for an empty include list, or `BlankPrefix`. |
| `TargetDiscoveryScope.contains scope method` | True when the symbol starts with an include prefix and with no exclude prefix, by ordinal comparison, and its assembly is not `CozNet`. An exclude prefix wins. |
| `TargetDiscoveryLimit.tryCreate count` | `Ok limit` for a count of at least 1, or an error message. |

### Ranking

| Function | Result |
| --- | --- |
| `TargetDiscovery.rankEventsUnchecked scope index events limit` | Ranks the `Sample` events. It does not check the run. |
| `TargetDiscovery.rank scope index run limit` | Checks the `TargetProfileResult`, and then ranks `run.Events`. |

Each sample falls in exactly one class, tested in this order:

| Class | Condition |
| --- | --- |
| `Unmapped` | No entry holds the address. |
| `NotBindable.NotMethodLabel` | The entry label is not a method label. |
| `NotBindable.NotOptimized` | The tier is not `[Optimized]`. |
| `NotBindable.NotUnique` | `CodeBinding.tryCapture` does not bind the symbol to exactly this entry. A second entry of the symbol in any tier is in this class. Two optimized entries with one symbol, and a symbol inside another optimized label, are in this class. |
| `OutOfScope` | The entry is bindable, and the scope excludes it. |
| `InScope` | The entry is bindable and in scope. |

`TargetDiscoveryResult` holds:
- `Counts`: `Samples`, `Unmapped`, `NotBindable`, `OutOfScope`, and `InScope`. The classes add up to `Samples`.
- `Ranked`: `DiscoveredMethod` values with `Symbol`, `Assembly`, `Start`, `End`, `Samples`, and `WorkerSamples`, ranked by samples and then by ordinal symbol.
- `OmittedMethods` and `OmittedSamples`: the methods and samples that the limit removes. `InScope` equals the ranked samples plus `OmittedSamples`.

The errors are:

| Error | Cause |
| --- | --- |
| `NoSamples` | No `Sample` event. |
| `NoSamplesInScope counts` | No sample in scope. |
| `RunIncomplete reasons` | `rank` only. `Passed` is false, `BindingError` is set, `CloseCode` is not 0, the experiment or a thread has a failure code, a thread lost samples or has a finish code that is not 0, or the replay failed. A full journal shows only as a failure code. |
| `DelayAdded (globalNs, creditedSamples)` | `rank` only. The run added delay, or a sample has a credit. |

`rank` cannot check the speedup of the run, and it cannot check that the perf-map lines came from the same process after the run. Read the perf map of the profiled process after the run.

Each sample takes one journal entry. A worker also keeps 45 entries free for settlement. So the journal capacity of each worker must be above its sample count, plus 45, plus its other events. A full journal fails the run, and `rank` then gives `RunIncomplete`.

### Discovery workflow

`src/CozNet/TargetDiscoveryWorkflow.fs` joins the ranking, the anchors, and the design check into one boundary. Only `PerfMapSnapshot.tryCaptureCurrentProcess` uses the Linux backend.

| Function or member | Result |
| --- | --- |
| `PerfMapSnapshot.tryCaptureCurrentProcess ()` | Reads `/tmp/perf-<pid>.map` of the current process once. `Ok` holds the lines, `CapturedInProcess (processId, path)`, and `FileBytesSha256`. `Unsupported` outside Linux x64, `Unreadable (path, message)` for a read error. |
| `PerfMapSnapshot.trySupplied label lines` | A snapshot of lines that the caller supplies, with `Supplied label` and `LineContentSha256`. `BlankLabel` for an empty label. |
| `snapshot.Lines`, `snapshot.CopyCapturedBytes ()` | Copies. A change of a copy does not change the snapshot. |
| `TargetMethodCatalog.tryCreate methods` | A catalog of the given methods. `NoMethods` for an empty list, `DuplicateSymbol (symbol, methods)` when two methods format as one symbol. `catalog.Unsupported` lists the methods that it cannot format. |
| `TargetDiscoveryWorkflow.prepare scope eligible run map catalog limit` | `Ok outcome`, or `MapMalformed`, `BindingDisagreesWithMap` when `CodeBinding.verify` of the eligible binding fails over the map, or `Ranking` with a `TargetDiscovery.rank` error. |
| `TargetDiscoveryWorkflow.tryDesign outcome eligible candidates period speedups blocks seed rotation progress` | `Ok design` from the snapshot of the outcome, or `NotDiscovered symbol`, `EligibleNotBound message`, or `Design` with a `TargetCausalDesign` error. |

`LineContentSha256` is the SHA-256 of the UTF-8 lines joined with `\n`, without a final `\n`. `FileBytesSha256` is the SHA-256 of the file bytes. A capture removes a leading byte-order mark, splits its bytes at `\n`, removes a final `\r` from each line, and drops one empty last line.

An outcome is opaque. Its members are `Snapshot`, `Counts`, `Candidates`, `OmittedMethods`, `OmittedSamples`, and `Limitations`. `Candidates` returns copies of `TargetDiscoveredCandidate` records:
- `Method`: the `DiscoveredMethod`.
- `Eligibility`: `InEligibleBinding` when the eligible binding holds its range, otherwise `OutsideEligibleBinding`.
- `Anchor`: `Anchored anchor`, `NoCatalog`, `NotInCatalog`, `CatalogAssemblyDiffers catalogAssembly`, or `AnchorFailed error`. An anchor result never removes a candidate.
  `CatalogAssemblyDiffers` means that the catalog method has the symbol, but its assembly name differs from the candidate label. The check uses the simple assembly name, so the caller must supply catalog methods of the same build as the profiled code.

`tryDesign` takes symbols, not candidate records. Each candidate must be a symbol of the outcome, and each candidate must also be in the eligible list, as `TargetCausalDesign.tryCreate` requires. The eligible list is bound over the snapshot of the outcome.

`Limitations` always holds `SpeedupNotRecorded`, `RunBindingIsCallerPrecondition`, `RunMapAssociationNotProven`, `MapTimingNotProven`, `MethodLevelOnly`, and `NoCallchain`. A supplied snapshot adds `MapSupplied label`.

### Symbols and source anchors

| Function | Result |
| --- | --- |
| `TargetSymbol.tryFormat method` | `Ok "<declaring type full name>::<name>(<parameters>)"` for the verified subset. Otherwise `NoDeclaringType`, `GenericMethod`, `GenericType typeName`, or `UnsupportedParameter (position, typeName)`. |
| `TargetSourceAnchor.tryFind method` | `Ok { Document; Line; Column }` for the first sequence point of the method that is not hidden. |
| `TargetSourceAnchor.tryFindIn assemblyPath metadataToken` | The same, for a method token in an assembly file. |

The verified parameter names are `bool`, `char`, `int8`, `uint8`, `int16`, `uint16`, `int32`, `uint32`, `int64`, `uint64`, `float32`, `float64`, `native int`, `native uint`, `string`, and `object`. One suffix can follow them: `[]` for a one-dimensional array, `&` for a reference, or `*` for a pointer. Instance methods and nested types are supported.

A source anchor uses an embedded portable PDB first. Otherwise it uses the PDB next to the assembly, with the file name in the CodeView entry, only when the PDB id equals the CodeView id. The anchor errors are `NoAssemblyFile`, `NoDebugDirectory`, `NotPortablePdb`, `PdbNotFound searched`, `PdbMismatch path`, `PdbMalformed (path, message)`, `PdbUnreadable (path, message)`, `AssemblyMalformed (path, message)`, `AssemblyUnreadable (path, message)`, `MethodNotInPdb token`, and `OnlyHiddenSequencePoints token`. A file that cannot be read or parsed gives an error, not an exception. `tryFind` gives `NoAssemblyFile` for a method of a dynamic assembly.

## Line attribution

`src/CozNet/TargetLineAttribution.fs` maps a native address inside one discovered method to a sequence point. It is portable. The native-to-IL map is supplied by the caller, or it comes from a [map capture](#map-capture) record. No CozNet run reads it from a live runtime yet. See [the limits](../Explanations/target-local-line-attribution.md).

`TargetCodeEvidence` is the raw input:

| Field | Meaning |
| --- | --- |
| `Label` | The source of the evidence, for example `synthetic: ...` or `retained: ...`. It must not be blank. |
| `ModuleVersionId` | The MVID of the module. |
| `MethodToken` | The MethodDef token of the method. |
| `CodeStart` | The native start address of the body. |
| `Regions` | The code regions of the body. Exactly one region is supported. |
| `NativeBodyCount` | The number of native bodies of the method. It must be 1. |
| `Entries` | Raw map entries: the IL offset as `uint32`, the native start, and the native end, relative to `CodeStart`. |

| Function | Result |
| --- | --- |
| `TargetLineAttribution.tryPrepare outcome symbol assemblyPath evidence` | `Ok resolver`, or a list of `TargetLinePrepareError`. |
| `TargetLineAttribution.resolve resolver ip` | A `TargetLineResult`. |

`tryPrepare` checks, in this order:
- The symbol is a candidate of the outcome, else `CandidateNotDiscovered`.
- The evidence: `EvidenceLabelBlank`, `MultipleBodiesUnsupported`, `ColdCodeUnsupported`, `RangeOverflow`, and `RangeMismatch` when the region differs from the candidate range.
- The PE and its matched PDB, through the source anchor reader: `SourceError`.
- The identity: `IdentityMismatch` for the MVID, the assembly name, the type and method name, or the parameter types of the token, `SharedGenericUnsupported`, and `NoIlBody`. The IL body size is the length of the IL code, without the method header.
- The map: `EmptyMap`, `IlOffsetBeyondBody`, `InvertedRange`, and `UnsupportedMapShape` for an end above the code size or an overlap.

The IL offset values `0xFFFFFFFF`, `0xFFFFFFFE`, and `0xFFFFFFFD` are no mapping, prolog, and epilog. A zero-length entry is accepted and holds no address. A part of the body that no entry covers is in `resolver.Uncovered`.

`resolve` gives:
- `Line location`: the document, the start and end line and column, and the IL range of the sequence point. The IL range of the last point ends at the IL body size.
- `InProlog`, `InEpilog`, `Unmapped`, or `Uncovered`.
- `HiddenSequencePoint ilOffset` or `NoSequencePoint ilOffset`.
- `OutsideBody` for an address outside the code region.

`resolver.Limitations` always holds `SuppliedEvidence label`, `OptimizedBoundariesAreCoarse`, `SequencePointRangeInferred`, `InlineesAttributedToCallSite`, `IlRewriteNotExcluded`, `MapEntryTimingNotProven`, `CodeLifetimeNotProven`, and `NoCallchainAttribution`.

```fsharp
// The evidence is supplied. No live acquisition exists yet.
let evidence =
    { Label = "synthetic: two-entry map"; ModuleVersionId = mvid; MethodToken = token; CodeStart = start
      Regions = [ { Start = start; Size = size } ]; NativeBodyCount = 1
      Entries = [| struct (0xFFFFFFFEu, 0u, 8u); struct (0u, 8u, uint32 size) |] }

match TargetLineAttribution.tryPrepare outcome symbol assemblyPath evidence with
| Ok resolver -> printfn "%A" (TargetLineAttribution.resolve resolver sampleIp)
| Error errors -> printfn "No resolver: %A" errors
```

## Map capture

`libCozNet.MapProfiler.so` is a separate Linux CLR profiler library in `native/mapprofiler`. It captures the native-to-IL map of one selected method. No CozNet run loads it yet. `src/CozNet/TargetMapCapture.fs` is the portable parser of its records.

### Selection

The library reads these variables once, in `Initialize`:

| Variable | Format |
| --- | --- |
| `COZNET_MAP_MODULE` | The module file name, 1 to 260 ASCII characters, without `/`. It matches the last path segment of the module. |
| `COZNET_MAP_TOKEN` | A MethodDef token of exactly 10 characters: lowercase `0x`, `06`, and six hexadecimal digits, not `0x06000000`. |
| `COZNET_MAP_MVID` | The MVID of the module, in the form `11223344-5566-7788-99AA-BBCCDDEEFF01`. |

The event mask is `COR_PRF_MONITOR_JIT_COMPILATION | COR_PRF_MONITOR_MODULE_LOADS`. A missing or malformed value gives `SelectionInvalid`, and the library asks for no events.

### Exports

| Export | Result |
| --- | --- |
| `DllGetClassObject` | The class factory for CLSID `{4B1F2C70-6D3A-4E0B-9C51-7A2E8D4F6B13}`, else `CLASS_E_CLASSNOTAVAILABLE`. |
| `int32_t coz_map_read(uint8_t* buffer, uint32_t capacity, uint32_t* required)` | 0 when it wrote the record, 1 during capture, 2 when `capacity` is below `*required`, and 3 when `required` is null. It writes no record byte for 1, 2, and 3. |

### Record, version 1

All fields are little-endian. The header is 704 bytes, and each copied map entry adds 12 bytes.

| Offset | Size | Field |
| ---: | ---: | --- |
| 0 | 4 | Magic `0x504D5A43` |
| 4 | 2 | Version 1 |
| 6 | 2 | Header size 704 |
| 8 | 4 | Total size |
| 12 | 4 | State: 0 `NotSeen`, 2 `Complete` |
| 16 | 4 | Outcome |
| 20 | 4 | Outcome HRESULT, signed |
| 24 | 4 | Flags: 1 `SecondNotification`, 2 `UnloadDuringCapture`, 4 `UnloadedAfterCapture`, 8 `Closed` |
| 28 | 4 | The first map count for `MapCountChanged`, else 0 |
| 32 | 8 | Function id |
| 40 | 8 | Module id |
| 48 | 16 | MVID, in `GUID` structure order |
| 64 | 16 | Selected MVID |
| 80 | 4 | MethodDef token |
| 84 | 8 | Body count, reported and copied |
| 92 | 8 | Region count, reported and copied |
| 100 | 8 | Entry count, reported and copied |
| 108 | 4 | Entry capacity, 4096 |
| 112 | 16 | Two body starts |
| 128 | 32 | Two regions: start and size |
| 160 | 16 | Counters: function info failures, module info failures, name truncations, unsafe callbacks |
| 176 | 8 | Module name units, reported and copied |
| 184 | 520 | Module name, 260 UTF-16 units |
| 704 | 12 each | Entries: raw IL offset, native start, native end |

### Outcomes

| Value | Outcome | HRESULT |
| ---: | --- | --- |
| 1 | `Captured` | 0 |
| 2 | `InfoUnavailable` | Failed |
| 3 | `SelectionInvalid` | `E_INVALIDARG` |
| 4 | `EventMaskFailed` | Failed |
| 5 | `JitFailed` | The JIT status |
| 6 | `MetadataFailed` | Failed |
| 7 | `MvidMismatch` | 0 |
| 8 | `StartsFailed` | Failed, also `ERROR_INSUFFICIENT_BUFFER` for more than two bodies |
| 9, 10 | `NoNativeBody`, `MultipleBodies` | 0 |
| 11 | `CodeInfoFailed` | Failed |
| 12, 13 | `NoCodeRegion`, `CodeInfoUnexpectedCount` | 0 |
| 14 | `MapFailed` | Failed |
| 15, 16, 17 | `MapEmpty`, `MapTooLarge`, `MapCountChanged` | 0 |
| 18 | `InternalError` | Failed |

### Parser

| Function | Result |
| --- | --- |
| `TargetMapCapture.tryParse bytes` | `Ok record` for every valid record, also a failure record, or a `TargetMapCaptureParseError`. |
| `TargetMapCapture.toEvidence source record` | `TargetCodeEvidence` only for `Completed Captured` with no flag, else a `TargetMapEvidenceError`. |
| `TargetMapCaptureSource.trySynthetic label` | A synthetic source. The evidence label is `synthetic: <label>`. |
| `TargetMapCaptureSource.tryLive processId librarySha256` | Internal to CozNet assemblies. A live source, with the evidence label `captured: pid <pid> profiler <sha256>`. |

`tryParse` rejects with these errors:
- `TooShort`, `BadMagic`, `UnsupportedVersion`, `UnexpectedHeaderSize`, `UnexpectedCapacity`, and `LengthMismatch` when the total size differs from the buffer length.
- `UnknownState`, `UnknownOutcome`, and `UnknownFlags`. State 1 is never written, so it is `UnknownState 1u`.
- `CountExceeded` when a copied count is above its reported count or its buffer.
- `NonZeroReserved` for a byte after the copied starts, regions, or name units, and for capture fields in a `NotSeen` record.
- `InconsistentRecord` for an HRESULT, a detail, a count, an MVID, a module name, or a flag that the outcome does not allow. A record without a claim, which is `NotSeen` or an `Initialize` failure, can only have the `Closed` flag.

The record copies the bytes that it keeps. `record.Entries` and `record.BodyStarts` give new arrays. `toEvidence` gives `CaptureNotSeen`, `CaptureFailed outcome`, or `CaptureInvalidated flags`.

```fsharp
let source = TargetMapCaptureSource.trySynthetic "retained fixture" |> Result.defaultWith failwith

match TargetMapCapture.tryParse bytes with
| Error error -> printfn "Not a record: %A" error
| Ok record ->
    match TargetMapCapture.toEvidence source record with
    | Ok evidence -> printfn "%A" (TargetLineAttribution.tryPrepare outcome symbol assemblyPath evidence)
    | Error error -> printfn "Status %A, flags %A: %A" record.Status record.Flags error
```

See [how to verify the map capture component](../How_Tos/verify-map-capture.md).

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
| `discovery-check <out>` | A run at 0% with no selected method, and then the discovery workflow on its live samples. The record holds the process id, the hashes of the perf map, the assemblies, and the PDBs, the run results, the sample classes, the ranked methods, their source anchors, and the design result. `<out>.perf-map` holds the perf map that the process read. Each record has `schema: "coznet.discovery-check/2"` and `mapHashKind`, also an error record. The exit code is 0 only when the run passed. It is 4 on a platform other than Linux x64. |
| `shutdown <out>` | Writes the work state to `<out>.marker` 30 ms after the work starts, then calls `Environment.Exit`. The exit code is 0 only when a worker is still working. |
| `profile-phases <speedup> <pattern> <out>` | The profile result for 10 barrier phases. Patterns are `alternating`, `selected-dominant`, and `peer-bound`. |
| `profile-gc <speedup> <selectedRounds> <peerRounds> <intervalMs> <out>` | The profile result, with a forced blocking gen-2 GC every `intervalMs` from the main thread |
| `rejit <out>` | The ReJIT check. It requires the test profiler from `native/rejit/ReJitProbe.Linux.cpp`. |
| `causal-profile <seed> <rotation> <out>` | One block of `TargetCausalProfile.run` with candidates `Work.selected` and `Work.peer` at speedups 25, 50, and 75, with 6000 and 3000 rounds |
| `throughput-profile <seed> <rotation> <out>` | One throughput block of 120 ms experiments. The workers repeat `Work.selected 40` and `Work.peer 20` with one visit after each call. |
| `plain-throughput <selectedRounds> <peerRounds> <out>` | The same loops without the profiler for 120 ms, with the elapsed time and the visits |
| Throughput-family plain modes | `plain-throughput`, `plain-pipeline`, `plain-lock`, `plain-spin-lock`, `plain-semaphore`, `plain-transaction`, and `plain-spin-transaction` first run their arm once untimed and write it to `<out>.warmup`. They then force a full collection and run the timed arm, as a profile runs its warm-up and forces a collection before each experiment. |
| `pipeline-profile <seed> <rotation> <out>` | One throughput block of 120 ms experiments. A producer runs `Work.selected 40` for each item of a `TargetQueue` of 4 items, and a consumer runs `Work.peer 20` and visits for each item. |
| `lock-profile <seed> <rotation> <out>` | One throughput block of 120 ms experiments. Two workers each run `Work.peer 20` outside a `TargetLock` and `Work.selected 40` inside it, and visit once per cycle. |
| `spin-transaction-profile <seed> <rotation> <out>` and `plain-spin-transaction <selectedRounds> <peerRounds> <out>` | The transaction workloads with a consumer that spins up to 1 ms in each `Dequeue` wait |
| `probe <out>` | Runs `Work.selected 40` on one thread without a profiler: 20 untimed samples, then 100 samples with the wall time and the thread CPU time of each, and the CPU before and after it. It records the checksum, the count of differing results, and the realtime, monotonic, and boot clocks at its start and end. |
| `transaction-selected-profile <peerRounds> <seed> <rotation> <out>` | The transaction workload with `Work.peer <peerRounds>`. It profiles only `Work.selected` at 0, 25, 50, and 75%, after the warm-up experiment. It also writes `<out>.evidence.json`: for each experiment, the signed and clipped totals of all transactions, a histogram of delay steps for each window, and up to 4096 windows with their counts of exported and omitted windows. Each experiment also has a `boundary` record: ordered reads of the counters and the clock at the start and the stop, the delay from the journal at those times, the upstream counters and latency or the reasons that it is undefined, and the races between counts and stamps. These are upstream-like ordered reads, not a common-instant snapshot. They do not reproduce the upstream reads of the global delay or the repeated counter reads of the upstream log, and the counters belong to one experiment, not to the process. The paired diagnostic uses it. |
| `transaction-profile <seed> <rotation> <out>` and `plain-transaction <selectedRounds> <peerRounds> <out>` | A latency progress point around a `TargetQueue` of 4 items. Worker 0 begins a transaction and runs `Work.selected 40`, and worker 1 runs `Work.peer 8` and ends it. The records hold the count and the sum of the queue handoffs. `handoffPlacement` counts the handoffs by the placement of the producer CPU at the enqueue and the consumer CPU after the dequeue (`sameCpu`, `sameCore`, `otherCore`, or `unknown`), in five time classes from below 50 microseconds to at least 2 ms. `handoffIdle` counts the handoffs by the time from the consumer clock read before `Dequeue` to the producer clock read before `Enqueue` (`ready` when the enqueue started first, `below100us`, `below400us`, or `atLeast400us`), in five time classes from below 10 to at least 150 microseconds. Only the baseline handoff of a profile is free of paid delay. |
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
