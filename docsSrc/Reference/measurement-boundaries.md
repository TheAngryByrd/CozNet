# Measurement boundaries

`MeasurementWindowCapture` retains immutable start and end readings for experimental workloads.
The contract uses `TimeProvider` and contains no platform APIs.
The sampled timer and [matched physical workload](matched-physical.md) use this contract.

## Boundary fields

| Field | Meaning |
| --- | --- |
| `BeforeTimestamp`, `AfterTimestamp` | Clock readings around the capture operation. |
| `TimestampFrequency` | Positive ticks per second for the capture clock. |
| `Accounting.Global` | Global delay already applied to the managed ledger, in nanoseconds. |
| `Accounting.Credit` | Cumulative accepted selected sample credit, in nanoseconds. |
| `Accounting.AcceptedSampleSequence`, `Accounting.Epoch` | Last accepted sample sequence and current sampling epoch. |
| `Accounting.Selected`, `Accounting.Peer` | Separate worker readings. |
| Worker `Progress` | Completed checksum batches. |
| Worker `ReleasedWait` | Confirmed wait from released service journals, in nanoseconds. |
| Worker `ActiveWait` | Confirmed wait from the currently owned journal prefix, in nanoseconds. |
| Worker `Pending` | Command and generation for retained service ownership, or null. Generation zero retains unresolved submission identity. |

Readings have no mutable arrays. Later ledger settlement, progress, or release cannot change a published boundary.
Counters must be nonnegative. Positive active wait requires pending ownership. A pending command must be nonzero.
Global delay, credit, sample sequence, epoch, progress, and released waits cannot decrease between start and end.
Active wait can decrease when a journal releases.

## Capture states

| State | Meaning |
| --- | --- |
| `NotStarted` | No start attempt. |
| `Active` | Start capture succeeded. |
| `Complete` | End capture succeeded. |
| `StartFailed` | Start capture or its release callback failed. No start boundary is published. |
| `FinishFailed` | End capture failed. The start remains available, but no end boundary is published. |

`Complete` describes capture completion. It does not establish experiment validity.

`Begin` validates the initial readings before invoking the worker-release callback.
The start bracket contains both the initial read and release callback.
`Finish` captures the end once. Repeated calls after completion or failure do not read counters or clocks again.
The first failure propagates to the caller. Failed capture states remain failed.
A second start attempt is invalid. An end attempt without a completed start is invalid.
Clock regression within a bracket or between the start and end brackets fails capture.

## Sampled timer integration

`measurementCapture` contains `status`, `start`, and `end`.
The start bracket contains sampler enablement, the nominal start timestamp, worker release, and optional GC-worker release.
Initial counter readings precede those operations.
At measurement end, the controller stops accepting sample credit and prevents sampling resume before capturing the endpoint.
It freezes the endpoint before shutdown drains, cancellation, retirement, and final settlement.
The legacy `measurementProgress` and progress timestamp brackets come from the frozen end boundary.
A failed end capture leaves these legacy fields unavailable.

Within a read, the timer workload copies controller accounting, reads global delay, then reads selected and peer worker counters.
The selected worker is read before the peer. Controller-owned credit, sample sequence, and epoch complete the readings.
The bracket contains this sequence. It does not make the two worker reads simultaneous.

## Measurement limits

These boundaries retain controller-accounted state. They do not capture every native wait that has occurred by the endpoint.
Unreceived sample periods and journal suffixes can cross the boundary.
Their contribution needs an explicit uncertainty rule before prediction analysis can use the records.
The nominal measurement timestamps remain separate from the capture brackets.
Neither later shutdown accounting nor a later successful cleanup can replace the frozen endpoint.
The workload still reports `predictionAccuracyEstablished = false`.

## Frozen timer prefix

Sampled timer boundaries include `Accounting.Timer`. Other producers can leave this optional field null.
The field contains `ReleasedJournalCount`, `Selected`, and `Peer`.
The count identifies the prefix of the controller's archived journal list at capture time.
Each worker retains its cumulative `ReleasedWait` and optional `Pending` record.
Pending records retain lease, command, generation, accepted child count, confirmed wait, and the last accepted native capture timestamp.
They also retain ledger settlement, cancellation intent, release-attempt state, and any controller failure.

The snapshot contains copied scalar values. Later polling, cancellation, or release cannot replace its accepted prefix.
Worker wait totals and pending identities must agree with the snapshot.
The end cannot decrease the archived journal count or change whether timer evidence is present.
The snapshot performs no native read. It records controller knowledge inside the existing endpoint bracket.
It does not establish which unreceived native children finished before the endpoint.
Earlier captures without this field cannot recover an exact accepted prefix from a later shutdown journal.
The [native capture evidence](../../reliability/features/native-signal/frozen-timer-prefix.md) retains a frozen empty prefix and its later deferred child.

See [the sampled timer reference](sampled-timer.md) and [the Coz port decision](../Research/coz-port-and-il-rewriting.md).
The [retained evidence](../../reliability/features/native-signal/measurement-boundaries.md) includes portable checks and failed live treatments.
