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
