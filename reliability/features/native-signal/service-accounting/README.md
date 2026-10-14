# Native service journal accounting

The managed adapter passes pure checks on Windows and Linux.
The Linux interop fixtures also pass through the adapter into the real immutable accounting model.
The [summary](summary.json), [manifest](manifest.json), and [outcomes](outcomes.json) retain the run evidence.

## Pure checks

| Scenario | Required result |
| --- | --- |
| `RepeatedPrefixAndNewCreditSettleOnce` | Repeated snapshots pay no extra wait. A 115,000 ns settlement leaves 35,000 ns of newer credit unpaid. |
| `InvalidSuffixPublishesNoPartialState` | A later invalid child publishes none of that batch's temporary state. The original state accepts the corrected batch. |
| `CancellationRequiresIntentAndKeepsUnpaidDebt` | Cancellation requires managed intent. Empty cancellation leaves 110,000 ns unpaid. A 40,000 ns prefix leaves 70,000 ns unpaid. |
| `RejectsForeignAndLateObservations` | Foreign commands, incorrect totals, late receipts, and premature completion fail. |
| `BackendOutcomeRulesRemainExplicit` | An injected backend interruption code preserves its 12,345 ns partial wait. The adapter contains no OS error constant. |
| `InvalidHeadersKeepDebtReserved` | Native errors, transport errors, invalid phases, rejected records, unsolicited cancellation, and active terminal children retain the unpaid reservation. |
| `RegressedSnapshotKeepsAcceptedPrefix` | A regressed snapshot cannot change an accepted prefix. Later cancellation settles only the original 40,000 ns. |
| `ZeroWaitPrefixRejectsStaleState` | A zero-wait prefix rejects an old accounting state and a different native sequence. The correct prefix accepts its next child. |

The last three scenarios were added after the retained live run.
The expanded pure checks pass on Windows through `ServiceAccountingVerify` and in the Linux SDK 10.0.100 container.
The retained manifest describes the earlier live run, not these additional test cases.
The adapter now compares journal count and sequence as well as measured wait.
The retained live results predate this stale-state fix.
The [subsequent run](stale-state-fix/summary.json) verifies live interop after the fix.
All five processes exit zero, including six native checks and the expanded pure checks.
Normal receipt takes 1,076,742 ns and settles the reservation once.
The deliberately late receipt takes 151,455,063 ns and retains the accounting reservation.
Both fixtures join their targets and close their native sessions.
The later cancellation fix applies the same progress check before cancellation.
Its zero-wait and foreign-sequence regression checks pass on Windows and Linux.
The retained live run predates that cancellation change.

The first scenario also rejects replay after settlement, stale accounting state, and changes to accepted receipt bounds.
The commands are:

```powershell
dotnet run --project build -t ServiceAccountingVerify
dotnet run --project build -t LinuxServiceInteropVerify
```

## Live journal checks

The fixture uses a synthetic selected sample to establish 110,000 ns of peer debt.
It reserves that debt in `SignalAccounting` and submits the same lease identity, budget, and command to the native service.
The target is a real managed thread registered with an ineligible instruction range.
The real native acknowledgment is therefore deferred and measures zero wait.

| Fixture | First receipt upper bound | Ledger result | Native teardown |
| --- | ---: | --- | --- |
| [Normal](receipt.json) | 1,003,856 ns | Settled once. No pending reservation. The original 110,000 ns remains unpaid. | Target joined and service closed. |
| [Late](late.json) | 151,471,208 ns | Rejected. The original reservation remains pending and publishes no measured wait. | Quiescent native lease released, target joined, service closed. |

The late fixture passes only because it requires accounting rejection.
Native teardown does not turn the rejected observation into a valid experiment result.
The original 100,000,000 ns receipt gate remains unchanged.

The [native checks](native-checks.log) also pass 6/6.
The run records unchanged source and binary hashes.
All five recorded process outcomes have exit code zero.

## Limits

This verifies journal translation and accounting transitions.
It does not connect the adapter to live CPU sample collection or the sampled controller.
It does not verify measured service in managed CPU code, GC stress, sustained capacity, or physical prediction accuracy.
Snapshot-copy and adapter allocation costs remain unmeasured.

See [the adapter contract](../../../../lode/profiling/service-accounting-adapter.md) and [the native debt plan](../../../../lode/plans/native-debt-service.md).
