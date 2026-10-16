# Finite cooperative measurement

`COZNET_FINITE_MEASUREMENT=1` enables frozen measurement in the bound selected-payment workload.
The runner option is `-FiniteMeasurement`. The default is disabled.
The mode requires owner draining and an explicit selected cadence.
Other activation values except `0` reject the run.

## Boundaries

The selected owner prepares a separate capture before source creation.
It captures the actual start after source setup and before its application loop.
The peer already runs on its dedicated worker. Each boundary records both progress counters.
The end freezes after the selected loop and before source closure or final payment cleanup.
On failure, the owner attempts to freeze before diagnostic capture and cleanup.
An excluded callback can stop the producer before that failure. This earlier pause is not the cleanup boundary.
Each end attempt closes acceptance permanently. A failed capture cannot use a later cleanup snapshot.

After freezing, source closure retains the remaining samples as diagnostic evidence.
Those samples do not enter experiment credit. Cleanup can settle the existing peer request but cannot reserve another experiment lease.
`finalDebt` remains a later cleanup observation. It is not an estimator input.
Positive-speedup completion requires positive accepted payment from both workers.
It does not require cleanup to pay all residual peer debt. Residuals keep the measurement unqualified.

## Records

| Field | Meaning |
| --- | --- |
| `finiteMeasurementEnabled` | Parsed activation Boolean. |
| `measurement.Window` | One start and one end, with clock brackets and capture status. |
| `measurement.Window.Preparation` | Separate preparation window, excluded from estimator inputs. |
| `Accounting.Cooperative` | Released journal count and selected/peer ownership at that boundary. |
| Ownership `Lease`, `Command`, `Generation`, `Budget` | Managed lease and native request identity. Generation zero retains unresolved submission. |
| Ownership `AcceptedChildren`, `ConfirmedWait` | Accepted native prefix at capture time. |
| Ownership `LedgerSettled` | Whether accepted wait has already entered local ledger credit. |
| Worker `Ledger` | Global delay, local credit, phase, and separate unsettled service prefix. |
| `measurement.CaptureFailure` | First preparation or capture error. |
| `measurement.Failure` | First workload or final verification error. |
| `measurementAssessment` | Invalid or unqualified outcome, reasons, and optional descriptive estimate. |

Cooperative capture uses no timer snapshot.
An accepted service prefix can remain unsettled in the ledger.
A settled service can retain native ownership until release. Capture does not count that wait as unsettled again.

## Descriptive estimate

Elapsed bounds use the outer and inner clock brackets.
Effective duration subtracts the frozen global-delay delta from each elapsed bound.
Selected and peer progress use their respective deltas from the same two boundaries.
The estimator never substitutes summed worker waits for global delay.
Cleanup cannot replace these values.
Replay binds selected progress to completed selected work and frozen global delay to later ledger observations.
It also checks released journals, accepted prefixes, and applicable ownership, residual, and source-tail reasons.

## Qualification limits

The [cooperative physical route](cooperative-physical-work.md) shares checksum work with this sampled path.
Controlled physical execution passes on Windows x64 and Linux x64. The first matched Linux block completes and remains unqualified.

A failed workload, failed capture, rejected source, failed cleanup, or insufficient measurement reach produces `invalid`.
An invalidation check can pass its intended rejection test while its measurement remains invalid.
Other completed records remain `unqualified`.
Pending native ownership, ledger residuals, and source tails remain explicit reasons.
Successful cleanup cannot qualify an unknown suffix at the earlier endpoint.
Sample membership at the boundary and injected-instruction attribution remain unresolved.
The mode does not establish physical prediction accuracy or stable service capacity.
The existing debt, lease, child, handler, and receipt limits remain unchanged.

The first Linux capture retains a pending peer request at the endpoint.
Cleanup later adds 499,989 ns to peer local credit without replacing the frozen record or publishing further global delay.
Its zero-speedup and treatment measurements remain unqualified. The intended invalidation remains invalid.
This capture establishes the observed freeze-before-cleanup ordering, not prediction accuracy or stable capacity.

See [the capture contract](measurement-boundaries.md), [the lifecycle explanation](../Explanations/experiment-lifecycle.md),
and [the procedure](../How_Tos/capture-cooperative-measurement.md).
