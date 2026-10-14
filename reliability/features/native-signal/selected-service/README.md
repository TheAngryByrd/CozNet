# Selected native service accounting

The selected adapter passes pure checks on Windows and Linux.
The [live Linux run](summary.json) also passes normal settlement and deliberate late-receipt rejection through native slot 0.
All five process outcomes exit zero. The [native checks](native-checks.log) pass 6/6.
The [manifest](manifest.json) retains source and binary hashes from the live run.

## Pure cases

| Scenario | Required result |
| --- | --- |
| `ActiveSelectedJournalsRequireSubmittedState` | An empty active journal preserves submission. Reserved and already bound states fail. |
| `SelectedPartialServiceStopsAfterOneChild` | Measured, deferred, and interrupted outcomes stop after one child. Partial debt remains unpaid. Overshoot publishes in full. Settled cursors reject replay. |
| `InvalidSelectedJournalsRetainSubmission` | Invalid identities, commands, budgets, errors, phases, cancellation, counts, aggregates, and capture clocks retain ownership. |
| `SelectedReceiptAndOutcomeBounds` | Late receipt, failed clock, invalid ordinal, request mismatch, zero sequence, ineligible address, native result mismatch, and unordered phase clocks fail. |

Two extra assertions for outstanding count and snapshot capture order were added after the live run.
The expanded pure suite passes on both platforms.
Only the check source changed after that run. The retained live evidence predates those assertions.

## Live fixtures

The fixture uses synthetic accounting to publish 110,000 ns of global delay.
A new sampled registration inherits that value.
A synthetic peer wait advances global delay to 140,000 ns at the next ordinary check.
The sampled registration then reserves a bounded 30,000 ns request.
The real native owner submits that request to slot 0.
The registered managed target has an ineligible instruction range, so its acknowledgment is deferred and measures zero wait.

| Fixture | First managed receipt upper bound | Accounting result | Native teardown |
| --- | ---: | --- | --- |
| [Normal](receipt.json) | 670,285 ns | Settled once. Zero pending requests. All 30,000 ns remains unpaid. | Target joined and session closed. |
| [Late](late.json) | 151,235,763 ns | Rejected. One request remains submitted with no bound native sequence. | Quiescent native command released, target joined, and session closed. |

The late case passes only because it requires rejection under the unchanged 100,000,000 ns receipt gate.
Native teardown cannot turn that rejected observation into a valid profiling result.

```powershell
dotnet run --project build -t ServiceAccountingVerify
dotnet run --project build -t LinuxSelectedServiceInteropVerify
```

The Linux runner uses isolated processes with a 30-second watchdog.
This verifies selected journal parsing, sequence binding, settlement, and target lifetime.
It does not establish sampled controller integration, measured native intervention in eligible managed code, capacity, or prediction accuracy.
See [the selected adapter contract](../../../../lode/profiling/service-selected-adapter.md).
