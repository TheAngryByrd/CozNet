# Cooperative progress marker

The native `CooperativeTarget` has an optional progress marker.
It records the last reached instrumentation point for one request generation.
The selected-payment workload can capture this diagnostic through the C ABI.

## Activation and query

`registerCurrentThread` accepts `observeProgress` after `observeCpu`.
Both arguments default to `false` and remain fixed for the target lifetime.
Disabled targets skip marker writes.

`progressEnabled()` reports activation.
`progress(generation)` returns an optional `CooperativeProgressSnapshot`.
Disabled, zero, stale, or released generations return no snapshot.
The query reads the outstanding generation before and after one atomic marker load.
All three generations must match.
The C++ caller must keep the target alive during the query.
The C ABI query holds the shared session lifetime guard.

| Field | Meaning |
| --- | --- |
| `generation` | Outstanding request generation. |
| `ordinal` | Last admitted child ordinal. Zero means no recorded admission. |
| `phase` | Last recorded instrumentation point. |

## Phases

| Phase | Recorded point |
| --- | --- |
| `pending` | Submission initialized the marker before announcing the request. |
| `poll_entered` | The owner passed role and reentrancy checks and read the announced generation. |
| `code_guard_acquired` | The owner acquired the shared code guard. |
| `child_admitted` | The lease admitted a child. This does not require a wait. |
| `wait_entered` | The owner is about to call the timing wait. |
| `wait_returned` | The timing wait returned, including unsuccessful waits. |
| `child_published` | The lease accepted publication of the child. |
| `returning` | A nonterminal poll reached its final path. |
| `quiesced` | Lease retirement and mailbox quiescence succeeded. |
| `failed` | The poll reached a failure return without a retained quiescence marker. |

Phases retain the last admitted ordinal until another child is admitted.
The ordinal is not a completed-child count.
The marker retains no phase history.

## Ownership and limits

Generation-checked atomic updates prevent an old poll from overwriting a new request.
Terminal markers survive later epilogue updates.
`failed` can advance to `quiesced`; `quiesced` cannot advance within the same generation.
Post-quiescence updates use local values and the marker only.
The `quiesced` marker can appear after receipt readiness and after a controller snapshot.
The implementation requires lock-free 64-bit atomics, as does the existing mailbox.

A snapshot does not authorize journal access, receipt acceptance, release, or payment.
`quiesced` can describe a failed or cancelled request.
The receipt remains authoritative for accounting and failure details.

A phase can become stale immediately after the query.
It does not describe continuous execution or establish scheduling cause.
`poll_entered` identifies a native poll point, not entry to the managed injected probe.
Marker writes affect timing.
The `wait_entered` and `wait_returned` writes fall inside the existing measured wait interval.
Other phase writes occur outside that interval.
The diagnostic does not change debt, child, handler, or receipt limits.

See [retained peer-progress analysis](cooperative-peer-progress.md).

## C ABI

`coz_cooperative_register_progress(session, slot, worker, site, observe_cpu)` enables the marker.
`observe_cpu` accepts `0` or `1` and controls the separate CPU journal.
Existing registration functions leave progress disabled.

`coz_cooperative_progress(session, slot, generation, progress)` accepts calls from any thread.
It returns three 64-bit fields: `generation`, `ordinal`, and `phase`.
Phase numbers follow the table order, from `pending = 1` to `failed = 10`.
The query clears the output before validation.
It rejects generations wider than 32 bits before conversion.

| Status | Meaning |
| --- | --- |
| `COZ_COOPERATIVE_OK` (`0`) | A matching observation was returned. |
| `COZ_COOPERATIVE_INVALID` (`1`) | Invalid session, output, slot, or request generation. |
| `COZ_COOPERATIVE_DISABLED` (`8`) | The registered target has no progress marker. |

## Managed evidence

`COZNET_PEER_PROGRESS=1` enables capture for the peer slot only.
The runner exposes the same setting as `-PeerProgress` and requires `-SelectedPayment`.
The manifest and outcome checks bind the requested activation to the record.

| Field | Meaning |
| --- | --- |
| `peerProgressEnabled` | Requested diagnostic activation. |
| `peerOwner`, `peerOwnerFailure` | Peer OS identity, or its capture error. |
| `peerSubmitProgress` | Latest successful submission observation. |
| `peerDebtProgress` | First peer observation after either selected or peer debt fails, before failure handling. |

Each observation retains slot, command, generation, budget, request start, and peer identity.
The debt observation also retains the original debt timestamp.
`Capture.Started` and `Capture.Finished` bracket the native query with managed timestamps.
These timestamps do not bound the time when the owner wrote the phase.
`Capture.Status`, `Snapshot`, and `Failure` preserve the query result or diagnostic error.
`Unavailable` identifies a missing pending request.

The peer can advance between the debt timestamp and the diagnostic query.
The snapshot describes failure capture, not the exact debt observation time.
Diagnostic errors do not replace the workload failure.
The selected-payment analyzer checks activation, request binding, phases, and capture order.
Child phases require a nonzero admitted ordinal.
Submission queries must finish before the corresponding receipt observation or failure capture.

See [how to capture peer progress](../How_Tos/capture-cooperative-peer-progress.md).
