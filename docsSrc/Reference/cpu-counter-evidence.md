# CPU counter evidence

The experimental Linux CPU event requests `PERF_SAMPLE_READ` with `ReadFormat = 0`.
Each sample retains an unsigned cumulative task-clock counter in `CumulativeCpuNanoseconds`.
The ring reader requires the 48-byte layout and rejects counter regression before sample publication.
Nominal periods remain in `PeriodNanoseconds`. The counter does not replace them.
Calls to `CpuSample.Create` without a cumulative count retain null.
At the same ring size, 48-byte records reduce sample capacity by one sixth compared with 40-byte records.
Counter collection and transition capture add work. Their observer cost has no verified bound.

## Counter source

`ICpuEventSession.CounterEvidence` exposes the optional portable `ICpuCounterEvidenceSource` capability.
Backends without this capability return null.
`ReadCounter()` returns these fields:

| Field | Meaning |
| --- | --- |
| `SourceIdentity` | Identity of one event instance |
| `Epoch` | Number of successful enable operations |
| `Enabled` | Last recorded event state |
| `Before`, `After` | Managed timestamps around the counter read |
| `TimestampFrequency` | Managed timestamp units per second |
| `CpuNanoseconds` | Unsigned cumulative task-clock counter, or null after a failed read |
| `Error` | Failure details, or null |

The Linux counter includes task-clock CPU while the event is enabled.
It does not count CPU while the event is disabled.
Kernel execution can contribute to the counter even though kernel-mode samples are excluded.
The backend never resets this event counter.

## Transition journal

The Linux session records enable and disable operations in a portable `CpuCounterJournal`.
Each transition retains its epoch, requested direction, completion state, timestamp bracket, frequency, and error.
A native failure preserves the previous event state.
A clock failure after native success preserves the changed state and marks the transition with an error.
The journal holds at most 65,536 transitions. Capacity exhaustion prevents the next native operation.
Control operations and counter reads must be serialized.

`DescribeCounters()` returns an immutable snapshot, including transitions, the last reading, and failure flags.
`FailedReads` and `FirstReadFailure` preserve failures even after later reads succeed.
`Overflowed` marks capacity exhaustion.
`UnrecordedCleanup` marks best-effort disposal outside the transition journal.
When that flag is true, `Enabled` does not establish the final native state.

## Frozen-window readings

The sampled timer result includes `cpuCounters`.
It records `startBefore` and `startAfter` around the start endpoint.
It records `endBefore` and `endAfter` around the end endpoint.
Shutdown readings do not replace these four values.

`EnabledCpuCountRange.Parse` requires four successful readings from the same source.
Their brackets must lie on the declared sides of the frozen endpoints.
Counts, epochs, and timestamps cannot regress. Timestamp frequencies must agree.

| Range field | Formula |
| --- | --- |
| `Lower` | `endBefore.CpuNanoseconds - startAfter.CpuNanoseconds` |
| `Upper` | `endAfter.CpuNanoseconds - startBefore.CpuNanoseconds` |

This is a range for the observed enabled-event counter delta.
It does not establish selected-method CPU, clock accuracy, or lifecycle validity.
`rejection` records why a range could not be parsed.
`source` retains the journal snapshot separately.
`selectedCpuBoundEstablished` and `missingSampleTailBoundEstablished` remain false.

`--verify-cpu-counters` runs ABI, ring, journal, and range checks without Linux syscalls.
`--verify-timer-layout` includes these checks.

See [CPU periods and missing tails](../Explanations/cpu-counter-attribution.md) for attribution limits.

## Offline reconciliation

`python native/AnalyzeCpuCounters.py capture.json report.json` parses one sampled timer capture.
The portable analyzer requires Python 3.9 or later. It uses no native platform APIs.
It creates the report exclusively and rejects an existing output path.
The report retains SHA-256 hashes of the input bytes and analyzer source.
Exit code zero means counter reconciliation succeeded. It does not mean the workload succeeded.

The parser reconciles endpoint readings, transition state, final count, sample order, and record totals.
It rejects failed reads, incomplete journals, sample loss, and inconsistent accepted-sample sequences.
The final snapshot can equal the disabled `endAfter` reading.
A counter cannot advance between readings in the same disabled epoch.
Missing legacy counter evidence produces a rejected diagnostic.

| Report field | Meaning |
| --- | --- |
| `reconciled`, `reasons` | Counter parsing result and rejection details |
| `enabledCountRange` | Recomputed frozen-window counter range |
| `countBeforeFirstAccepted` | Counter range before the first accepted sample. Null when none exists. |
| `countAfterLastAccepted` | Counter range after the last accepted sample. The whole enabled range when none exists. |
| `disabledElapsed` | Disabled interval ranges clipped to the uncertain measurement endpoints |
| `recordedWorkloadFailures` | Workload failures retained independently of counter reconciliation |
| `nativeSessionClosed` | Recorded native closure state |

Disabled elapsed bounds use managed timestamps. Nanosecond conversion rounds the lower bound down and the upper bound up.
Sample ordering uses controller receipt timestamps. It does not compare perf timestamps with managed timestamps.
Shutdown samples do not replace the frozen-window suffix.
Selected CPU, missing-sample tails, disabled CPU, and prediction accuracy claim flags remain false.

The matched analyzer embeds this report as `cpuCounterDiagnostics` for profile cases, including failed cases.
See [matched prediction diagnostics](matched-prediction.md) for eligibility rules.
