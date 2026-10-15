# Sampled timer workload

`SampledTimerWorkload` connects `ICpuEventSession` observations to `TimerServiceController`.
Its current entry point uses the Linux x64 timer and perf adapters.
It runs two synchronous arithmetic workers with fixed native method mapping.
It supports cold and explicitly initialized zero and 50-percent experiments.
Measured warmup resets are not implemented here.

## Sampling and accounting

The selected worker creates its CPU-event session on its own OS thread.
The controller owns sample consumption and immutable accounting updates.
The first sample uses a fresh eligibility epoch and establishes a baseline without credit.
Selected-method intervals can then earn credit from their reported CPU periods.

A selected service reservation advances the eligibility epoch.
The controller disables sampling and excludes queued samples before submitting that lease.
It resumes sampling only after retired journal release.
The first sample in the new epoch establishes another baseline.
This exclusion can discard eligible CPU periods. It does not prove exact source-line attribution.

Observed selected debt subtracts published sample credit, settled wait, and active confirmed selected wait from global delay.
Observed peer debt subtracts settled wait and active confirmed peer wait.
The controller stores cumulative released wait independently of diagnostic success.
Failure snapshots read that total even when closing a diagnostic phase throws after release.
Each measurement iteration checks the unchanged 2,000,000 ns debt limit.
`TimerDebtLimit` accepts nonnegative debt up to and including that limit for both workers.
The portable controller checks use the same gate at one nanosecond below, at, and above the limit.
The check uses scalar debt values. It allocates boundary records only for periodic capture, new peaks, and failures.
The controller retains the failing boundary before it reports a debt failure.

Timer accounting enforces the 50,000 ns requested child wait, 5 ms handler interval, and 100 ms first receipt bounds.
The lease budget remains at most 2,000,000 ns.

## Shutdown

Shutdown disables sampling before cancellation and target retirement.
It polls pending journals before requesting cancellation, so completed journals can settle first.
Each worker retires its timer registration before it exits.
The controller joins each worker and polls any outstanding retired journal before releasing its ownership.
Unresolved accounting or release failure prevents native session close.
Shutdown does not forgive remaining ledger debt. The final boundary preserves that debt.
The workload's outer failure list retains exception objects during cleanup and formats their text afterward.
Controller journals format their own failures when those failures occur.
After worker ownership is released, `TimerSessionCleanup` attempts sample disposal, native close, and start-gate disposal in order.
It retains each error and continues cleanup. A sample-disposal error cannot skip native close or escape before result serialization.
Any cleanup error still fails the workload. `closed` reports native close only.

## Output

| Field | Meaning |
| --- | --- |
| `samples` | Raw CPU samples, eligibility epoch, acceptance, credit, and controller timestamp. |
| `boundaries` | Global delay, credit, settled waits, active waits, and remaining debt. |
| `peaks` | Largest debt observed during measurement for each worker. |
| `journals` | Timer operations whose native release succeeded. |
| `waits` | Cumulative released wait in nanoseconds, ordered selected then peer. Includes payment retained after diagnostic phase failure. |
| `pending` | Timer operations that retain controller ownership. |
| `lifetimes` | Worker join, retirement, release, and failure outcomes. |
| `stableMap` | Whether the selected native range matched at measurement completion. |
| `closed` | Whether the native session closed after worker cleanup. |
| `failures` | Measurement and cleanup errors. |
| `failureBoundary`, `failureStage` | Accounting snapshot and stage at the primary failure, before shutdown. |
| `controllerPhases` | Optional bounded controller intervals with bracketed thread CPU readings and completion status. |
| `controllerPhasesOverwritten` | Number of older phase records replaced by the rolling timer diagnostic. |
| `initialization` | Separate synthetic samples, timer journals, and final accounting from optional managed initialization. |
| `transportInitialization` | Dedicated native setup slot, deferred command observations, cleanup status, and failures. |
| `initialIntervalNanoseconds` | Configured initial timer interval, either 1,000,000 ns or 10,000 ns. |
| `forceGc`, `gcObservations` | Whether forced GC was requested, and each collection's timestamps, index, and pause records. |
| `gcJoined`, `gcReachedMeasurement`, `gcError` | GC worker cleanup, complete measurement-window reach, and worker error. |
| `measurementStartedTimestamp`, `measurementFinishedTimestamp` | Process-clock boundaries for GC reach and measurement duration. |
| `measurementCapture` | Immutable start and end readings with timestamp brackets and explicit capture status. |

Timer journals retain their own schema. Their timestamps do not represent the older signal transport's queue phases.
The workload does not compare predictions with a physical optimization.
The [measurement boundary contract](measurement-boundaries.md) keeps its endpoint separate from shutdown accounting.
Unreceived sample and journal tails still prevent a prediction claim from these records alone.
`predictionAccuracyEstablished` therefore remains `false`.

## Remaining validation limits

