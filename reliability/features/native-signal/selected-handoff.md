# Selected scheduling handoff comparison

Retained comparison: `artifacts/selected-handoff/20261016T003856284`.
The retained plan declares ordinary CPUs off/on, then CPU 0 off/on.
Each configuration requests 50,000 batches and runs zero speedup, treatment, and invalidation after selected payment.
All configurations use qualified-sample cadence, owner draining, direct readiness, peer progress, and scheduler capture.
Source and binary hashes remain unchanged during the comparison.

## Results

| Configuration | Treatment outcome | Handoff attempts | Completed selected batches |
| --- | --- | ---: | ---: |
| Ordinary CPUs, off | Passed | 0 | 50,000 |
| Ordinary CPUs, on | Passed | 1,047 | 50,000 |
| CPU 0, off | Passed | 0 | 50,000 |
| CPU 0, on | Failed handler bound | 52 | 338 |

All four zero-speedup cases pass with zero handoff attempts.
All four intended invalidation cases pass. Those cases record zero handoff attempts, so they do not cover invalidation after handoff.
All twelve selected-payment records and scheduler analyses verify.
The aggregate comparison remains failed.
The ordinary runs also pass all 14 native checks and the three managed verification routes.
Windows managed checks pass, including the five handoff policy checks.

The result does not establish a capacity improvement.
The default remains disabled. No unchanged timing failure was retried.
The fixed order and single observation per configuration do not establish statistical causation or isolated overhead.

## Failed child

The CPU 0 handoff treatment retains peer command 5, generation 5, with a 499,710 ns budget.
Its first child requested 50,000 ns.
The child records `Started=459110228088501` and `Finished=459110236332986` in native nanoseconds.
The child span is 8,244,485 ns. The measured wait is 8,243,570 ns.
The native header reports `Error=4`, which is `handler_bound`.
The child has `Valid=0`, and the header has `Measured=0`.
The managed adapter rejects that journal. It does not settle the failed child as payment.

The last debt observation has 999,710 ns of peer debt and zero selected debt.
No failed debt gate occurred, so `peerDebtProgress` is absent.
The latest submission marker reports `pending`, ordinal zero.
It precedes the later child and does not describe its wait phase.

One closed peer switch-out interval overlaps 2,687,750 ns of the child span.
That interval is [459110233642578, 459110236330328), with the preemption flag set.
This overlap does not explain the full span or establish why the handler exceeded its bound.
It does not authorize debt forgiveness or replacement of wall-time payment.

## Coverage and limits

Five portable policy checks cover parsing, threshold boundaries, pending state, caller-thread execution, repeated admission, exceptions, and unavailable backends.
Seventy-two selected-payment replay checks pass, including four handoff checks.
The positive-counter unit fixture checks handoff fields only. It does not claim full accounting validity for that reduced fixture.
The retained ordinary treatment supplies full replay evidence with positive handoff attempts.
The outcome fixtures and all fifteen scheduler checks also pass.

The complete workload has no portable injected failure check at the handoff checkpoint.
The live record retains a later native failure, not an injected backend exception.
Portable checks prove one backend call per policy invocation on the calling thread.
They do not prove one invocation per workload checkpoint or the subsequent drain and service order.
Replay checks qualifying aggregate evidence. It cannot reject every inflated attempt count or reconstruct each scheduling decision.
These coverage limits are accepted for this default-off experiment. They do not qualify production use or prediction accuracy.
Enabled mode adds a debt observation before qualification. Zero attempts do not establish zero added overhead or unchanged failure-detection timing.
No independent handoff overhead bound or physical prediction comparison is established.
Claude and Codex reviews found no confirmed defects against `767673348961c0165748df76aee7210ec319d046`.
Both cleared this default-off experiment with the stated coverage limits. Neither review establishes capacity or prediction accuracy.

See [the handoff reference](../../../docsSrc/Reference/selected-handoff.md),
[the comparison procedure](../../../docsSrc/How_Tos/compare-selected-handoff.md), and [the preceding scheduler evidence](bound-scheduler.md).
