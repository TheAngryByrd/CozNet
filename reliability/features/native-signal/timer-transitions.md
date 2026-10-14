# Timer transition evidence

The retained run is `artifacts/timer-transitions/20261014T211743327-e25ecb0d`.
`LinuxTimerTransitions` failed after retaining all eight cases.
The portable layout checks passed on Windows and Linux.
The manifest source and binary hashes remained unchanged during the run.

| Case | Result | Recorded generations | Paid children | Confirmed wait (ns) |
| --- | --- | ---: | ---: | ---: |
| JIT, no GC | Pass | 128 | 0 | 0 |
| JIT, GC | Pass | 128 | 0 | 0 |
| Native wait, no GC | Pass | 128 | 0 | 0 |
| Native wait, GC | Pass | 128 | 0 | 0 |
| Managed wait, no GC | Pass | 128 | 0 | 0 |
| Managed wait, GC | Pass | 128 | 0 | 0 |
| Transitions, no GC | Pass | 128 | 39 | 615,066 |
| Transitions, GC | Fail | 27 | 18 | 156,788 |

All final journal sums equal their command's confirmed wait.
Every target joined, every client closed, and every retained accounting state has zero pending reservations.
All four GC cases contain a complete forced collection inside a command interval.
The largest collection call is 1.836836 ms.
The largest recorded handler interval is 53,330 ns.
The largest first receipt bound is 6,736,665 ns.

## Retained failure

The forced-GC transition case fails with `Synthetic selected credit must not wait.`
Step 27 reserves 4,898 ns and confirms 26,686 ns of actual wait.
Cumulative peer wait reaches 156,788 ns.
Step 28 adds selected credit to 140,000 ns, then publishes the peer overshoot and records a skipped lease.
Step 29 adds selected credit to 145,000 ns, below the global delay of 156,788 ns.
The ledger therefore requires selected-side payment of 11,788 ns.
The synthetic-credit helper rejects that request before another native submission.

This failure exposes the harness assumption that selected credit always covers global delay.
The ledger preserves overshoot as designed.
The harness needs selected-side debt service before this case can establish complete transition coverage.
Discarding the overshoot or increasing synthetic credit would change the experiment.
The run was not repeated.

## Scope

JIT cases interrupted executable JIT image addresses and completed thousands of dynamic methods.
Wait cases reached their controlled waits and applied no delay.
The no-GC transition case interrupted both selected code and native fixture code.
These observations do not establish general CLR safety, sampled capacity, or physical prediction accuracy.
GC command intervals do not isolate individual native waits or runtime suspensions.

See [the procedure](../../../docsSrc/How_Tos/run-timer-transitions.md) and [the record reference](../../../docsSrc/Reference/timer-transitions.md).

## Selected-side recovery

The revised matrix is `artifacts/timer-transitions/20261014T212709243-8714e333`.
All eight cases pass with unchanged input hashes.
Each case retains 128 peer generations.
JIT and wait cases have zero paid children and zero selected payments.
Both transition cases require positive selected payment before they can pass.

| Case | Paid peer children | Selected payments | Selected confirmed wait (ns) |
| --- | ---: | ---: | ---: |
| Transitions, no GC | 54 | 52 | 49,752 |
| Transitions, GC | 69 | 68 | 7,815 |

Every final selected and peer journal sums to its confirmed wait.
Both targets join in every case, both registrations release their results, and every accounting state has zero pending reservations.
The largest recorded handler interval is 60,305 ns.
The largest first receipt bound is 7,462,261 ns.
All four GC cases reach a complete collection inside a peer command interval.

An earlier revised run, `artifacts/timer-transitions/20261014T212552717-4971d638`, passed all cases but performed no selected payments.
It does not verify live selected payment.
The current workload publishes peer overshoot after each completed lease and services selected debt before the next synthetic credit.
This explicit check schedule exercises the recovery path without discarding overshoot or increasing credit.
The separate selected arithmetic worker changes the workload's CPU demand, so timings are not a controlled comparison with the original run.

The arithmetic regression also passes with and without forced GC:

| Run | Peer generations | Paid peer children | Selected payments | Selected confirmed wait (ns) |
| --- | ---: | ---: | ---: | ---: |
| `artifacts/timer-managed/20261014T212836177-194e51fd` | 16 | 48 | 16 | 2,230 |
| `artifacts/timer-managed-gc/20261014T212808054-12f5b61b` | 16 | 48 | 16 | 2,525 |

These runs verify controlled timer delivery and accounting recovery.
They do not establish sampled capacity, general CLR safety, or prediction accuracy.