Shutdown polls before cancellation, but a target can become quiescent between those operations.
Native cancellation can then return `EBUSY`. The run retains that failure even if later cleanup succeeds.
This conservative false-failure behavior does not turn an invalid experiment into a pass.

A closing-clock failure during the shutdown release callback can leave the worker lifetime unreleased after successful native release.
The callback exception prevents session cleanup. The run retains its wait totals and fails.
This prototype accepts conservative retention because a failed release callback does not prove completion of the full worker cleanup contract.

Portable checks do not exercise the complete sampling lifecycle with both slots pending.
The live evidence does not cover every disable, drain, epoch-change, and resume interleaving.
The native preparation path also lacks complete fault injection for submission ambiguity, timeout, and release failure.
The interval API enforces controller ownership, but its native fixture does not directly test the non-controller `EPERM` path.
These gaps remain accepted limits of this internal experimental workload. They do not extend public engine support.

The [first retained comparison](../../reliability/features/native-signal/timer-sampled.md) passes zero percent and fails treatment lease capacity.
It retains 14,999,982 ns of final peer debt. Both cases complete native cleanup.
That capture predates the separate failure-boundary fields.

## Controller phase diagnostic

Timer diagnostics retain the latest 128 phase records in chronological order.
New records replace the oldest records after the buffer fills.
`controllerPhasesOverwritten` reports how many records were replaced.
Other workloads keep the default prefix capture behavior.
The records distinguish sample drain, selected submission, peer submission, selected journal processing, and peer journal processing.
An initial setup record exercises the CPU clock before measurement starts.
An operation failure retains `Completed = false` if the closing clock succeeds.
If the closing clock fails, the phase produces no interval. The controller retains the exception and the workload fails.

CPU deltas measure controller execution between bracketed readings.
Elapsed intervals include execution and time when the controller did not execute.
Neither interval alone identifies JIT, GC, or a scheduling cause.
The diagnostic adds clock reads and allocation. Its results are not a capacity baseline.
The first drain is eligible for retention. Later empty drains do not consume record capacity.
Older startup records can leave the rolling buffer before measurement finishes.
Unchanged timer polls produce no phase record.
Changed journal phases include reading, accounting, and any release. Submission phases include the native call and binding.
The initial probe and lease preparation remain outside those service phases.
See [the retained evidence](../../reliability/features/native-signal/timer-sampled.md) for earlier captures that exhausted the budget on empty polls.

Three portable checks verify phase retention:

- `PrefixStopsAfterCapacity` checks the unchanged default limit and clock-read count.
- `RecentPhasesRemainOrderedAcrossWraps` checks chronological order and replacement counts through multiple wraps.
- `DiscardedDrainsDoNotEvictButFailuresDo` checks retention of incomplete operations without evicting records for empty drains.

## Managed initialization

`LinuxInitializedTimer` runs a fixed synthetic path before native creation or worker startup.
It uses the measurement workload's sample drain, preparation, and polling functions.
An in-memory perf ring supplies one synthetic 80,000 ns sample.
The synthetic transport supplies one completed peer journal and one completed selected journal.

The path requires 40,000 ns of credit, 70,000 ns of peer wait, and 30,000 ns of selected wait.
It also requires zero remaining debt and no pending ownership.
The output labels these records as `synthetic` under `initialization`.

Live measurement starts with fresh ledger handles, sample stream, counters, cadence, and native controller ownership.
It starts at a fresh eligibility epoch, so the first live sample earns no credit.
Synthetic waits do not count toward live zero-percent checks or measured payment.
This path exercises managed startup. It does not initialize every native submission or receipt path.
The latest retained initialized run passes zero percent and fails treatment at 3,499,914 ns of observed peer debt.
Both cases release their workers and close the native session.

## Native transport initialization

`LinuxPreparedTimer` adds a dedicated native setup slot after managed initialization and before live worker creation.
The timer API permits only one session per process.
Setup uses slot 2 in that session. Live selected and peer workers use slots 0 and 1.
A temporary target receives one 50,000 ns command outside the selected instruction range.
The setup requires one deferred child, zero measured wait, and the existing receipt and handler bounds.
It then retires and joins the setup target and releases its journal.

The setup must pass before live measurement starts.
Its records use the source label `synthetic-command/native-deferred/dedicated-slot`.
It does not publish sample credit or inject measured wait into the live ledger.
The live slots have separate registrations and generation sequences.
The session closes only after setup and live target ownership have all released.
This setup exercises native submission and receipt handling. It does not prove stable sampled capacity.
The latest prepared comparison passes zero percent and fails treatment lease capacity at 2,043,571 ns of peer debt.
Treatment completes 13 journals. Both live sessions close with no pending ownership.
The [retained evidence](../../reliability/features/native-signal/timer-sampled.md) includes every preceding failure and its source manifest.

## Initial interval comparison

