# Controller dispatch timing

## Contract and controlled evidence

The optional observer records managed submission, action entry, action exit, and caller observation.
It preserves the dispatched result and the original dispatch exception.
The default path calls the existing worker directly and creates no observer.
The observer uses managed synchronization and `Stopwatch`. It has no Linux-only dependency.

| Requirement | Check |
| --- | --- |
| Retain exact phases, result, and request identity | `CompletedDispatchRetainsThreePhasesAndIdentity` |
| Preserve queued failure and ignore late execution | `FailuresPreserveExceptionAndIncompleteSnapshot` |
| Bound recent history while retaining maxima | `RecentHistoryIsBoundedAndMaximaSurviveEviction` |
| Select maxima for the correct phase | `DistinctPhaseMaximaUseTheirOwnIntervals` |
| Preserve active timeout without late completion | `ActiveTimeoutRetainsStartedWithoutLateCompletion` |
| Preserve real worker fault wrapping and inner exception | `WorkerFaultKeepsItsExistingExceptionShape` |
| Parse disabled and invalid activation values | `DisabledAndInvalidModesDoNotCreateCapture` |
| Reject a foreign caller before dispatch or clock reads | `ForeignThreadCannotDispatchOrChangeEvidence` |
| Reject inconsistent timestamps, outcomes, counts, and maxima | `test_dispatch_phases_and_maxima_are_consistent` |
| Preserve missing endpoints on failed dispatch | `test_incomplete_dispatch_keeps_failure_without_inventing_action` |
| Require null observations when disabled | `test_disabled_dispatch_timing_has_no_observations` |
| Include an evicted failure maximum in global totals | `test_evicted_failure_maximum_contributes_to_totals` |
| Require a queue maximum for an evicted started call | `test_evicted_started_failure_requires_queue_maximum` |
| Require unretained incomplete calls to also be failures | `test_unretained_incomplete_calls_must_also_be_failures` |

The managed checks pass through `--verify-bound-cooperative-managed` on Windows and Linux with .NET 10.
The Python suites pass 21 peer and 58 selected checks.
The three eviction/count regression tests fail without their respective guards.
Outcome-reader fixtures reject missing, malformed, and mismatched activation values.
The live runner passes three managed groups and thirteen native checks before capture.
The replay fix and extra controlled checks postdate live capture. The runtime observer and workload remain unchanged.
The final replay verifies all six captured records and preserves the one-CPU treatment failure.

## Unrestricted capture

Path: `artifacts/controller-dispatch/20261015T220857533-949f2434`.
Settings: owner draining, per-sample service, qualified-sample cadence, 50,000 requested batches, pause timing, child CPU, and dispatch timing.
All three cases pass. Zero speedup and treatment each complete 50,000 batches.
The treatment records 29,456 dispatches, no dispatch failures, and no incomplete actions.
Session creation supplies its longest total, queue, action, and return intervals.
These maxima do not describe steady-state payment.
Replay verifies all three records. The runner reports unchanged source and binary hashes.

## One-CPU capture

Path: `artifacts/controller-dispatch-single/20261015T221111890/01-owner`.
The declared single trial uses the baseline binaries and guest CPU 0 with the same settings.
The CPU restriction also changes processor availability for runtime policy.
The runner retains its plan, command arrays, affinity evidence, input hashes, raw records, and replay results.
Zero speedup completes 50,000 batches. Expected invalidation passes after selected payment.
Treatment fails after 19,944 batches. It records 142,948 dispatches, one failed dispatch, and no incomplete actions.
Replay verifies all three records without promoting the failed treatment.
Input hashes remain unchanged. No unchanged failed case was retried.

The failed peer request has command 154, generation 154, and budget 499,864 ns.
Its diagnostic journal retains a 6,167,752 ns child. That child exceeds the 5 ms handler bound and does not authorize payment.
The receipt-read action rejects the journal before the managed receipt-interval parser runs.
`receiptIntervalRejection` remains null.

Dispatch sequence 142,943 identifies `PeerRead` for command and generation 154.
Its queue, action, and return brackets are 0.024660 ms, 0.316728 ms, and 115.427269 ms.
The action exits 33.285584 ms after managed request start.
The caller observes failure 148.712853 ms after request start.
The outer failure capture starts 1.893341 ms after that observation.
The longest queue record is sequence 48,525: `PeerHeader`, command and generation 55, with 49.894854 ms before action entry.

The return bracket includes worker fault publication, exception handling, scheduling, and observer work.
It cannot identify a scheduling cause, isolated exception cost, or native waiting duration.
Native child durations and managed dispatch brackets use separate clocks. This report does not place them on one timestamp axis.
The native wait failure and later caller-observation interval are separate observations.
The result cannot reconstruct the earlier rejected managed receipt endpoint.
Local artifacts are not distributed with this document.

## Accepted limits

Each observed call allocates timestamp state, a callback, and a retained record, and takes locks and clock readings.
No enabled/disabled overhead comparison was run. The capture cannot establish uninstrumented latency or stable capacity.
`Returned` precedes journal and maximum updates, so the total bracket excludes that final observer work.
The injected clock must be monotonic and nonthrowing. Runtime capture uses `Stopwatch.GetTimestamp`.
Allocation failure and a throwing test clock are outside the exception-preservation contract.

The 128-record history can lose a request's submission during frequent header polling.
Independent maxima survive eviction but can belong to session creation or cleanup.
Replay checks retained records, sequence, counts, and maxima. It cannot independently reconstruct evicted records.

Replay checks that operation labels are valid and request identifiers are unsigned.
It does not bind them to accepted payments or failure boundaries.
Source review verifies the workload's operation labels and selected/peer identity mapping.
The controlled checks do not detect a swapped workload label or swapped identity mapping.
These fields remain diagnostics and do not authorize payment.

The lock protects cross-thread timestamp visibility during timeout capture.
The controlled concurrency checks exercise late completion but do not prove every scheduler interleaving.
All payment, handler, receipt, and debt bounds remain unchanged.

See [the reference](../../../docsSrc/Reference/cooperative-managed.md#controller-dispatch-timing),
[capture procedure](../../../docsSrc/How_Tos/run-bound-selected-cadence.md#capture-controller-dispatch-intervals),
and [interpretation](../../../docsSrc/Explanations/owner-drain-capacity.md#controller-action-exit-is-not-caller-observation).
