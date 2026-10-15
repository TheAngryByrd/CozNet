# Timer service controller draft

`TimerServiceController` coordinates reserved timer leases through `ITimerTransport`.
It is an internal prototype in `tests/CozNet.LinuxPerf`.
It does not extend the public serial engine.
Twenty-one portable controller checks run through `TimerLayoutVerify`.
The [sampled timer workload](sampled-timer.md) connects real CPU samples to this controller.
Its first treatment fails the lease capacity bound. Sampled capacity remains unverified.

## Transport and slots

`ITimerTransport` exposes submission, snapshot probing, journal reading, cancellation, and release after a retired copy.
It also exposes the last submission result and raw capture.
`NativeTimerClient` supplies the Linux transport.
The controller accepts a `TimeProvider` and does not call Linux APIs directly.
This boundary permits portable checks with a fake transport. It does not establish support on another platform.

Slot `0` belongs to the selected ledger thread. Slot `1` belongs to the peer ledger thread.
The controlled `TimerManagedWorkload` fixture uses a different slot assignment.
The constructor requires distinct ledger threads and a nonzero, nonwrapping instruction range.

## Operations

| Member | Current behavior |
| --- | --- |
| `Submit` | Accepts a reserved lease, records sent accounting, owns the slot, and submits the native command. |
| `Poll` | Reads changed journal prefixes, applies timer accounting, and releases only after retired visibility and ledger settlement. |
| `Cancel` | Records cancellation intent before the native cancellation call. |
| `HasPending` | Reports whether the controller owns the slot. |
| `ConfirmedWait` | Reports accepted wait for a pending selected or peer lease. Returns zero after release. |
| `ReleasedWait` | Reports cumulative confirmed wait for successfully released journals in the specified slot. Diagnostic failure does not reset this total. |
| `Journals` | Contains operations whose native release succeeded. |
| `PendingJournals` | Contains operations that still own their slots, including failed operations. |
| `CaptureAccounting` | Copies the archived journal count, released waits, and accepted pending prefixes without reading the transport. |

`Submit`, `Poll`, and `Cancel` update the caller's accounting through `ref SignalAccounting`.
The caller must reserve each lease before submission.
The controller rejects a lease for another slot or a submission to an occupied slot.

## Settlement and release

`TimerAccountingAdapter` validates each accepted prefix and applies ledger settlement.
Ledger settlement and native release are separate operations.
A quiesced journal can settle accounting while the controller retains slot ownership.
Retired visibility permits release only after settlement and a stable journal check.
The controller marks release as attempted before it calls the transport.
A failed release retains ownership and blocks automatic release retries.
Successful release updates `ReleasedWait` before the diagnostic phase closes.
The workload uses this total for debt checks, snapshots, and output.

## Failure records and limits

Submission failure retains sent accounting and slot ownership.
The pending journal records the available submission status, generation, and exception.
Read and validation failures retain attempted evidence where available.
Each read clears the previous attempt from the pending journal.
A failed read retains a new raw capture only if the transport published one during that read.
The last accepted observation remains separate from failed attempts.

`CaptureAccounting` returns immutable scalar records. The caller must serialize it with other controller operations.
An unresolved submission retains ownership with zero accepted children and no accepted capture timestamp.
A failed release retains its settled prefix and release-attempt state without increasing the archived journal count.
The [measurement boundary](measurement-boundaries.md) stores this snapshot before shutdown.

An `AggregateException` retains both the operation error and a diagnostic closing-clock error when both occur.
Phase closure consumes the active phase before reading the closing clock. A failed clock does not prevent later phases.
If phase closure fails after native release, the archived journal retains that failure.
The released wait remains recorded, slot ownership stays released, and the controller does not repeat native release.

Cancellation intent remains set if native cancellation throws.
Another `Cancel` call reports unresolved cancellation without retrying that native operation.
Repeated cancellation returns normally only after the native cancellation call succeeds.
Native recovery from a failed cancellation remains unverified.