`LinuxFastTimer` uses the prepared workload with a 10 µs initial timer interval.
`LinuxPreparedTimer` retains the default 1 ms initial interval.
Both use 10 µs continuation intervals and the same measurement and failure bounds.
The parsed managed values are `1ms` and `10us`.
Other strings fail before native configuration.
The native configuration also rejects unsupported values and changes after registration.

Shorter initial delivery does not extend public support or prove safety during runtime transitions.
The comparison must retain failed measurements and cleanup outcomes.

`LinuxFastTimerCapacity` uses the same managed and native preparation with the 10 µs interval but disables controller phase capture.
Its `controllerPhases` and `controllerPhasesOverwritten` fields are null.
Samples, journals, periodic boundaries, new peaks, and failures remain available.
The capacity mode changes diagnostic cost only. It does not change accounting or timing bounds.
The [first retained capacity run](../../reliability/features/native-signal/timer-sampled.md) passes both percentages for two-second windows.
Treatment completes 2,360 journals and peaks at 999,998 ns of peer debt.
Both sessions close without pending ownership.
This single run does not establish repeatability, forced-GC capacity, or prediction accuracy.

## Sampled forced GC

`LinuxFastTimerGc` uses prepared 10 µs capacity mode without phase capture and adds a real GC worker.
That worker allocates 1 MiB, forces a blocking compacting generation-2 collection, then waits 20 ms between calls.
The controller releases the GC worker when measurement starts.
At least one complete collection with a positive collection index must fit inside the measurement window.
Each collection call must finish within 250 ms.

Shutdown requests GC stop before it collects pending native journals.
The workload reads GC observations only after the GC worker joins.
A failed join, worker error, missing reach, or collection bound violation fails the case.
Measurement-window reach does not prove overlap with any particular timer child or command.

Sampled forced-GC capacity remains unverified.
The [retained comparison after the retirement fix](../../reliability/features/native-signal/timer-sampled.md#sampled-forced-gc-after-the-retirement-fix) fails treatment at 2,499,975 ns of peer debt.
Both native sessions close with no pending ownership. Both GC workers join and meet the measurement-window reach condition.
The debt limit remains 2,000,000 ns.

## Scheduler capture

`LinuxFastTimerScheduler` adds scheduler observations to prepared forced-GC capacity mode.
The optional `ISchedulerEventSession` factory separates workload control from platform capture.
`LinuxSchedulerEventSession` supplies the current Linux x64 implementation.

`schedulerCaptures` contains three entries: controller, selected target, and peer target.
Each entry records process and thread identities, native clock brackets, switch records, loss, and capture failures.
`schedulerTiming` identifies an enabled diagnostic. `schedulerStopped` identifies completed scheduler shutdown.

Shutdown stops scheduler collection before it permits target retirement.
Unresolved disposal retains target ownership and prevents native close. Capture failures and missing or lost records fail the workload.
The mode retains existing debt and timing limits. Collector threads and allocations add diagnostic cost.
Scheduler overlap does not identify the cause of descheduling or prove prediction accuracy.
The [first retained scheduler capture](../../reliability/features/native-signal/timer-sampled.md#first-scheduler-capture) fails treatment debt while all streams report zero loss.
Peer preemption overlaps almost the entire first arm-to-handler interval in that run.
`TimerSchedulerCaptureGroup` controls registration, capture completion, and the target retirement gate.
Its portable fault checks verify that unresolved disposal blocks retirement and that capture errors survive successful cleanup.
Duplicate or late registration fails before it can replace a collector.
Repeated shutdown returns the retained result without retrying collector disposal.
These checks do not inject native perf failures. Observer-cost measurements remain incomplete.

## Scheduler cost comparison

`LinuxTimerSchedulerCost` runs eight pairs at zero percent with forced GC.
Each pair contains scheduler capture enabled and disabled. Seed 20261014 selects their order.
The runner retains every case and fails if any case fails. It does not repeat failed cases.

`measurementProgress` contains completed checksum batches for the selected and peer workers before shutdown.
`progressCapturedBeforeTimestamp` and `progressCapturedAfterTimestamp` bracket both counter reads.
Counters start at zero before measurement release. Their snapshot can include work after `measurementFinishedTimestamp` within the capture brackets.
The final `progress` field also includes shutdown work.

For each worker, divide its count by the elapsed time from measurement start to each capture bracket.
These two rates bound the throughput represented by that counter snapshot.
Use `timestampFrequency` for unit conversion. Reject missing snapshots, failed cases, or invalid clock ordering.
The comparison includes sampling and forced GC but no live delay commands. It does not measure treatment overhead or process CPU cost.
The [first retained comparison](../../reliability/features/native-signal/timer-sampled.md#scheduler-capture-throughput-comparison) completes all 16 cases.
Its paired throughput changes range from −16.43% to +34.88%. The variability does not establish a small overhead bound.

See [the run procedure](../How_Tos/run-sampled-timer.md) and [controller contracts](timer-service-controller.md).
