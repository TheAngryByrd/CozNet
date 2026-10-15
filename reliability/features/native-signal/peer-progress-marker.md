# Cooperative progress marker evidence

The opt-in marker records native phases without receipt or release authority.
The managed workload retains the latest submission query and first failed-debt query.

## Controlled checks

All 14 native checks pass on Windows and Linux.
The progress API case checks any-thread access, generation width, disabled queries,
output clearing, retirement, and unchanged release authorization.
Managed interop passes on both platforms for pending, quiesced, and released generations.
Managed capture checks retain query errors and reject malformed successful markers.
The selected-payment replay has 68 passing checks, including capture order and request identity.

## Sampled run

Retained run: `artifacts/peer-progress-marker/20261015T232551957-cc3e857b`.
The run enables peer progress, direct readiness, dispatch timing, owner draining,
and qualified-sample cadence. It does not restrict the CPU set.
All source and binary hashes remain unchanged during the run.

| Case | Workload outcome | Replay |
| --- | --- | --- |
| Zero speedup | Passed | Verified |
| 50 percent | Failed the 100 ms receipt interval | Verified |
| Invalidation after selected payment | Passed expected rejection | Verified |

The failed treatment submitted peer command 1, generation 1, with a 500,000 ns budget.
Its submission snapshot reports `pending`, ordinal zero.
The managed query bracket is `[454659944814679, 454659945593060]` at 1 GHz.
The request start is `454659927223144`.
This bracket describes observation time, not when the native phase was written.
The last observed peer debt is 500,000 ns.
No failed debt gate occurred, so `peerDebtProgress` is absent.

The marker does not establish the cause of the receipt delay.
This run does not verify live failed-debt capture or stable capacity.
The earlier receipt-readiness comparison used different diagnostic settings.
Do not treat these runs as an isolated overhead comparison.

See [the marker reference](../../../docsSrc/Reference/cooperative-progress-marker.md)
and [retained peer-progress analysis](peer-progress.md).

## One-CPU capture

Retained run: `artifacts/peer-progress-marker-single/20261015T233134078`.
The run uses the preceding source and binary manifest with verified unchanged hashes.
Its retained runner fixes CPU affinity to CPU 0 and requests 50,000 batches per case.
This changes both scheduling capacity and runtime CPU policy.
It is a diagnostic scenario, not an isolated overhead comparison.

Zero speedup passes. The treatment fails at 2,499,917 ns of peer debt.
The invalidation case fails at 2,500,000 ns before reaching its intended scenario.
All three records pass replay.

Both failed cases retain `pending`, ordinal zero, at submission and after the failed debt check.
The treatment snapshot binds command 2, generation 2, and a 999,917 ns request.
Its debt timestamp is `454976859025116`.
Its query bracket is `[454976859197138, 454976859198700]` at 1 GHz.
The query starts 172,022 ns after the debt timestamp.
The invalidation snapshot binds command 1, generation 1, and a 500,000 ns request.
Its query starts 175,691 ns after its debt timestamp.

These records exercise live failed-debt capture before later failure handling.
They show no recorded native poll entry for the pending generation at query time.
They do not prove that the peer received no CPU or explain why it did not reach that point.
Scheduler evidence is still required.
The live missing-pending branch remains unexercised; replay fixtures cover its explicit unavailable representation.
This diagnostic branch has no accounting or release authority.

## Remaining coverage limits

Concurrent generation replacement has a stress check with an active writer.
This check does not force each compare-exchange interleaving.
The generation-checked CAS and post-quiescence local reads also receive source review.
Transient poll, code-guard, wait-return, and child-publication writes lack direct placement checks.
The held-wait and terminal checks cover the durable observations used by this diagnostic.

Live failed-debt capture now has retained evidence.
A controlled workload test still does not inject a diagnostic failure at that exact branch or repeat the failed gate.
Managed capture checks retain query exceptions; source review verifies the later original debt throw.
These coverage gaps are accepted for this opt-in diagnostic, which has no accounting or release authority.
They do not establish diagnostic completeness under every interleaving.
Marker storage and disabled-path branches can also affect cost.
No isolated overhead benefit is established.
