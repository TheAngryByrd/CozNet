# Nominal timer credit capture

Evidence: `artifacts/timer-nominal-credit/20261016T131125614-32b1c50d`.

The declared Linux x64 capture uses nominal-event credit, managed preparation, native preparation, and the 10 µs initial timer interval.
The runner retains both cases. It returns exit 1 because the treatment fails the existing debt gate.
All source, binary, and retained replay hashes remain unchanged.

| Case | Workload exit | Counter and credit replay | Selected peak debt (ns) | Peer peak debt (ns) | Native close |
| --- | ---: | --- | ---: | ---: | --- |
| 0% | 0 | Pass | 0 | 0 | Complete |
| 50% | 1 | Pass | 303913 | 2361731 | Complete |

Both preparations record an 80,000 ns synthetic period, 40,000 ns credit, 30,000 ns selected wait, and 70,000 ns peer wait.
Both live measurements start with zero credit and epoch 1. Every retained live sample has a 1,000,000 ns period.
The zero-speedup case has 1,913 qualified events and zero credit.
The treatment has 1,026 qualified events and 513,000,000 ns credit across 18 recorded epochs.
Neither case excludes first events because of an epoch boundary.

The treatment retains `The service lease debt exceeds its capacity bound.` during measurement.
Its 2,361,731 ns peer peak exceeds the unchanged 2,000,000 ns limit.
Counter and credit replay remain valid for the failed record. They do not convert the failed workload into a passing case.
No unchanged timing retry or matched prediction series follows this capture.

## Limits and next step

This capture verifies successful nominal preparation, live reset, policy binding, and retained accounting replay.
It does not establish stable capacity, exact source membership, general CLR safety, or physical prediction accuracy.
The next investigation must explain the retained debt and payment timing before another matched series.
All debt, lease, child, handler, and receipt bounds remain unchanged.

The Release workload build, portable timer checks, and strict documentation build pass.
Python checks pass: counter 31, membership 15, matched prediction 35, and bound selected payment 110.
Both adversarial reviews identified the mutable top-level period on failed preparation. The implementation now declares the fixed live period.
Injected preparation failure remains untested. Failed preparation can retain synthetic rows that live replay rejects while preserving raw failure details.

See [the policy reference](../../../docsSrc/Reference/nominal-event-credit.md), [capture procedure](../../../docsSrc/How_Tos/run-nominal-timer-credit.md), and [estimator explanation](../../../docsSrc/Explanations/nominal-event-credit.md).