An unchanged header can suppress a journal read. Only a nondecreasing capture timestamp can differ.
Changed identity, totals, state, or error fields cause another read and accounting validation.
Retired-journal comparison uses receipt values, including the timing bound, rather than timing-object identity.
No sampled capacity or prediction-accuracy claim follows from this draft.

## Portable checks

| Check | Required result |
| --- | --- |
| `FrozenLedgerSeparatesSettlementFromNativeRelease` | Accepted wait remains separate from local credit. Failed native release cannot count settled wait as unsettled again. |
| `FrozenLedgerRejectsMismatchedControllerState` | Service mismatches, missing evidence, duplicate threads, and changed worker identities reject boundary capture. |
| `PartialPrefixDoesNotPayTwice` | Repeated peer prefixes pay once. Cancellation retains unpaid debt. |
| `SelectedPrefixPreservesUnpaidDebt` | Selected prefixes pay once. Cancellation preserves remaining selected debt after published peer overshoot. |
| `EquivalentRetiredReceiptReleases` | Quiescence settles the ledger. Equivalent retired receipt values permit release without paying deferred debt. |
| `ChangedHeaderCannotHideFailure` | Changed error, transport error, generation, or aggregate fields fail without settlement or release. |
| `ReadFailureDoesNotReuseRawCapture` | Failed reads cannot relabel an old capture as new evidence. |
| `CancellationFailureRemainsVisible` | Failed native cancellation retains intent and ownership. Repeated cancellation reports unresolved failure. |
| `ReleaseFailureRetainsOwnership` | Failed release prevents slot reuse, repeat release, and repeat settlement. |
| `SubmissionFailureRetainsSentLease` | Failed submission preserves sent accounting, native status, and generation. |
| `WrongSlotDoesNotSubmit` | A mismatched lease fails before native submission or accounting mutation. |
| `PhaseCaptureKeepsServiceAfterIdlePolls` | Discarded drains and unchanged polls do not consume phase capacity. Later service and failed submission retain records. |
| `UnavailableTargetRetainsGrowingDebt` | An empty active journal retains new debt across the inclusive 2 ms gate. Cancellation releases ownership without debt forgiveness. |
| `DelayedDeliveryCreditsOnlyConfirmedWait` | A long arm-to-handler interval earns no credit. Only the later measured child wait settles once. |
| `FrozenAccountingRetainsAcceptedPrefixAcrossRelease` | Later release cannot replace frozen prefix evidence or its serialized values. |
| `FrozenAccountingRetainsUnresolvedOwnership` | Failed submission and release preserve their distinct prefix and ownership states. |
| `FrozenAccountingExcludesRejectedObservation` | A rejected observation cannot replace accepted prefix values or mutate an earlier snapshot. |
| `CombinedOperationAndPhaseFailuresRemainVisible` | Submission, read, or accounting failure survives a simultaneous closing-clock failure. Later phases remain usable. |
| `ClosingClockFailurePreservesReleasedWait` | Diagnostic failure after native release preserves committed wait and archived failure evidence without duplicate release. |
| `ReleasedWaitAccumulatesAcrossGenerations` | Two generations accumulate released wait in one slot. The other slot and later idle polls cannot alter that total. |
| `CancellationCanObserveCompletedOrDeferredJournal` | Cancellation intent permits a raced completed or deferred terminal journal while preserving exact wait accounting. |

These checks use a fake transport. They do not establish native syscall behavior or sampled prediction accuracy.

An optional `ControllerPhaseCapture` records native submission and changed journal processing.
Submission timing starts after the controller owns the lease and records sent accounting.
Journal timing starts after the probe detects a changed header.
It includes journal reading, validation, ledger settlement, and any native release.
An unchanged probe produces no phase record. Probe cost remains outside these intervals.

See [timer accounting](timer-accounting.md), [timer interop](timer-interop.md), and [worker lifecycle](timer-worker-lifetime.md).
